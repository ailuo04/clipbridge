package app.clipbridge

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.text.InputType
import android.view.*
import android.widget.*
import app.clipbridge.compat.PlatformCompat
import app.clipbridge.network.*
import app.clipbridge.protocol.BridgeProtocol
import app.clipbridge.storage.PairingStore
import app.clipbridge.ui.BridgeUi
import app.clipbridge.updates.UpdateRepository

class MainActivity : Activity() {
    private lateinit var store: PairingStore
    private lateinit var ui: BridgeUi
    private lateinit var updates: UpdateRepository
    private lateinit var body: LinearLayout
    private lateinit var scroll: ScrollView
    private lateinit var navigation: LinearLayout
    private lateinit var notice: TextView
    private val handler=Handler(Looper.getMainLooper())
    private val prefs by lazy { getSharedPreferences("interface",MODE_PRIVATE) }
    private var page=0
    private var selected=""
    private var signature=""
    private var latestStatus=""
    private var resumed=false
    private var sharedUris=emptyList<Uri>()
    private var sharedText: String?=null
    private var filter=0
    private data class Target(val id: String,val name: String,val address: String,val online: Boolean)
    private val tick=object : Runnable { override fun run() { refresh(); handler.postDelayed(this,1000) } }
    override fun onCreate(savedInstanceState: Bundle?) {
        val mode=prefs.getInt("theme",0)
        if(mode!=0) applyOverrideConfiguration(Configuration().apply { uiMode=if(mode==1) Configuration.UI_MODE_NIGHT_NO else Configuration.UI_MODE_NIGHT_YES })
        super.onCreate(savedInstanceState)
        store=PairingStore(this); ui=BridgeUi(this); updates=UpdateRepository.get(this)
        page=savedInstanceState?.getInt("page") ?: 0; selected=prefs.getString("selected","") ?: ""
        val root=ui.column().apply { setBackgroundColor(ui.canvas) }
        notice=ui.text("主动发送，安静接收。",12f,ui.muted).apply { setPadding(ui.dp(22),ui.dp(8),ui.dp(22),ui.dp(8)); accessibilityLiveRegion=View.ACCESSIBILITY_LIVE_REGION_POLITE }
        scroll=ScrollView(this).apply { isFillViewport=true; clipToPadding=false }
        body=ui.column(22); scroll.addView(body)
        navigation=ui.row().apply { setPadding(ui.dp(12),ui.dp(8),ui.dp(12),ui.dp(8)); setBackgroundColor(ui.surface) }
        root.addView(notice); root.addView(scroll,LinearLayout.LayoutParams(-1,0,1f)); root.addView(navigation)
        setContentView(root)
        root.setOnApplyWindowInsetsListener { view,insets ->
            if(Build.VERSION.SDK_INT>=30) { val bars=insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout()); view.setPadding(bars.left,bars.top,bars.right,bars.bottom) }
            else { @Suppress("DEPRECATION") view.setPadding(insets.systemWindowInsetLeft,insets.systemWindowInsetTop,insets.systemWindowInsetRight,insets.systemWindowInsetBottom) }; insets
        }
        handleShare(intent); render(); updates.schedule()
    }
    override fun onStart() { super.onStart(); handler.post(tick) }
    override fun onResume() {
        super.onResume(); resumed=true
        if(prefs.getBoolean("connection",true) && BridgeService.current==null && PlatformCompat.canUseLan(this)) startConnection(false)
        if(updates.autoCheck()) updates.check()
        signature=""; refresh()
    }
    override fun onPause() { resumed=false; super.onPause() }
    override fun onStop() { handler.removeCallbacks(tick); super.onStop() }
    override fun onSaveInstanceState(outState: Bundle) { outState.putInt("page",page); super.onSaveInstanceState(outState) }
    override fun onNewIntent(intent: Intent) { super.onNewIntent(intent); setIntent(intent); handleShare(intent); signature=""; render() }
    private fun message(value: String) { notice.text=value }
    private fun safe(block: ()->Unit) { try { block() } catch(e: Exception) { message(e.message ?: "操作失败，请重试") } }
    private fun action(parent: LinearLayout,label: String,primary: Boolean=false,enabled: Boolean=true,block: ()->Unit): Button = ui.button(label,primary) { safe(block) }.apply {
        isEnabled=enabled; alpha=if(enabled) 1f else .45f
        parent.addView(this,LinearLayout.LayoutParams(-1,-2).apply { topMargin=ui.dp(8) })
    }
    private fun targets(): List<Target> {
        val service=BridgeService.current; val hub=service?.hub
        val current=store.records().map { t -> val p=hub?.nearby?.get(t.id)?.takeIf { it.authenticated && System.currentTimeMillis()-it.seen<=15000 }; Target(t.id,t.name,p?.address ?: t.address,p!=null && t.active) }.toMutableList()
        service?.peers?.values?.filter { System.currentTimeMillis()-it.lastSeen<=15000 && current.none { t->t.address==it.address } }?.forEach { current.add(Target("legacy:"+it.address,it.name,it.address,true)) }
        if(selected.startsWith("manual:") && store.load()!=null) { val host=selected.removePrefix("manual:"); if(BridgeProtocol.isLocal(host) && current.none { it.address==host }) current.add(Target(selected,"手动连接的旧版设备",host,true)) }
        return current.sortedByDescending { it.online }
    }
    private fun target(): Target? {
        val items=targets()
        if(selected.isEmpty()) items.firstOrNull { it.online }?.let { selected=it.id; prefs.edit().putString("selected",selected).apply() }
        return items.firstOrNull { it.id==selected }
    }
    private fun requireService()=BridgeService.current?.takeIf { it.hub!=null } ?: error("请先开启连接服务，或稍等服务准备完成")
    private fun selectedHost(): String { requireService(); return target()?.takeIf { it.online }?.address ?: error("请选择一台已配对且在线的设备") }
    private fun startConnection(askNotification: Boolean=true) {
        if(!PlatformCompat.canUseLan(this)) { PlatformCompat.requestLan(this); message("请允许访问本地网络后再开启连接"); return }
        if(askNotification && Build.VERSION.SDK_INT>=33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED) requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS),8)
        prefs.edit().putBoolean("connection",true).apply(); startForegroundService(Intent(this,BridgeService::class.java)); message("正在搜索同一网络里的设备…")
    }
    private fun stopConnection() { val serviceIntent=Intent(this,BridgeService::class.java); stopService(serviceIntent) }
    private fun refresh() {
        val service=BridgeService.current
        if(service!=null && service.status!=latestStatus) { latestStatus=service.status; message(service.status) }
        val hub=service?.hub
        val next=page.toString()+selected+filter+service?.status+service?.receivedText?.length+targets().joinToString { it.toString() }+hub?.nearby?.values?.sortedBy { it.id }?.joinToString { "${it.id}:${it.address}:${it.authenticated}" }+hub?.pending?.values?.joinToString { "${it.session}:${it.id}:${it.name}:${it.address}:${it.code}:${it.expires}:${it.decision.isDone}" }+service?.pending?.keys?.joinToString()+service?.transfers?.values?.joinToString()+updates.state+updates.progress+updates.ready
        if(next!=signature) { signature=next; render() }
    }
    private fun render() {
        val oldY=scroll.scrollY; body.removeAllViews(); navigation.removeAllViews()
        listOf("设备","传输","设置").forEachIndexed { index,label ->
            val tab=ui.column(8).apply { gravity=Gravity.CENTER; minimumHeight=ui.dp(64); background=ui.background(if(index==page) ui.tint else ui.surface,16); isClickable=true; isFocusable=true; contentDescription=label+(if(index==page) "，已选择" else "") }
            tab.addView(ui.icon(listOf(0,3,4)[index],if(index==page) ui.primary else ui.muted),LinearLayout.LayoutParams(ui.dp(25),ui.dp(25)))
            val pending=BridgeService.current?.pending?.size ?: 0
            tab.addView(ui.text(if(index==1 && pending>0) "$label · $pending" else label,12f,if(index==page) ui.primary else ui.muted,index==page))
            tab.setOnClickListener { page=index; signature=""; scroll.scrollTo(0,0); render() }
            navigation.addView(tab,LinearLayout.LayoutParams(0,-2,1f).apply { marginStart=ui.dp(4); marginEnd=ui.dp(4) })
        }
        body.addView(ui.text(listOf("互传","传输","设置")[page],30f,bold=true))
        body.addView(ui.text(listOf("连接你的设备，让内容自由流动。","收到的文件，由你确认后保存。","把连接调成你喜欢的样子。")[page],13f,ui.muted)); ui.gap(body,18)
        when(page) { 0->devicesPage(); 1->transfersPage(); 2->settingsPage() }
        ui.gap(body,18); scroll.post { scroll.scrollTo(0,oldY) }
    }
    private fun devicesPage() {
        val service=BridgeService.current; val hub=service?.hub
        hub?.pending?.values?.sortedBy { it.expires }?.forEach { request ->
            val card=ui.card(body,ui.mint)
            card.addView(ui.text("与 ${request.name} 配对",18f,bold=true)); card.addView(ui.text("请在两台设备上核对相同数字",13f,ui.green))
            card.addView(ui.text(request.code,36f,ui.primary,true).apply { setTextIsSelectable(true); contentDescription="配对验证码 "+request.code.toCharArray().joinToString(" ") })
            card.addView(ui.text(request.address,12f,ui.muted))
            if(!request.decision.isDone) { action(card,"确认数字一致",true) { hub.decide(request,true); signature="" }; action(card,"拒绝 / 数字不一致") { hub.decide(request,false); signature="" } }
            else card.addView(ui.text("已确认，等待另一台设备…",14f,ui.green))
        }
        val current=target()
        val card=ui.card(body)
        val top=ui.row(); top.addView(ui.icon(0),LinearLayout.LayoutParams(ui.dp(45),ui.dp(45)).apply { marginEnd=ui.dp(14) })
        val label=ui.column(); label.addView(ui.text(current?.name ?: "选择你的设备",21f,bold=true)); label.addView(ui.text(if(current?.online==true) "已连接 · ${current.address}" else if(current!=null) "设备离线" else "首次连接无需填写密钥",12f,if(current?.online==true) ui.green else ui.muted)); top.addView(label,LinearLayout.LayoutParams(0,-2,1f)); card.addView(top)
        action(card,if(current==null) "搜索与连接" else "切换设备") {
            if(service==null) startConnection() else chooseTarget()
        }
        val send=ui.row()
        fun sendTile(title: String,subtitle: String,kind: Int,primary: Boolean,block: ()->Unit): LinearLayout {
            val tile=ui.column(16).apply { background=ui.background(if(primary && !ui.dark) ui.primary else ui.surface); minimumHeight=ui.dp(144); isClickable=true; isFocusable=true; contentDescription="$title，$subtitle" }
            val color=if(primary && !ui.dark) android.graphics.Color.WHITE else ui.primary
            tile.addView(ui.icon(kind,color),LinearLayout.LayoutParams(ui.dp(34),ui.dp(34))); ui.gap(tile,10)
            tile.addView(ui.text(title,19f,if(primary && !ui.dark) android.graphics.Color.WHITE else ui.ink,true)); tile.addView(ui.text(subtitle,11f,if(primary && !ui.dark) 0xffe0e8ff.toInt() else ui.muted))
            tile.alpha=if(current?.online==true) 1f else .55f; tile.setOnClickListener { safe(block) }; return tile
        }
        send.addView(sendTile("发送文件","照片、视频和文档",1,true,::pickFiles),LinearLayout.LayoutParams(0,-2,1f).apply { marginEnd=ui.dp(6) })
        send.addView(sendTile("发送剪贴板","预览文字后发送",2,false,::readClipboard),LinearLayout.LayoutParams(0,-2,1f).apply { marginStart=ui.dp(6) }); body.addView(send)
        if(sharedUris.isNotEmpty() || sharedText!=null) { val share=ui.card(body,ui.tint); action(share,if(sharedUris.isNotEmpty()) "发送分享的 ${sharedUris.size} 个文件" else "发送分享的文字",true) { if(sharedUris.isNotEmpty()) confirmFiles(sharedUris) else sharedText?.let(::previewText) } }
        if(service?.receivedText!=null) action(body,"复制最近收到的文字") { getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("ClipBridge",requireService().receivedText)); message("已复制收到的文字") }
        ui.heading(body,"我的设备")
        val all=targets()
        if(all.isEmpty()) { val empty=ui.card(body); empty.addView(ui.text("还没有配对设备",16f,bold=true)); empty.addView(ui.text("在下方选择电脑，两端确认验证码即可连接。",13f,ui.muted)) }
        all.forEach { t -> val row=ui.card(body); row.addView(ui.text(t.name,17f,bold=true)); row.addView(ui.text((if(t.online) "已连接" else "离线")+" · "+t.address,12f,if(t.online) ui.green else ui.muted)); action(row,if(selected==t.id) "当前发送目标" else "设为发送目标",selected==t.id,t.online) { selected=t.id; prefs.edit().putString("selected",selected).apply(); signature="" } }
        ui.heading(body,"附近设备",if(service!=null) "正在搜索同一 Wi-Fi 或手机热点里的设备" else "连接服务已停止")
        val nearby=hub?.nearby?.values?.filter { p->store.records().none { it.id==p.id } }?.sortedBy { it.name } ?: emptyList()
        nearby.forEach { peer -> val row=ui.card(body); row.addView(ui.text(peer.name,17f,bold=true)); row.addView(ui.text("${peer.platform} · ${peer.address}",12f,ui.muted)); action(row,"配对",true) { hub?.pair(peer); message("正在建立配对，请在两端核对验证码") } }
        if(nearby.isEmpty()) { val empty=ui.card(body,ui.mint); empty.addView(ui.text("让两台设备连接同一网络",16f,ui.green,true)); empty.addView(ui.text("请打开电脑端新版互传。若持续找不到设备，检查 Wi-Fi 隔离、防火墙或系统本地网络权限。",13f,ui.muted)); action(empty,if(service==null) "开始搜索" else "重新搜索") { if(service==null) startConnection() else { stopConnection(); handler.postDelayed({ safe { startConnection(false) } },600) } } }
    }
    private fun chooseTarget() {
        val items=targets().filter { it.online }; require(items.isNotEmpty()) { "还没有在线设备，请先与附近设备配对" }
        AlertDialog.Builder(this).setTitle("发送到哪台设备").setItems(items.map { "${it.name} · ${it.address}" }.toTypedArray()) { _,which -> selected=items[which].id; prefs.edit().putString("selected",selected).apply(); signature="" }.setNegativeButton("取消",null).show()
    }
    private fun transfersPage() {
        val choices=ui.row(); listOf("全部","发送","接收").forEachIndexed { i,s -> choices.addView(ui.button(s,filter==i) { filter=i; signature=""; render() },LinearLayout.LayoutParams(0,-2,1f).apply { marginEnd=ui.dp(4) }) }; body.addView(choices)
        val service=BridgeService.current; val requests=service?.pending?.values?.toList() ?: emptyList()
        if(filter!=1 && requests.isNotEmpty()) ui.heading(body,"待处理 · ${requests.size}")
        if(filter!=1) requests.forEach { request -> val card=ui.card(body); card.addView(ui.text(request.offer.files.first().name+(if(request.offer.files.size>1) " 等 ${request.offer.files.size} 个文件" else ""),17f,bold=true)); card.addView(ui.text("来自 ${request.address} · ${ui.size(request.offer.total)}",12f,ui.muted)); card.addView(ui.text(request.offer.files.take(6).joinToString("\n") { "${it.name} · ${ui.size(it.size)}" },13f)); action(card,"接受文件",true) { request.decision.complete(true) }; action(card,"拒绝") { request.decision.complete(false) } }
        ui.heading(body,"传输记录","文件保存到 Download/ClipBridge；请求约 100 秒后超时。")
        val rows=service?.transfers?.values?.filter { row -> requests.none { it.offer.id==row.id } && (filter==0 || row.direction==if(filter==1) "发送" else "接收") }?.takeLast(100) ?: emptyList()
        rows.reversed().forEach { row -> val card=ui.card(body); card.addView(ui.text(row.name.ifEmpty { row.direction },16f,bold=true)); card.addView(ui.text("${row.direction} · ${row.state}"+(if(row.address.isNotEmpty()) " · ${row.address}" else ""),12f,ui.muted)); card.addView(ui.text("${ui.size(row.done)} / ${ui.size(row.total)}",12f,ui.muted)); if(row.direction=="接收" && row.state.startsWith("已保存")) action(card,"打开收到的文件") { openReceived(row.id) }; if(row.state in listOf("传输中","准备文件","等待对方接受")) { card.addView(ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal).apply { max=100; progress=if(row.total>0) (row.done*100/row.total).toInt() else 0 }); action(card,"取消") { service?.cancel(row.id) } } }
        if(rows.isEmpty() && requests.isEmpty()) { val empty=ui.card(body); empty.addView(ui.text("还没有传输任务",18f,bold=true)); empty.addView(ui.text("从设备页发送文件，或等待电脑发送请求。",13f,ui.muted)) }
        action(body,"打开接收目录") { startActivity(Intent("android.intent.action.VIEW_DOWNLOADS")) }
    }
    private fun openReceived(id: String) {
        val uris=requireService().receivedFiles(id); require(uris.isNotEmpty()) { "文件记录已清除，请打开接收目录" }
        fun open(uri: Uri) { startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(uri,contentResolver.getType(uri) ?: "application/octet-stream").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)) }
        if(uris.size==1) open(uris.first()) else AlertDialog.Builder(this).setTitle("打开收到的文件").setItems(uris.indices.map { "文件 ${it+1}" }.toTypedArray()) { _,which -> safe { open(uris[which]) } }.show()
    }
    private fun setting(parent: LinearLayout,title: String,detail: String,checked: Boolean,change: (Boolean)->Unit) {
        val row=ui.row(); val labels=ui.column(); labels.addView(ui.text(title,15f,bold=true)); labels.addView(ui.text(detail,11f,ui.muted)); row.addView(labels,LinearLayout.LayoutParams(0,-2,1f))
        row.addView(Switch(this).apply { isChecked=checked; contentDescription=title; minimumWidth=ui.dp(48); minimumHeight=ui.dp(48); setOnCheckedChangeListener { _,value -> safe { change(value) } } }); parent.addView(row); ui.gap(parent,10)
    }
    private fun settingsPage() {
        val profile=ui.card(body); profile.addView(ui.text(store.name(),22f,bold=true)); profile.addView(ui.text("本机名称 · Android ${Build.VERSION.RELEASE}",12f,ui.muted)); action(profile,"修改设备名称") { editName() }
        ui.heading(body,"应用更新","当前版本 ${updates.versionName}")
        val update=ui.card(body,ui.tint); update.addView(ui.text(updates.state,18f,bold=true)); update.addView(ui.text("检查与下载不会弹出界面；安装由你主动发起。",12f,ui.muted))
        if(updates.state.startsWith("正在下载")) update.addView(ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal).apply { progress=updates.progress })
        if(updates.ready) action(update,"安装更新",true,block=::installUpdate)
        else if(updates.available!=null) action(update,"立即下载更新",true) { updates.check(force=true,manualDownload=true) }
        action(update,"检查更新") { updates.check(force=true); message("正在检查更新…") }
        val options=ui.card(body)
        setting(options,"自动检查更新","每天在后台检查，也会在打开时检查",updates.autoCheck()) { updates.settings(it,updates.autoDownload()); signature="" }
        setting(options,"非计费网络自动下载","按流量计费时等待，或由你手动下载",updates.autoDownload()) { updates.settings(updates.autoCheck(),it); signature="" }
        ui.heading(body,"连接与偏好")
        val connection=ui.card(body)
        setting(connection,"保持连接服务","后台只接收和保持连接，不读取剪贴板",prefs.getBoolean("connection",true)) { on -> prefs.edit().putBoolean("connection",on).apply(); if(on) startConnection() else { stopConnection(); message("连接已停止") }; signature="" }
        action(connection,"外观 · "+listOf("跟随系统","浅色","深色")[prefs.getInt("theme",0)]) { AlertDialog.Builder(this).setTitle("外观").setSingleChoiceItems(arrayOf("跟随系统","浅色","深色"),prefs.getInt("theme",0)) { dialog,which -> prefs.edit().putInt("theme",which).apply(); dialog.dismiss(); recreate() }.setNegativeButton("取消",null).show() }
        action(connection,"按 IP 发起配对") {
            val input=EditText(this).apply { hint="例如 192.168.1.8"; isSingleLine=true; inputType=InputType.TYPE_CLASS_PHONE }
            AlertDialog.Builder(this).setTitle("按 IP 发起配对").setMessage("仍需在两端核对验证码，无需输入密钥。").setView(input).setNegativeButton("取消",null).setPositiveButton("开始配对") { _,_ -> safe { requireService().hub!!.pairAddress(input.text.toString().trim()); page=0; signature=""; render() } }.show()
        }
        action(connection,"兼容旧版 / 手动连接",block=::legacyPairing)
        action(connection,"本地网络权限") { if(!PlatformCompat.canUseLan(this)) PlatformCompat.requestLan(this) else message("当前系统允许本地网络访问；未来版本将按系统要求请求权限") }
        ui.heading(body,"已信任的设备")
        store.records().forEach { t -> val card=ui.card(body); card.addView(ui.text(t.name,16f,bold=true)); card.addView(ui.text(t.platform+" · "+if(t.active) "已配对" else "确认恢复中",12f,ui.muted)); action(card,"取消配对") { AlertDialog.Builder(this).setTitle("取消与 ${t.name} 的配对？").setMessage("该设备将无法继续传输，重新连接需要双方再次确认。").setNegativeButton("保留",null).setPositiveButton("取消配对") { _,_ -> safe { BridgeService.current?.hub?.revoke(t.id) ?: store.remove(t.id); signature="" } }.show() } }
        body.addView(ui.text("剪贴板只在你点击发送时读取。后台请求和更新不会打开界面。",12f,ui.muted))
    }
    private fun editName() {
        val input=EditText(this).apply { setText(store.name()); isSingleLine=true; hint="设备名称" }
        AlertDialog.Builder(this).setTitle("修改设备名称").setView(input).setNegativeButton("取消",null).setPositiveButton("保存") { _,_ -> safe { store.setName(input.text.toString()); signature="" } }.show()
    }
    private fun legacyPairing() {
        val content=ui.column(18); val key=EditText(this).apply { hint=if(store.load()!=null) "已保存旧版密钥；留空则保留" else "旧版电脑显示的 64 位密钥"; inputType=InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD }
        val host=EditText(this).apply { hint="电脑局域网 IPv4 地址（可选）"; setText(store.peer()); isSingleLine=true }; content.addView(key); content.addView(host)
        AlertDialog.Builder(this).setTitle("兼容旧版 / 手动连接").setView(content).setNegativeButton("取消",null).setNeutralButton("撤销旧版配对") { _,_ -> stopConnection(); store.clear(); message("旧版共享配对已撤销；新配对设备记录保留"); signature="" }.setPositiveButton("保存并连接") { _,_ -> safe {
            val value=key.text.toString().trim().ifEmpty { store.load()?.let(BridgeProtocol::hex) ?: "" }
            store.save(value,host.text.toString()); if(host.text.isNotBlank()) { selected="manual:"+host.text.toString().trim(); prefs.edit().putString("selected",selected).apply() }
            stopConnection(); handler.postDelayed({ safe { startConnection(false) } },600)
        } }.show()
    }
    private fun readClipboard() {
        selectedHost(); check(hasWindowFocus()) { "请在互传界面主动操作剪贴板" }
        val clip=getSystemService(ClipboardManager::class.java).primaryClip
        val text=clip?.takeIf { it.itemCount>0 }?.getItemAt(0)?.text?.toString(); require(!text.isNullOrEmpty()) { "剪贴板没有纯文本" }; previewText(text)
    }
    private fun previewText(text: String) {
        val host=selectedHost(); require(text.toByteArray().size<=BridgeProtocol.MAX_TEXT) { "文字超过 256 KiB" }
        val view=ui.text(text,15f).apply { setPadding(ui.dp(18),ui.dp(12),ui.dp(18),ui.dp(12)); setTextIsSelectable(true) }
        AlertDialog.Builder(this).setTitle("发送到 ${target()?.name ?: host}").setView(ScrollView(this).apply { addView(view) }).setNegativeButton("取消",null).setPositiveButton("确认发送") { _,_ -> safe { requireService().sendText(host,text) } }.show()
    }
    private fun pickFiles() { selectedHost(); @Suppress("DEPRECATION") startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply { type="*/*"; addCategory(Intent.CATEGORY_OPENABLE); putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true); addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION) },42) }
    @Deprecated("Platform document picker")
    override fun onActivityResult(requestCode: Int,resultCode: Int,data: Intent?) {
        super.onActivityResult(requestCode,resultCode,data)
        if(requestCode==42 && resultCode==RESULT_OK && data!=null) safe { val uris=mutableListOf<Uri>(); data.clipData?.let { clip -> repeat(clip.itemCount) { uris.add(clip.getItemAt(it).uri) } }; data.data?.let(uris::add); confirmFiles(uris.distinct()) }
    }
    private fun confirmFiles(uris: List<Uri>) {
        val host=selectedHost(); require(uris.size in 1..256) { "请选择 1 至 256 个文件" }
        AlertDialog.Builder(this).setTitle("发送 ${uris.size} 个文件？").setMessage("目标：${target()?.name ?: host}\n接收方需要主动接受，可以在传输页取消。").setNegativeButton("取消",null).setPositiveButton("发送") { _,_ -> safe { requireService().sendFiles(host,uris); page=1; signature=""; render() } }.show()
    }
    @Suppress("DEPRECATION") private fun handleShare(value: Intent?) {
        if(value?.action !in listOf(Intent.ACTION_SEND,Intent.ACTION_SEND_MULTIPLE)) return
        sharedUris=if(value?.action==Intent.ACTION_SEND_MULTIPLE) value.getParcelableArrayListExtra<Uri>(Intent.EXTRA_STREAM) ?: arrayListOf() else listOfNotNull(value?.getParcelableExtra<Uri>(Intent.EXTRA_STREAM))
        sharedText=value?.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString(); page=0; value?.action=null; message("已收到分享内容，请选择目标后主动发送")
    }
    private fun installUpdate() {
        if(!packageManager.canRequestPackageInstalls()) { startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:$packageName"))); message("允许此来源后，返回互传再次点击安装更新"); return }
        message("正在核验安装包…")
        updates.prepareInstall { file,error -> runOnUiThread {
            if(file==null) message(error ?: "安装包不可用")
            else if(resumed && hasWindowFocus()) safe { startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(Uri.parse("content://app.clipbridge.updates/package.apk"),"application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)) }
            else message("更新已校验，返回互传后可再次点击安装")
        } }
    }
    override fun onRequestPermissionsResult(requestCode: Int,permissions: Array<out String>,grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode,permissions,grantResults)
        if(requestCode==91 && PlatformCompat.canUseLan(this)) safe { startConnection(false) }
    }
}
