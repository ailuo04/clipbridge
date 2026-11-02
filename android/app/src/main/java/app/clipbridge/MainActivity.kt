package app.clipbridge

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.InputType
import android.view.WindowInsets
import android.widget.*
import app.clipbridge.network.BridgeService
import app.clipbridge.network.Peer
import app.clipbridge.protocol.BridgeProtocol
import app.clipbridge.storage.PairingStore

class MainActivity : Activity() {
    private lateinit var store: PairingStore
    private lateinit var keyInput: EditText
    private lateinit var addressInput: EditText
    private lateinit var statusView: TextView
    private lateinit var pairedView: TextView
    private lateinit var deviceSpinner: Spinner
    private lateinit var transferList: LinearLayout
    private lateinit var received: Button
    private var devices = listOf<Peer>()
    private lateinit var sharedSend: Button
    private var sharedUris = emptyList<Uri>()
    private var sharedText: String? = null
    private var lastServiceStatus: String? = null
    private var signature = ""
    private var transferSignature = ""
    private val handler = Handler(Looper.getMainLooper())
    private val refresh = object : Runnable { override fun run() { refreshState(); handler.postDelayed(this, 1000) } }
    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
    private fun label(text: String, size: Float = 16f) = TextView(this).apply { this.text = text; textSize = size; setTextColor(0xff172033.toInt()); setPadding(0, dp(8), 0, dp(8)); setTextIsSelectable(true) }
    private fun action(text: String, block: () -> Unit) = Button(this).apply { this.text = text; isAllCaps = false; setOnClickListener { try { block() } catch (e: Exception) { statusView.text = e.message ?: "操作失败，请重试" } } }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState); store = PairingStore(this)
        val scroll = ScrollView(this).apply { setBackgroundColor(0xfff2f5fa.toInt()); isFillViewport = true }
        val body = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(20), dp(16), dp(20), dp(24)) }
        scroll.addView(body); setContentView(scroll)
        scroll.setOnApplyWindowInsetsListener { view, insets ->
            if (Build.VERSION.SDK_INT >= 30) { val bars = insets.getInsets(WindowInsets.Type.systemBars()); view.setPadding(bars.left, bars.top, bars.right, bars.bottom) }
            else { @Suppress("DEPRECATION") view.setPadding(insets.systemWindowInsetLeft, insets.systemWindowInsetTop, insets.systemWindowInsetRight, insets.systemWindowInsetBottom) }
            insets
        }
        body.addView(label("互传 ClipBridge", 28f))
        body.addView(label("主动发送，安静接收。两台设备连接同一 Wi-Fi 或手机热点。", 14f))
        statusView = label("尚未连接", 14f); body.addView(statusView)
        pairedView = label("", 14f); body.addView(pairedView)
        keyInput = EditText(this).apply { hint = "电脑显示的 64 位配对密钥"; inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD; maxLines = 2 }
        body.addView(keyInput)
        addressInput = EditText(this).apply { hint = "电脑 IP（发现失败时填写）"; setText(store.peer()); inputType = InputType.TYPE_CLASS_TEXT; isSingleLine = true }
        body.addView(addressInput)
        body.addView(action("保存配对", {
            check(BridgeService.current == null) { "请先停止连接，再修改配对" }
            store.save(keyInput.text.toString(), addressInput.text.toString()); keyInput.text.clear(); statusView.text = "配对已保存，可以开始连接"; refreshState()
        }))
        val connectionButtons = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        connectionButtons.addView(action("开始连接", { startConnection() }), LinearLayout.LayoutParams(0, dp(52), 1f))
        connectionButtons.addView(action("停止连接", { stopService(Intent(this, BridgeService::class.java)); statusView.text = "连接已停止" }), LinearLayout.LayoutParams(0, dp(52), 1f))
        body.addView(connectionButtons)
        body.addView(label("目标设备", 20f))
        deviceSpinner = Spinner(this); body.addView(deviceSpinner)
        body.addView(action("发送剪贴板", {
            check(hasWindowFocus()) { "请在当前互传界面操作剪贴板" }
            val clipboard = getSystemService(ClipboardManager::class.java)
            val text = clipboard.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.text?.toString()
            require(!text.isNullOrEmpty()) { "剪贴板没有纯文本" }
            previewText(text)
        }))
        body.addView(action("选择文件发送", {
            requireService(); selectedHost()
            startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply { type = "*/*"; addCategory(Intent.CATEGORY_OPENABLE); putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true); addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION) }, 42)
        }))
        sharedSend = action("发送分享的内容", {
            if (sharedUris.isNotEmpty()) confirmFiles(sharedUris) else sharedText?.let { previewText(it) }
        }).apply { visibility = android.view.View.GONE }
        body.addView(sharedSend)
        received = action("复制最近收到的文字", {
            val text = requireService().receivedText ?: error("还没有收到文字")
            getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("ClipBridge", text)); statusView.text = "已复制收到的文字"
        }); body.addView(received)
        body.addView(label("接收请求与传输记录", 20f))
        body.addView(label("收到文件不会弹出界面。请在此点击接受；请求约 100 秒后超时。文件保存到 Download/ClipBridge。", 13f))
        transferList = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }; body.addView(transferList)
        body.addView(action("打开下载目录", { startActivity(Intent("android.intent.action.VIEW_DOWNLOADS")) }))
        body.addView(action("撤销配对", {
            AlertDialog.Builder(this).setTitle("撤销配对？").setMessage("将停止连接并删除此手机保存的配对信息。").setNegativeButton("取消", null).setPositiveButton("撤销") { _, _ -> stopService(Intent(this, BridgeService::class.java)); store.clear(); statusView.text = "配对已撤销" }.show()
        }))
        body.addView(label("Android " + Build.VERSION.RELEASE + " · " + Build.MODEL + "\n手机发送时需要打开本界面。后台仅保持连接，不读取剪贴板。", 12f))
        handleShare(intent)
    }
    override fun onStart() { super.onStart(); handler.post(refresh) }
    override fun onStop() { handler.removeCallbacks(refresh); super.onStop() }
    override fun onNewIntent(intent: Intent) { super.onNewIntent(intent); setIntent(intent); handleShare(intent) }
    private fun requireService(): BridgeService = BridgeService.current ?: error("请先点击开始连接")
    private fun startConnection() {
        check(store.load() != null) { "请先保存电脑的配对密钥" }
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 8)
        startForegroundService(Intent(this, BridgeService::class.java)); statusView.text = "正在连接…"
    }
    private fun selectedHost(): String {
        val manual = addressInput.text.toString().trim()
        val host = if (deviceSpinner.selectedItemPosition > 0) devices.getOrNull(deviceSpinner.selectedItemPosition - 1)?.address ?: manual else manual
        require(BridgeProtocol.isLocal(host)) { "请选择电脑，或输入电脑的局域网 IPv4 地址" }; return host
    }
    private fun previewText(text: String) {
        requireService(); val host = selectedHost(); require(text.toByteArray().size <= BridgeProtocol.MAX_TEXT) { "文字超过 256 KiB" }
        val view = TextView(this).apply { this.text = text; setPadding(dp(16), dp(12), dp(16), dp(12)); setTextIsSelectable(true) }
        val scroller = ScrollView(this).apply { addView(view) }
        AlertDialog.Builder(this).setTitle("发送到 $host · ${text.toByteArray().size} 字节").setView(scroller).setNegativeButton("取消", null).setPositiveButton("确认发送") { _, _ ->
            try { requireService().sendText(host, text) } catch (e: Exception) { statusView.text = e.message }
        }.show()
    }
    @Deprecated("Uses platform document picker without an extra runtime library")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == 42 && resultCode == RESULT_OK && data != null) {
            val uris = mutableListOf<Uri>(); data.clipData?.let { clip -> repeat(clip.itemCount) { uris.add(clip.getItemAt(it).uri) } }; data.data?.let(uris::add)
            if (data.flags and Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION != 0) uris.forEach { runCatching { contentResolver.takePersistableUriPermission(it, Intent.FLAG_GRANT_READ_URI_PERMISSION) } }
            confirmFiles(uris.distinct())
        }
    }
    private fun confirmFiles(uris: List<Uri>) {
        try {
            require(uris.size in 1..256) { "请选择 1 至 256 个文件" }; requireService(); val host = selectedHost()
            AlertDialog.Builder(this).setTitle("发送 ${uris.size} 个文件？").setMessage("目标：$host\n发送前将准备并校验文件；可以在传输列表中取消。").setNegativeButton("取消", null).setPositiveButton("发送") { _, _ ->
                try { requireService().sendFiles(host, uris) } catch (e: Exception) { statusView.text = e.message }
            }.show()
        } catch (e: Exception) { statusView.text = e.message }
    }
    @Suppress("DEPRECATION")
    private fun handleShare(value: Intent?) {
        if (value?.action !in listOf(Intent.ACTION_SEND, Intent.ACTION_SEND_MULTIPLE)) return
        val streams = if (value?.action == Intent.ACTION_SEND_MULTIPLE) value.getParcelableArrayListExtra<Uri>(Intent.EXTRA_STREAM) ?: arrayListOf() else listOfNotNull(value?.getParcelableExtra<Uri>(Intent.EXTRA_STREAM))
        sharedUris = streams
        sharedText = value?.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString()
        sharedSend.visibility = android.view.View.VISIBLE
        sharedSend.text = if (streams.isNotEmpty()) "发送分享的 " + streams.size + " 个文件" else "发送分享的文字"
        statusView.text = "已收到系统分享，请开始连接并选择设备后主动发送"
        value?.action = null
    }
    private fun refreshState() {
        pairedView.text = if (store.load() != null) "已保存配对密钥 · Android Keystore 保护" else "尚未配对"
        val service = BridgeService.current
        if (service != null && service.status != lastServiceStatus) { statusView.text = service.status; lastServiceStatus = service.status }
        else if (service == null && lastServiceStatus != null) { statusView.text = "连接已停止"; lastServiceStatus = null }
        received.isEnabled = service?.receivedText != null
        val latest = service?.peers?.values?.sortedBy { it.address } ?: emptyList()
        val newSignature = latest.joinToString { it.address + it.name }
        if (signature != newSignature || deviceSpinner.adapter == null) {
            val selected = devices.getOrNull(deviceSpinner.selectedItemPosition - 1)?.address
            devices = latest; signature = newSignature
            deviceSpinner.adapter = ArrayAdapter(this, android.R.layout.simple_spinner_dropdown_item, listOf("使用上方手动 IP / 选择设备") + latest.map(Peer::toString))
            latest.indexOfFirst { it.address == selected }.takeIf { it >= 0 }?.let { deviceSpinner.setSelection(it + 1) }
        }
        val requests = service?.pending?.values?.toList() ?: emptyList()
        val progress = service?.transfers?.values?.toList() ?: emptyList()
        val next = requests.joinToString { it.offer.id } + progress.joinToString { it.toString() }
        if (next == transferSignature) return
        transferSignature = next; transferList.removeAllViews()
        requests.forEach { request ->
            transferList.addView(label("待接收 · ${request.address}\n" + request.offer.files.joinToString("\n") { "${it.name} · ${it.size} 字节" }, 14f))
            val actions = LinearLayout(this)
            actions.addView(action("接受", { request.decision.complete(true) }))
            actions.addView(action("拒绝", { request.decision.complete(false) })); transferList.addView(actions)
        }
        progress.filter { row -> requests.none { it.offer.id == row.id } }.takeLast(30).forEach { row ->
            transferList.addView(label("${row.direction} · ${row.state}\n${row.done} / ${row.total} 字节", 14f))
            if (row.state in listOf("传输中", "准备文件", "等待对方接受")) transferList.addView(action("取消", { service?.cancel(row.id) }))
        }
    }
}
