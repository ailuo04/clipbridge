package app.clipbridge

import android.Manifest
import android.app.Activity
import android.content.res.ColorStateList
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
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
import app.clipbridge.protocol.PairingInvitationCodec
import com.google.zxing.client.android.Intents
import com.journeyapps.barcodescanner.CaptureActivity
import app.clipbridge.storage.PairingStore
import app.clipbridge.ui.BridgeUi
import app.clipbridge.ui.UiPresentation
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
    private var seenPairSessions=emptySet<String>()
    private var focusPairing=false
    private var sharedUris=emptyList<Uri>()
    private var sharedText: String?=null
    private var filter=0
    private var advancedExpanded=false
    private var navigationSignature=""
    private var renderGeneration=0
    private var pendingScrollRestore=false
    private val pageOffsets=IntArray(3)
    private data class Target(val id: String,val name: String,val address: String,val online: Boolean)
    private val tick=object : Runnable { override fun run() { refresh(); handler.postDelayed(this,1000) } }
    override fun attachBaseContext(base: Context) {
        val mode=base.getSharedPreferences("interface",Context.MODE_PRIVATE).getInt("theme",0)
        if(mode==1 || mode==2) {
            val config=Configuration(base.resources.configuration).apply {
                uiMode=(uiMode and Configuration.UI_MODE_NIGHT_MASK.inv()) or
                    (if(mode==1) Configuration.UI_MODE_NIGHT_NO else Configuration.UI_MODE_NIGHT_YES)
            }
            super.attachBaseContext(base.createConfigurationContext(config))
        } else super.attachBaseContext(base)
    }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        store=PairingStore(this); ui=BridgeUi(this); updates=UpdateRepository.get(this)
        page=(savedInstanceState?.getInt("page") ?: 0).coerceIn(0,2); selected=prefs.getString("selected","") ?: ""
        filter=(savedInstanceState?.getInt("filter") ?: 0).coerceIn(0,2)
        advancedExpanded=savedInstanceState?.getBoolean("advanced") ?: false
        savedInstanceState?.getIntArray("pageOffsets")?.let { positions -> positions.copyInto(pageOffsets,endIndex=minOf(positions.size,pageOffsets.size)) }
        pendingScrollRestore=savedInstanceState?.containsKey("pageOffsets")==true
        val root=ui.column().apply { background=ui.backdrop() }
        notice=ui.text("主动发送，安静接收。",12f,ui.muted).apply {
            minimumHeight=ui.dp(48); gravity=Gravity.CENTER_VERTICAL
            setPadding(ui.dp(16),ui.dp(9),ui.dp(16),ui.dp(9)); background=ui.glass(ui.tint,22)
            accessibilityLiveRegion=View.ACCESSIBILITY_LIVE_REGION_POLITE
            isFocusable=true; contentDescription="连接状态：主动发送，安静接收。"
        }
        notice.setOnClickListener {
            if(BridgeService.current?.hub?.pending?.isNotEmpty()==true) { pageOffsets[page]=scroll.scrollY; page=0; focusPairing=true; signature=""; render() }
        }
        scroll=ScrollView(this).apply { isFillViewport=true; clipToPadding=false; isVerticalScrollBarEnabled=false }
        body=ui.column(20); scroll.addView(body)
        navigation=ui.row().apply {
            setPadding(ui.dp(6),ui.dp(6),ui.dp(6),ui.dp(6)); background=ui.glass(radius=28)
            elevation=ui.dp(3).toFloat()
        }
        root.addView(notice,LinearLayout.LayoutParams(-1,-2).apply { marginStart=ui.dp(20); marginEnd=ui.dp(20); topMargin=ui.dp(8) })
        root.addView(scroll,LinearLayout.LayoutParams(-1,0,1f))
        root.addView(navigation,LinearLayout.LayoutParams(-1,-2).apply { marginStart=ui.dp(20); marginEnd=ui.dp(20); bottomMargin=ui.dp(10); topMargin=ui.dp(2) })
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
        if(prefs.getBoolean("connection",true) && BridgeService.current==null) {
            if(PlatformCompat.canUseLan(this)) startConnection(false)
            else message("请允许互传访问本地网络，然后开始搜索")
        }
        if(updates.autoCheck()) updates.check()
        signature=""; refresh()
    }
    override fun onPause() { resumed=false; super.onPause() }
    override fun onStop() { handler.removeCallbacks(tick); super.onStop() }
    override fun onSaveInstanceState(outState: Bundle) {
        if(!pendingScrollRestore) pageOffsets[page]=scroll.scrollY
        outState.putInt("page",page); outState.putInt("filter",filter)
        outState.putBoolean("advanced",advancedExpanded); outState.putIntArray("pageOffsets",pageOffsets)
        super.onSaveInstanceState(outState)
    }
    override fun onDestroy() { if(::ui.isInitialized) ui.dismissDialogs(); super.onDestroy() }
    override fun onNewIntent(intent: Intent) { super.onNewIntent(intent); setIntent(intent); handleShare(intent); signature=""; render() }
    private fun message(value: String) { notice.text=value; notice.contentDescription="连接状态：$value" }
    private fun safe(block: ()->Unit) { try { block() } catch(e: Exception) { message(e.message ?: "操作失败，请重试") } }
    private fun action(parent: LinearLayout,label: String,primary: Boolean=false,enabled: Boolean=true,block: ()->Unit): Button = ui.button(label,primary) { safe(block) }.apply {
        isEnabled=enabled
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
        val sessions=hub?.pending?.keys?.toSet() ?: emptySet()
        if(resumed && page==0 && sessions.any { it !in seenPairSessions }) focusPairing=true
        seenPairSessions=sessions
        val next=page.toString()+selected+filter+service?.status+service?.receivedText?.length+targets().joinToString { it.toString() }+hub?.nearby?.values?.sortedBy { it.id }?.joinToString { "${it.id}:${it.address}:${it.authenticated}" }+hub?.pending?.values?.joinToString { "${it.session}:${it.id}:${it.name}:${it.address}:${it.code}:${it.expires}:${it.decision.isDone}" }+service?.pending?.keys?.joinToString()+service?.transfers?.values?.joinToString()+updates.state+updates.progress+updates.ready
        if(next!=signature) { signature=next; render() }
    }
    private fun selectPage(index: Int) {
        if(index==page) return
        if(!pendingScrollRestore) pageOffsets[page]=scroll.scrollY
        page=index; pendingScrollRestore=true; signature=""; render()
    }
    private fun renderNavigation() {
        val pending=BridgeService.current?.pending?.size ?: 0
        val pairs=BridgeService.current?.hub?.pending?.size ?: 0
        val key="$page/$pending/$pairs"
        if(key==navigationSignature) return
        navigationSignature=key; navigation.removeAllViews()
        listOf("设备","传输","设置").forEachIndexed { index,label ->
            val badge=if(index==1) pending else if(index==0) pairs else 0
            navigation.addView(ui.navigationItem(label,listOf(0,3,4)[index],index==page,badge) { selectPage(index) },
                LinearLayout.LayoutParams(0,-2,1f).apply { marginStart=ui.dp(2); marginEnd=ui.dp(2) })
        }
    }
    private fun render() {
        val oldY=UiPresentation.scrollOffset(pageOffsets[page],scroll.scrollY,pendingScrollRestore,focusPairing)
        focusPairing=false; body.removeAllViews(); renderNavigation()
        val generation=++renderGeneration
        ui.pageHeader(body,listOf("让内容，自由流动","每一次传输，都看得见","你的互传，你来设定")[page],
            listOf("连接设备，主动发送，安心接收。","收到的文件，确认后才会保存。","外观、连接与更新，井然有序。")[page])
        when(page) { 0->devicesPage(); 1->transfersPage(); 2->settingsPage() }
        ui.gap(body,12)
        scroll.post { if(generation==renderGeneration) { scroll.scrollTo(0,oldY); pageOffsets[page]=scroll.scrollY; pendingScrollRestore=false } }
    }
    private fun devicesPage() {
        val service=BridgeService.current; val hub=service?.hub
        hub?.pending?.values?.sortedBy { it.expires }?.forEach { request ->
            val card=ui.card(body,ui.mint)
            card.addView(ui.badge("等待双方确认",ui.green)); ui.gap(card,8)
            card.addView(ui.text("与 "+request.name+" 配对",19f,bold=true))
            card.addView(ui.text("请确认两台设备显示相同的六位数字。",13f,ui.muted))
            card.addView(ui.text(request.code,34f,ui.primary,true).apply {
                setTextIsSelectable(true); letterSpacing=.12f
                contentDescription="配对验证码 "+request.code.toCharArray().joinToString(" ")
            })
            card.addView(ui.text(request.address,12f,ui.muted))
            if(!request.decision.isDone) ui.actionPair(card,
                ui.button("拒绝") { safe { hub.decide(request,false); signature="" } },
                ui.button("数字一致，配对",true) { safe { hub.decide(request,true); signature="" } })
            else card.addView(ui.text("已确认，等待另一台设备…",14f,ui.green))
        }
        if(!PlatformCompat.canUseLan(this)) {
            val permission=ui.card(body,ui.mint)
            ui.deviceSummary(permission,"先允许本地网络访问","授权后才能搜索和连接附近设备。",10)
            action(permission,"开启本地网络权限",true) { startConnection() }
            ui.listAction(permission,"打开系统权限设置") { safe { startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,Uri.parse("package:$packageName"))) } }
        }
        val current=target()
        val targetCard=ui.card(body,ui.tint)
        targetCard.addView(ui.text("当前发送目标",11f,ui.primary,true)); ui.gap(targetCard,8)
        ui.deviceSummary(targetCard,current?.name ?: "连接你的第一台设备",
            current?.address ?: "同一 Wi-Fi 或热点下，扫码或发现即可配对。",
            status=if(current?.online==true) "● 已连接，可以发送" else if(current!=null) "● 设备暂时离线" else "无需手动填写密钥",connected=current?.online==true)
        if(current!=null || targets().any { it.online }) {
            ui.gap(targetCard,4)
            ui.listAction(targetCard,"切换发送目标") { safe { chooseTarget() } }
        }
        val send=if(ui.stackActions) ui.column() else ui.row()
        val enabled=current?.online==true
        fun sendTile(title: String, subtitle: String, kind: Int, emphasized: Boolean, block: ()->Unit): View {
            val tile=ui.column(16).apply {
                background=ui.ripple(ui.glass(if(emphasized) ui.tint else ui.surface,24),24)
                minimumHeight=ui.dp(if(ui.stackActions) 100 else 140)
                isEnabled=enabled; isClickable=enabled; isFocusable=true
                contentDescription=title+"，"+(if(enabled) subtitle else "请先连接一台在线设备")
                setOnClickListener { if(enabled) safe(block) }
            }
            tile.addView(ui.icon(kind,if(enabled) ui.primary else ui.muted),LinearLayout.LayoutParams(ui.dp(28),ui.dp(28))); ui.gap(tile,10)
            tile.addView(ui.text(title,16f,if(enabled) ui.ink else ui.muted,true)); tile.addView(ui.text(subtitle,12f,ui.muted))
            return tile
        }
        val files=sendTile("发送文件","照片、视频与文档",1,true,::pickFiles)
        val clipboard=sendTile("发送剪贴板","预览文字，再确认发送",2,false,::readClipboard)
        if(ui.stackActions) {
            send.addView(files,LinearLayout.LayoutParams(-1,-2).apply { bottomMargin=ui.dp(10) }); send.addView(clipboard,LinearLayout.LayoutParams(-1,-2))
        } else {
            send.addView(files,LinearLayout.LayoutParams(0,-2,1f).apply { marginEnd=ui.dp(6) }); send.addView(clipboard,LinearLayout.LayoutParams(0,-2,1f).apply { marginStart=ui.dp(6) })
        }
        body.addView(send,LinearLayout.LayoutParams(-1,-2).apply { bottomMargin=ui.dp(14) })
        if(sharedUris.isNotEmpty() || sharedText!=null) {
            val share=ui.card(body,ui.tint)
            share.addView(ui.text("分享内容已准备好",16f,bold=true))
            share.addView(ui.text("先选择在线设备，确认后再发送。",12f,ui.muted))
            action(share,if(sharedUris.isNotEmpty()) "发送分享的 "+sharedUris.size+" 个文件" else "预览分享的文字",true,enabled) {
                if(sharedUris.isNotEmpty()) confirmFiles(sharedUris) else sharedText?.let(::previewText)
            }
        }
        if(service?.receivedText!=null) {
            val received=ui.card(body)
            ui.listAction(received,"复制最近收到的文字","仅在你点击时写入剪贴板",2) { safe {
                getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("ClipBridge",requireService().receivedText)); message("已复制收到的文字")
            } }
        }
        ui.heading(body,"附近设备",if(service==null) "开启连接后，发现同一网络里的电脑与手机。" else "仅发现附近设备，不会自动建立信任。")
        val discover=ui.card(body)
        ui.actionPair(discover,ui.button(if(service==null) "开启搜索" else "重新搜索") { safe {
            if(service==null) startConnection() else if(hub==null) message("连接服务正在启动，请稍候") else { hub.refreshDiscovery(); message("正在重新搜索附近设备") }
        } },ui.button("扫码配对",true) { safe { scanPairing() } })
        val nearby=hub?.nearby?.values?.filter { peer->store.records().none { it.id==peer.id } }?.sortedBy { it.name } ?: emptyList()
        if(nearby.isEmpty()) {
            ui.gap(discover,14)
            discover.addView(ui.text(if(service==null) "连接服务尚未开启" else "等待发现附近设备",15f,bold=true))
            discover.addView(ui.text("打开电脑互传并连接同一网络，也可以扫描电脑上的配对二维码。",13f,ui.muted))
        }
        nearby.forEach { peer ->
            val card=ui.card(body)
            ui.deviceSummary(card,peer.name,peer.platform+" · "+peer.address,if(peer.platform.contains("Android",true)) 8 else 0)
            action(card,"发起配对",true) { focusPairing=true; hub?.pair(peer); message("正在连接，请在两端核对验证码") }
        }
        ui.heading(body,"我的设备","每台设备分别配对，在线后才可发送。")
        val all=targets()
        if(all.isEmpty()) ui.empty(body,"还没有配对设备","从附近设备发起配对，双方核对数字后即可连接。",0)
        all.forEach { device ->
            val card=ui.card(body)
            ui.deviceSummary(card,device.name,device.address,status=if(device.online) "● 在线" else "● 离线",connected=device.online)
            action(card,if(selected==device.id) "当前发送目标" else "设为发送目标",selected==device.id,device.online && selected!=device.id) {
                selected=device.id; prefs.edit().putString("selected",selected).apply(); signature=""
            }
        }
    }
    @Suppress("DEPRECATION") private fun scanPairing() {
        if(!PlatformCompat.canUseLan(this)) { startConnection(); return }
        if(BridgeService.current?.hub==null) { startConnection(); message("连接服务正在准备，请稍后再次点击扫码配对"); return }
        requireService()
        require(packageManager.hasSystemFeature(PackageManager.FEATURE_CAMERA_ANY)) { "此设备没有可用相机，请使用附近设备或按 IP 配对" }
        val scan=Intent(this,CaptureActivity::class.java).apply {
            action=Intents.Scan.ACTION
            putExtra(Intents.Scan.FORMATS,"QR_CODE")
            putExtra(Intents.Scan.PROMPT_MESSAGE,"扫描电脑互传中的配对二维码")
            putExtra(Intents.Scan.BEEP_ENABLED,false)
            putExtra(Intents.Scan.BARCODE_IMAGE_ENABLED,false)
            putExtra(Intents.Scan.ORIENTATION_LOCKED,false)
        }
        startActivityForResult(scan,43)
    }
    private fun chooseTarget() {
        val items=targets().filter { it.online }; require(items.isNotEmpty()) { "还没有在线设备，请先与附近设备配对" }
        ui.dialog().setTitle("发送到哪台设备").setItems(items.map { "${it.name} · ${it.address}" }.toTypedArray()) { _,which -> selected=items[which].id; prefs.edit().putString("selected",selected).apply(); signature="" }.setNegativeButton("取消",null).show()
    }
    private fun transfersPage() {
        val choices=ui.row().apply { background=ui.glass(radius=20); setPadding(ui.dp(5),ui.dp(5),ui.dp(5),ui.dp(5)) }
        listOf("全部","发送","接收").forEachIndexed { index,label ->
            choices.addView(ui.button(label,filter==index) { filter=index; signature=""; render() }.apply {
                isSelected=filter==index; contentDescription=label+"传输"+(if(filter==index) "，已选择" else "")
            },LinearLayout.LayoutParams(0,-2,1f).apply { marginStart=ui.dp(2); marginEnd=ui.dp(2) })
        }
        body.addView(choices); ui.gap(body,14)
        val service=BridgeService.current
        val requests=service?.pending?.values?.filter { !it.decision.isDone } ?: emptyList()
        val visibleRequests=if(filter==1) emptyList() else requests
        if(visibleRequests.isNotEmpty()) ui.heading(body,"等待你接收 · "+visibleRequests.size,"只有你确认后，文件才会保存。")
        visibleRequests.forEach { request ->
            val card=ui.card(body,ui.tint)
            card.addView(ui.badge("等待接收",ui.primary)); ui.gap(card,8)
            card.addView(ui.text(request.offer.files.first().name+(if(request.offer.files.size>1) " 等 "+request.offer.files.size+" 个文件" else ""),17f,bold=true))
            card.addView(ui.text("来自 "+request.address+" · "+ui.size(request.offer.total),12f,ui.muted)); ui.gap(card,8)
            card.addView(ui.text(request.offer.files.take(6).joinToString("\n") { it.name+" · "+ui.size(it.size) },13f,ui.muted))
            if(request.offer.files.size>6) card.addView(ui.text("另有 "+(request.offer.files.size-6)+" 个文件",12f,ui.muted))
            ui.actionPair(card,ui.button("拒绝") { request.decision.complete(false) },ui.button("接受文件",true) { request.decision.complete(true) })
        }
        val pendingIds=service?.pending?.keys?.toSet() ?: emptySet()
        val rows=service?.transfers?.values?.filter { row -> row.id !in pendingIds && (filter==0 || row.direction==if(filter==1) "发送" else "接收") }?.takeLast(100)?.reversed() ?: emptyList()
        val groups=listOf("active" to "正在传输","finished" to "已完成","unsuccessful" to "未完成","waiting" to "等待处理","other" to "其他记录")
        groups.forEach { (group,title) ->
            val items=rows.filter { UiPresentation.transferGroup(it.state)==group }
            if(items.isNotEmpty()) ui.heading(body,title+" · "+items.size)
            items.forEach { transfer ->
                val card=ui.card(body)
                ui.deviceSummary(card,transfer.name.ifEmpty { transfer.direction },transfer.direction+(if(transfer.address.isNotEmpty()) " · "+transfer.address else ""),1)
                ui.gap(card,8)
                card.addView(ui.text(transfer.state,13f,when(group) { "finished"->ui.green; "unsuccessful"->if(transfer.state.startsWith("失败")) ui.danger else ui.muted; else->ui.primary },true))
                val metric=if(UiPresentation.progressUnit(transfer.state)=="files") "正在准备 "+transfer.done+" / "+transfer.total+" 个文件" else ui.size(transfer.done)+" / "+ui.size(transfer.total)
                card.addView(ui.text(metric,12f,ui.muted))
                if(group=="active") {
                    val percent=UiPresentation.percent(transfer.done,transfer.total)
                    card.addView(ui.progress(percent,transfer.state+"，"+percent+"%"))
                    ui.listAction(card,"取消本次传输",destructive=true) { safe { service?.cancel(transfer.id) } }
                }
                if(transfer.direction=="接收" && transfer.state.startsWith("已保存")) ui.listAction(card,"打开收到的文件",kind=9) { safe { openReceived(transfer.id) } }
            }
        }
        if(rows.isEmpty() && visibleRequests.isEmpty()) ui.empty(body,"这里还没有传输任务","从设备页主动发送文件，或等待另一台设备发来请求。",3)
        val directory=ui.card(body)
        ui.listAction(directory,"打开接收目录","文件保存到 Download/ClipBridge",9) { safe { startActivity(Intent("android.intent.action.VIEW_DOWNLOADS")) } }
        body.addView(ui.text("记录仅保留在本次连接服务中。未处理的文件请求约 100 秒后超时。",12f,ui.muted))
    }
    private fun openReceived(id: String) {
        val uris=requireService().receivedFiles(id); require(uris.isNotEmpty()) { "文件记录已清除，请打开接收目录" }
        fun open(uri: Uri) { startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(uri,contentResolver.getType(uri) ?: "application/octet-stream").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)) }
        if(uris.size==1) open(uris.first()) else ui.dialog().setTitle("打开收到的文件").setItems(uris.indices.map { "文件 ${it+1}" }.toTypedArray()) { _,which -> safe { open(uris[which]) } }.show()
    }
    private fun setting(parent: LinearLayout, title: String, detail: String, checked: Boolean, change: (Boolean)->Unit) {
        val row=if(ui.stackActions) ui.column() else ui.row()
        row.setPadding(0,ui.dp(8),0,ui.dp(8))
        val labels=ui.column(); labels.addView(ui.text(title,14f,bold=true)); labels.addView(ui.text(detail,12f,ui.muted))
        val toggle=Switch(this).apply {
            isChecked=checked; contentDescription=title; minimumWidth=ui.dp(56); minimumHeight=ui.dp(48)
            thumbTintList=ColorStateList(arrayOf(intArrayOf(android.R.attr.state_checked),intArrayOf()),intArrayOf(ui.primaryFill,ui.muted))
            trackTintList=ColorStateList(arrayOf(intArrayOf(android.R.attr.state_checked),intArrayOf()),intArrayOf(ui.tint,ui.line))
            setOnCheckedChangeListener { _,value -> safe { change(value) } }
        }
        if(ui.stackActions) {
            row.addView(labels,LinearLayout.LayoutParams(-1,-2)); row.addView(toggle,LinearLayout.LayoutParams(-2,-2).apply { gravity=Gravity.END })
        } else {
            row.addView(labels,LinearLayout.LayoutParams(0,-2,1f).apply { marginEnd=ui.dp(12) }); row.addView(toggle)
        }
        labels.setOnClickListener { toggle.isChecked=!toggle.isChecked }; labels.minimumHeight=ui.dp(48)
        parent.addView(row,LinearLayout.LayoutParams(-1,-2))
    }
    private fun settingsPage() {
        val profile=ui.card(body)
        ui.deviceSummary(profile,store.name(),"本机名称 · Android "+Build.VERSION.RELEASE,8)
        ui.listAction(profile,"修改设备名称") { safe { editName() } }
        ui.heading(body,"外观与连接")
        val preferences=ui.card(body)
        val theme=prefs.getInt("theme",0).coerceIn(0,2)
        ui.listAction(preferences,"外观 · "+listOf("跟随系统","浅色","深色")[theme],"浅色柔光，深色静谧，随你切换",4) {
            ui.dialog().setTitle("选择外观").setSingleChoiceItems(arrayOf("跟随系统","浅色","深色"),theme) { dialog,which ->
                prefs.edit().putInt("theme",which).apply(); dialog.dismiss(); recreate()
            }.setNegativeButton("取消",null).show()
        }
        ui.divider(preferences)
        setting(preferences,"保持连接服务","后台只接收和保持连接，不读取剪贴板",prefs.getBoolean("connection",true)) { on ->
            prefs.edit().putBoolean("connection",on).apply()
            if(on) startConnection() else { stopConnection(); message("连接已停止") }; signature=""
        }
        updateSettings()
        ui.heading(body,"设备管理")
        val records=store.records()
        if(records.isEmpty()) ui.empty(body,"还没有信任的设备","配对后会在这里显示。你可以分别取消每台设备的配对。",10)
        records.forEach { trusted ->
            val card=ui.card(body)
            ui.deviceSummary(card,trusted.name,trusted.platform+" · "+if(trusted.active) "已配对" else "确认恢复中",10)
            ui.listAction(card,"取消与此设备的配对",destructive=true) {
                ui.dialog().setTitle("取消与 "+trusted.name+" 的配对？").setMessage("该设备将无法继续传输，重新连接需要双方再次确认。")
                    .setNegativeButton("保留",null).setPositiveButton("取消配对") { _,_ -> safe {
                        BridgeService.current?.hub?.revoke(trusted.id) ?: store.remove(trusted.id); signature=""
                    } }.show()
            }
        }
        ui.heading(body,"更多选项")
        val advanced=ui.card(body)
        ui.listAction(advanced,if(advancedExpanded) "收起高级连接" else "高级连接","按 IP 配对、本地网络权限与旧版兼容",7) {
            advancedExpanded=!advancedExpanded; signature=""; render()
        }
        if(advancedExpanded) {
            ui.divider(advanced)
            ui.listAction(advanced,"按 IP 发起配对","仍然需要双方核对验证码") { safe { pairByAddress() } }
            ui.listAction(advanced,"兼容旧版 / 手动连接","仅用于旧版共享密钥连接") { safe { legacyPairing() } }
            ui.listAction(advanced,"本地网络权限") { safe {
                if(!PlatformCompat.canUseLan(this)) PlatformCompat.requestLan(this) else message("当前系统允许本地网络访问")
            } }
        }
        val about=ui.card(body)
        ui.listAction(about,"开源软件许可","查看互传使用的开源组件",1) { safe {
            val text=assets.open("third-party-notices.txt").bufferedReader().use { it.readText() }
            ui.dialog().setTitle("开源软件许可").setMessage(text).setPositiveButton("关闭",null).show()
        } }
        body.addView(ui.text("剪贴板只在你点击发送时读取。后台请求和更新不会打开界面。",12f,ui.muted))
    }
    private fun updateSettings() {
        ui.heading(body,"应用更新","当前版本 "+updates.versionName)
        val card=ui.card(body,ui.tint)
        val kind=UiPresentation.updateKind(updates.state,updates.ready)
        ui.deviceSummary(card,when(kind) { "ready"->"新版本已准备好"; "downloading"->"正在下载更新"; "checking"->"正在检查更新"; "error"->"暂时未能完成更新"; "latest"->"已是最新版本"; else->"让互传保持最新" },updates.state,11)
        updates.available?.let { update ->
            ui.gap(card,8); card.addView(ui.text("v"+update.version+" · "+ui.size(update.size),12f,ui.muted))
            if(update.notes.isNotBlank()) ui.listAction(card,"查看更新内容") {
                ui.dialog().setTitle("v"+update.version+" 更新内容").setMessage(update.notes).setPositiveButton("知道了",null).show()
            }
        }
        val busy=kind in setOf("checking","downloading")
        if(kind=="downloading") {
            card.addView(ui.progress(updates.progress,"更新包下载进度 "+updates.progress+"%"))
            card.addView(ui.text("下载进度 "+updates.progress.coerceIn(0,100)+"%",12f,ui.primary,true))
        }
        if(updates.ready) action(card,"安装更新",true,block=::installUpdate)
        else if(updates.available!=null && !busy) action(card,"下载新版本",true) { updates.check(force=true,manualDownload=true) }
        if(busy) action(card,if(kind=="downloading") "取消下载" else "取消检查") { updates.cancel(); message("已请求取消，正在清理更新任务…") }
        else ui.listAction(card,"检查更新",kind=7) { updates.check(force=true); message("正在检查更新…") }
        card.addView(ui.text("安全校验通过后，由你点击进入系统安装流程。",12f,ui.muted)); ui.divider(card)
        setting(card,"自动检查更新","打开时按间隔检查；后台任务由系统调度",updates.autoCheck()) { updates.settings(it,updates.autoDownload()); signature="" }
        setting(card,"非计费网络自动下载","按流量计费时等待，也可手动下载",updates.autoDownload()) { updates.settings(updates.autoCheck(),it); signature="" }
    }
    private fun pairByAddress() {
        val input=ui.input("例如 192.168.1.8").apply { inputType=InputType.TYPE_CLASS_PHONE }
        ui.dialog().setTitle("按 IP 发起配对").setMessage("仍需在两端核对验证码，无需输入密钥。").setView(input)
            .setNegativeButton("取消",null).setPositiveButton("开始配对") { _,_ -> safe {
                requireService().hub!!.pairAddress(input.text.toString().trim()); page=0; focusPairing=true; signature=""; render()
            } }.show()
    }
    private fun editName() {
        val input=ui.input("设备名称",store.name())
        ui.dialog().setTitle("修改设备名称").setView(input).setNegativeButton("取消",null).setPositiveButton("保存") { _,_ -> safe { store.setName(input.text.toString()); signature="" } }.show()
    }
    private fun legacyPairing() {
        val content=ui.column()
        val key=ui.input(if(store.load()!=null) "已保存旧版密钥；留空则保留" else "旧版电脑显示的 64 位密钥").apply { inputType=InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD }
        val host=ui.input("电脑局域网 IPv4 地址（可选）",store.peer())
        content.addView(key); ui.gap(content,12); content.addView(host); ui.gap(content,8)
        content.addView(ui.text("旧版共享密钥不提供逐设备隔离，新设备建议使用扫码配对。",12f,ui.muted))
        ui.dialog().setTitle("兼容旧版 / 手动连接").setView(content).setNegativeButton("取消",null).setNeutralButton("撤销旧版配对") { _,_ -> stopConnection(); store.clear(); message("旧版共享配对已撤销；新配对设备记录保留"); signature="" }.setPositiveButton("保存并连接") { _,_ -> safe {
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
        ui.dialog().setTitle("发送到 ${target()?.name ?: host}").setView(view).setNegativeButton("取消",null).setPositiveButton("确认发送") { _,_ -> safe { requireService().sendText(host,text) } }.show()
    }
    private fun pickFiles() { selectedHost(); @Suppress("DEPRECATION") startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply { type="*/*"; addCategory(Intent.CATEGORY_OPENABLE); putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true); addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION) },42) }
    @Deprecated("Platform document picker")
    override fun onActivityResult(requestCode: Int,resultCode: Int,data: Intent?) {
        super.onActivityResult(requestCode,resultCode,data)
        if(requestCode==43) {
            if(resultCode!=RESULT_OK) {
                message(if(data?.getBooleanExtra(Intents.Scan.MISSING_CAMERA_PERMISSION,false)==true) "扫码需要相机权限，也可以使用附近设备或按 IP 配对" else "已取消扫码")
                return
            }
            safe {
                val invitation=PairingInvitationCodec.parse(data?.getStringExtra(Intents.Scan.RESULT) ?: error("没有识别到二维码"))
                val hub=requireService().hub ?: error("连接服务正在启动，请稍候")
                page=0; focusPairing=true; signature=""; val needsConfirmation=hub.pairInvitation(invitation); render()
                message(if(needsConfirmation) "正在连接电脑，请在两端核对验证码" else "设备已配对，正在重新连接")
            }
            return
        }
        if(requestCode==42 && resultCode==RESULT_OK && data!=null) safe { val uris=mutableListOf<Uri>(); data.clipData?.let { clip -> repeat(clip.itemCount) { uris.add(clip.getItemAt(it).uri) } }; data.data?.let(uris::add); confirmFiles(uris.distinct()) }
    }
    private fun confirmFiles(uris: List<Uri>) {
        val host=selectedHost(); require(uris.size in 1..256) { "请选择 1 至 256 个文件" }
        ui.dialog().setTitle("发送 ${uris.size} 个文件？").setMessage("目标：${target()?.name ?: host}\n接收方需要主动接受，可以在传输页取消。").setNegativeButton("取消",null).setPositiveButton("发送") { _,_ -> safe { requireService().sendFiles(host,uris); signature=""; selectPage(1) } }.show()
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
        if(requestCode==91) { if(PlatformCompat.canUseLan(this)) safe { startConnection(false) } else { message("未获得本地网络权限，可在系统权限设置中允许"); signature=""; render() } }
    }
}
