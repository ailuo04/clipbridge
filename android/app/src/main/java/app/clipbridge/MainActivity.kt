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
import app.clipbridge.ui.HistoryTimelineAdapter
import app.clipbridge.ui.LocalFilesLauncher
import app.clipbridge.ui.UiPresentation
import app.clipbridge.updates.UpdateRepository

class MainActivity : Activity() {
    companion object { private val conversations=app.clipbridge.ui.ConversationState<Uri>() }
    private var chatOpen=false
    private var pickerPeer=""
    private data class ChatPosition(val first:Int,val top:Int,val atBottom:Boolean)
    private val chatPositions=mutableMapOf<String,ChatPosition>()
    private lateinit var store: PairingStore
    private lateinit var ui: BridgeUi
    private lateinit var updates: UpdateRepository
    private lateinit var conversationHost: LinearLayout
    private var chatList: ListView? = null
    private var chatPeer = ""
    private var chatEditor: EditText? = null
    private val drafts = conversations.texts
    private val attachmentDrafts=conversations.attachments
    private val chatSending get()=conversations.isSending(selected)
    private val transferTimes = mutableMapOf<String,Long>()
    private lateinit var body: LinearLayout
    private lateinit var scroll: ScrollView
    private lateinit var navigation: LinearLayout
    private lateinit var notice: TextView
    private val handler=Handler(Looper.getMainLooper())
    private val clipboardManager by lazy { getSystemService(ClipboardManager::class.java) }
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
    private val pageOffsets=IntArray(4)
    private data class Target(val id: String,val name: String,val address: String,val online: Boolean,val platform: String = "") { val isComputer get()=UiPresentation.isComputer(platform) }
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
        page=(savedInstanceState?.getInt("page") ?: 0).coerceIn(0,3); selected=prefs.getString("selected","") ?: ""
        chatOpen=savedInstanceState?.getBoolean("chatOpen") ?: false
        pickerPeer=savedInstanceState?.getString("pickerPeer").orEmpty()
        filter=(savedInstanceState?.getInt("filter") ?: 0).coerceIn(0,2)
        advancedExpanded=savedInstanceState?.getBoolean("advanced") ?: false
        savedInstanceState?.getBundle("chatDrafts")?.let { bundle -> bundle.keySet().forEach { key -> if(!drafts.containsKey(key)) drafts[key]=bundle.getString(key).orEmpty() } }
        savedInstanceState?.getBundle("attachmentDrafts")?.let { bundle->bundle.keySet().forEach { id->if(!attachmentDrafts.containsKey(id))attachmentDrafts[id]=bundle.getStringArrayList(id).orEmpty().map(Uri::parse).toMutableList() } }
        savedInstanceState?.getIntArray("pageOffsets")?.let { positions -> positions.copyInto(pageOffsets,endIndex=minOf(positions.size,pageOffsets.size)) }
        pendingScrollRestore=savedInstanceState?.containsKey("pageOffsets")==true
        val root=ui.column().apply { background=ui.backdrop() }
        notice=ui.text("手动操作，安静接收。",12f,ui.muted).apply {
            minimumHeight=ui.dp(48); gravity=Gravity.CENTER_VERTICAL
            setPadding(ui.dp(16),ui.dp(9),ui.dp(16),ui.dp(9)); background=ui.glass(ui.tint,22)
            accessibilityLiveRegion=View.ACCESSIBILITY_LIVE_REGION_POLITE
            isFocusable=true; contentDescription="连接状态：手动操作，安静接收。"
        }
        notice.setOnClickListener {
            if(BridgeService.current?.hub?.pending?.isNotEmpty()==true) { pageOffsets[page]=scroll.scrollY; page=1; focusPairing=true; signature=""; render() }
        }
        scroll=ScrollView(this).apply { isFillViewport=true; clipToPadding=false; isVerticalScrollBarEnabled=false }
        body=ui.column(20); scroll.addView(body)
        navigation=ui.row().apply {
            setPadding(ui.dp(6),ui.dp(6),ui.dp(6),ui.dp(6)); background=ui.glass(radius=28)
            elevation=ui.dp(3).toFloat()
        }
        root.addView(notice,LinearLayout.LayoutParams(-1,-2).apply { marginStart=ui.dp(20); marginEnd=ui.dp(20); topMargin=ui.dp(8) })
        root.addView(scroll,LinearLayout.LayoutParams(-1,0,1f))
        conversationHost=ui.column(14)
        root.addView(conversationHost,LinearLayout.LayoutParams(-1,0,1f))
        root.addView(navigation,LinearLayout.LayoutParams(-1,-2).apply { marginStart=ui.dp(20); marginEnd=ui.dp(20); bottomMargin=ui.dp(10); topMargin=ui.dp(2) })
        setContentView(root)
        root.setOnApplyWindowInsetsListener { view,insets ->
            if(Build.VERSION.SDK_INT>=30) { val bars=insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout()); val keyboard=insets.getInsets(WindowInsets.Type.ime());view.setPadding(bars.left,bars.top,bars.right,maxOf(bars.bottom,keyboard.bottom)) }
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
        saveChatPosition()
        outState.putBoolean("chatOpen",chatOpen);outState.putString("pickerPeer",pickerPeer)
        outState.putBundle("attachmentDrafts",Bundle().apply { attachmentDrafts.forEach { (id,uris)->putStringArrayList(id,ArrayList(uris.map(Uri::toString))) } })
        outState.putBundle("chatDrafts",Bundle().apply { drafts.forEach { (key,value) -> putString(key,value) } })
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
        val current=store.records().map { t -> val p=hub?.nearby?.get(t.id)?.takeIf { it.authenticated && System.currentTimeMillis()-it.seen<=15000 }; Target(t.id,t.name,p?.address ?: t.address,p!=null && t.active,t.platform) }.toMutableList()
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
        if(resumed && page==1 && sessions.any { it !in seenPairSessions }) focusPairing=true
        seenPairSessions=sessions
        val next=page.toString()+selected+filter+service?.status+service?.receivedText?.length+targets().joinToString { it.toString() }+hub?.nearby?.values?.sortedBy { it.id }?.joinToString { "${it.id}:${it.address}:${it.authenticated}" }+hub?.pending?.values?.joinToString { "${it.session}:${it.id}:${it.name}:${it.address}:${it.code}:${it.expires}:${it.decision.isDone}" }+service?.pending?.keys?.joinToString()+service?.transfers?.values?.joinToString()+service?.historyRevision+updates.state+updates.progress+updates.ready
        val composing=chatEditor?.text?.let { android.view.inputmethod.BaseInputConnection.getComposingSpanStart(it)>=0 }==true
        if(next!=signature && !(page==0 && chatOpen && composing)) { signature=next; render() }
    }
    private fun selectPage(index: Int) {
        saveChatPosition()
        if(index==0 && page==0 && chatOpen){chatOpen=false;signature="";render();return}
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
        listOf("会话","添加设备","传输","设置").forEachIndexed { index,label ->
            val badge=if(index==2) pending else if(index==1) pairs else 0
            navigation.addView(ui.navigationItem(label,listOf(2,0,3,4)[index],index==page,badge) { selectPage(index) },
                LinearLayout.LayoutParams(0,-2,1f).apply { marginStart=ui.dp(2); marginEnd=ui.dp(2) })
        }
    }
    private fun render() {
        val oldY=UiPresentation.scrollOffset(pageOffsets[page],scroll.scrollY,pendingScrollRestore,focusPairing)
        focusPairing=false; body.removeAllViews(); renderNavigation()
        val generation=++renderGeneration
        val showingChat=page==0 && chatOpen && target()!=null
        scroll.visibility=if(showingChat) View.GONE else View.VISIBLE
        conversationHost.visibility=if(showingChat) View.VISIBLE else View.GONE
        navigation.visibility=if(showingChat) View.GONE else View.VISIBLE
        if(page!=0) ui.pageHeader(body,listOf("会话","添加设备","传输记录","设置")[page],
            listOf("选择设备，发送文字和文件；历史记录保留 7 天。","添加设备后，直接发送消息和原文件。","已配对设备自动接收，进度实时可见。","外观、连接与更新，井然有序。")[page])
        when(page) { 0->if(showingChat) conversationPage() else conversationListPage(); 1->devicesPage(); 2->transfersPage(); 3->settingsPage() }
        ui.gap(body,12)
        scroll.post { if(generation==renderGeneration) { scroll.scrollTo(0,oldY); pageOffsets[page]=scroll.scrollY; pendingScrollRestore=false } }
    }
    private fun saveChatPosition() {
        if(chatPeer.isBlank())return
        chatList?.let { list->chatPositions[chatPeer]=ChatPosition(list.firstVisiblePosition,list.getChildAt(0)?.top ?: 0,list.lastVisiblePosition>=list.count-2) }
    }
    private fun openConversation(id:String) {
        saveChatPosition();selected=id;prefs.edit().putString("selected",id).apply();chatOpen=true;page=0;signature="";render()
    }
    @Deprecated("Platform back callback") override fun onBackPressed() {
        if(page==0 && chatOpen){saveChatPosition();chatOpen=false;signature="";render();return}
        if(page!=0){selectPage(0);return}
        @Suppress("DEPRECATION") super.onBackPressed()
    }
    private fun conversationListPage() {
        saveChatPosition();chatList=null;chatEditor=null
        ui.pageHeader(body,"会话","与你的电脑、手机和其他设备互传。")
        val actions=ui.row()
        actions.addView(ui.button("添加设备",true) { selectPage(1) },LinearLayout.LayoutParams(0,-2,1f))
        actions.addView(ui.button("接收文件夹") { safe { openLocalFiles() } },LinearLayout.LayoutParams(0,-2,1f).apply { marginStart=ui.dp(8) })
        body.addView(actions);ui.gap(body,18)
        val service=BridgeService.current
        val devices=targets().filter { !it.id.startsWith("legacy:") && !it.id.startsWith("manual:") }
        if(devices.isEmpty()) { ui.empty(body,"添加你的第一台设备","同一局域网内配对后，即可发送消息、图片、视频和文件夹。",0);return }
        devices.forEach { device->
            val history=runCatching { service?.recentHistory(device.id).orEmpty() }.getOrDefault(emptyList())
            val last=history.maxByOrNull { it.event.createdUtc }?.event
            val unread=history.count { it.event.senderDeviceId!=service?.localDeviceId() && it.event.createdUtc>prefs.getLong("read-"+device.id,0) }
            val draft=drafts[device.id].orEmpty();val attachments=attachmentDrafts[device.id].orEmpty()
            val subtitle=when { conversations.isSending(device.id)->"正在发送…";draft.isNotBlank()->"[草稿] "+draft;attachments.isNotEmpty()->"[草稿] ${attachments.size} 个附件";last?.text!=null->last.text;last?.files?.isNotEmpty()==true->(if(last.files.first().isDirectory) "[文件夹] " else "[文件] ")+last.files.first().name;else->"点击开始对话" }
            val row=ui.row().apply { setPadding(ui.dp(14),ui.dp(16),ui.dp(14),ui.dp(16));background=ui.ripple(ui.background(ui.surface,16));isFocusable=true;contentDescription=device.name+"，"+(if(device.online)"在线" else "离线")+"，"+subtitle;setOnClickListener { openConversation(device.id) };setOnLongClickListener { deviceMenu(device);true } }
            row.addView(ui.iconTile(if(device.isComputer) 0 else 8),LinearLayout.LayoutParams(ui.dp(44),ui.dp(44)).apply { marginEnd=ui.dp(12) })
            val labels=ui.column();labels.addView(ui.text(device.name,16f,bold=true));labels.addView(ui.text(subtitle.orEmpty().replace('\n',' '),12f,ui.muted).apply { maxLines=1;ellipsize=android.text.TextUtils.TruncateAt.END });row.addView(labels,LinearLayout.LayoutParams(0,-2,1f))
            val status=ui.column();status.addView(ui.text(if(device.online)"在线" else "离线",11f,if(device.online)ui.green else ui.muted));if(unread>0)status.addView(ui.badge(unread.toString(),ui.primary));row.addView(status)
            body.addView(row,LinearLayout.LayoutParams(-1,-2).apply { bottomMargin=ui.dp(8) })
        }
    }
    private fun deviceMenu(device:Target) {
        ui.dialog().setTitle(device.name).setItems(arrayOf("打开对话","清空聊天记录","删除设备")) { _,which->when(which){0->openConversation(device.id);1->clearHistory(device);else->deleteDevice(device)} }.setNegativeButton("关闭",null).show()
    }
    /** Files ClipBridge saved for an entry: received copies only, never an original the user picked to send. */
    private fun ownedFiles(entry: app.clipbridge.history.HistoryLocalRecord): List<Uri> {
        val outgoing=entry.event.senderDeviceId==BridgeService.current?.localDeviceId()
        return entry.localFileReferences.values.map(Uri::parse).filter { UiPresentation.ownsLocalFile(outgoing,it.authority) }
    }
    /** Removes the entry and the received files behind it. Returns how many files could not be removed. */
    private fun deleteHistoryEntry(entry: app.clipbridge.history.HistoryLocalRecord): Int {
        val owned=ownedFiles(entry)
        requireService().deleteHistory(entry.event.peerId,entry.event.eventId)
        // A folder transfer lists the folder and its children; removing the folder covers them.
        return owned.count { uri -> runCatching {
            if(uri.authority==app.clipbridge.storage.ReceivedFileProvider.AUTHORITY) { val file=app.clipbridge.storage.ReceivedFileProvider.resolve(this,uri); !file.exists() || file.deleteRecursively() }
            else { contentResolver.delete(uri,null,null); true }
        }.getOrDefault(false).not() }
    }
    private fun confirmDeleteHistory(entry: app.clipbridge.history.HistoryLocalRecord) {
        val owned=ownedFiles(entry)
        val keepsOriginal=entry.localFileReferences.isNotEmpty() && owned.isEmpty()
        val detail=(if(owned.isEmpty()) "删除这条记录。" else "删除这条记录，并删除本机保存的文件。")+(if(keepsOriginal) "你发送的原文件不会被删除。" else "")+"对方设备上的这条记录会同步移除。"
        ui.dialog().setTitle("删除？").setMessage(detail).setNegativeButton("取消",null).setPositiveButton("删除") { _,_->safe {
            val failed=deleteHistoryEntry(entry); message(if(failed>0) "记录已删除，$failed 个文件未能删除" else "已删除"); signature=""; render()
        } }.show()
    }
    private fun clearHistory(device:Target) {
        val entries=runCatching { requireService().recentHistory(device.id) }.getOrDefault(emptyList())
        if(entries.isEmpty()) { message("没有可清空的记录"); return }
        ui.dialog().setTitle("清空与“${device.name}”的聊天记录？").setMessage("本机已接收的文件会一并删除，你发送的原文件不会被删除。对方设备上的记录会同步移除。")
            .setNegativeButton("取消",null).setPositiveButton("清空") { _,_->safe {
                val failed=entries.sumOf { deleteHistoryEntry(it) }; message(if(failed>0) "聊天记录已清空，$failed 个文件未能删除" else "聊天记录已清空"); signature=""; render()
            } }.show()
    }
    private fun deleteDevice(device:Target) {
        ui.dialog().setTitle("删除“${device.name}”？").setMessage("从设备列表移除并取消配对。已接收的本机文件会保留。")
            .setNegativeButton("取消",null).setPositiveButton("删除设备") { _,_->safe {
                BridgeService.current?.hub?.revoke(device.id) ?: store.remove(device.id)
                conversations.forget(device.id);chatPositions.remove(device.id);prefs.edit().remove("read-"+device.id).apply()
                if(selected==device.id){selected="";chatOpen=false;prefs.edit().remove("selected").apply()}
                signature="";render()
            } }.show()
    }

    private fun conversationPage() {
        val service=BridgeService.current
        val current=target()
        val samePeer=chatPeer==current?.id
        val oldList=chatList
        val remembered=chatPositions[current?.id]
        val atBottom=if(samePeer && oldList!=null) oldList.lastVisiblePosition>=oldList.count-2 else remembered?.atBottom ?: true
        val first=if(samePeer) oldList?.firstVisiblePosition ?: 0 else remembered?.first ?: 0
        val top=if(samePeer) oldList?.getChildAt(0)?.top ?: 0 else remembered?.top ?: 0
        val editing=chatEditor?.hasFocus()==true
        val cursor=chatEditor?.selectionStart ?: 0
        conversationHost.removeAllViews()
        chatPeer=current?.id.orEmpty()
        if(current==null) {
            ui.empty(conversationHost,"连接你的设备","配对一次，文字和文件即可直接互传",0)
            conversationHost.addView(ui.button("连接设备",true) { selectPage(1) })
            chatList=null; chatEditor=null; return
        }
        val header=ui.row()
        header.addView(ui.button("‹") { saveChatPosition();chatOpen=false;signature="";render() }.apply { contentDescription="返回会话列表" })
        val names=ui.column()
        names.addView(ui.text(current.name,20f,bold=true))
        names.addView(ui.text(if(current.online) "● 在线 · 已配对设备直接接收" else "● 离线 · 连接后继续互传",11f,ui.muted))
        header.addView(names,LinearLayout.LayoutParams(0,-2,1f))
        header.addView(ui.button("⋯") { safe { deviceMenu(current) } }.apply { contentDescription="设备选项" })
        conversationHost.addView(header)
        val history=if(service!=null && !current.id.startsWith("legacy:") && !current.id.startsWith("manual:")) runCatching { service.recentHistory(current.id) }.getOrDefault(emptyList()) else emptyList()
        val active=service?.transfers?.values?.filter { it.peerId==current.id && it.state in listOf("排队中","传输中","准备文件","连接接收设备") && history.none { h -> h.event.eventId==it.id } } ?: emptyList()
        if(atBottom && resumed && hasWindowFocus()) { val last=history.filter { it.event.senderDeviceId!=service?.localDeviceId() }.maxOfOrNull { it.event.createdUtc } ?: 0; if(last>prefs.getLong("read-"+current.id,0))prefs.edit().putLong("read-"+current.id,last).apply() }
        val rows=history+active.map { t ->
            app.clipbridge.history.HistoryLocalRecord(app.clipbridge.history.HistorySyncEvent(1,t.id,current.id,if(t.direction=="发送") service?.localDeviceId().orEmpty() else current.id,transferTimes.getOrPut(t.id) { System.currentTimeMillis() },"file",null,
                listOf(app.clipbridge.history.HistoryFileMetadata(t.name.ifBlank { "文件" },t.total,"0".repeat(64))),t.state+" · "+UiPresentation.percent(t.done,t.total)+"%"))
        }
        val timeline=ListView(this).apply { divider=null; clipToPadding=false; setPadding(0,ui.dp(12),0,ui.dp(8)); transcriptMode=ListView.TRANSCRIPT_MODE_NORMAL }
        val adapter=HistoryTimelineAdapter(timeline,ui,{ service?.localDeviceId().orEmpty() },
            { value -> safe { clipboardManager.setPrimaryClip(ClipData.newPlainText("ClipBridge",value)); message("已复制") } },
            { uri -> safe { openReceived(uri) } },
            { entry -> safe { shareHistory(entry) } },
            { entry -> safe { confirmDeleteHistory(entry) } },{ current.isComputer })
        timeline.adapter=adapter; adapter.submit(rows); chatList=timeline
        conversationHost.addView(timeline,LinearLayout.LayoutParams(-1,0,1f))
        timeline.setOnScrollListener(object:AbsListView.OnScrollListener { override fun onScrollStateChanged(view:AbsListView?,state:Int) { saveChatPosition() };override fun onScroll(view:AbsListView?,firstVisibleItem:Int,visibleItemCount:Int,totalItemCount:Int) {} })
        if(atBottom) timeline.setSelection(adapter.count-1) else timeline.setSelectionFromTop(first,top)
        val composer=ui.column(10).apply { background=ui.background(ui.surface,22) }
        val toolbar=ui.row()
        toolbar.addView(ui.button("文件") { safe { pickFiles() } },LinearLayout.LayoutParams(0,-2,1f))

        toolbar.addView(ui.button("接收目录") { safe { openLocalFiles() } },LinearLayout.LayoutParams(0,-2,1.2f))
        composer.addView(toolbar)
        val attachments=attachmentDrafts[current.id].orEmpty()
        if(attachments.isNotEmpty()) {
            val strip=ui.row()
            attachments.forEach { uri ->
                val tile=ui.column(4)
                val preview=ImageView(this).apply { scaleType=ImageView.ScaleType.FIT_CENTER }
                tile.addView(preview,LinearLayout.LayoutParams(ui.dp(76),ui.dp(54)))
                app.clipbridge.ui.MediaThumbnail.load(preview,uri)
                tile.addView(ui.button("移除") { attachmentDrafts[current.id]?.remove(uri); signature=""; render() }.apply { isEnabled=!chatSending && current.online })
                strip.addView(tile)
            }
            composer.addView(HorizontalScrollView(this).apply { addView(strip) },LinearLayout.LayoutParams(-1,ui.dp(110)))
        }
        if(sharedUris.isNotEmpty() || sharedText!=null) composer.addView(ui.button("发送分享内容") { safe { if(sharedUris.isNotEmpty()) confirmFiles(sharedUris) else sharedText?.let { previewText(it); sharedText=null }; signature="" } })
        val inputRow=ui.row()
        val editor=object: EditText(this) {
            override fun onTextContextMenuItem(id:Int):Boolean {
                if(id==android.R.id.paste || id==android.R.id.pasteAsPlainText) {
                    val clip=clipboardManager.primaryClip
                    val uris=if(clip!=null) (0 until clip.itemCount).mapNotNull { clip.getItemAt(it).uri } else emptyList()
                    if(uris.isNotEmpty()) { safe { confirmFiles(uris) }; return true }
                }; return super.onTextContextMenuItem(id)
            }
        }.apply { hint="输入消息…"; setText(drafts[current.id].orEmpty()); setTextColor(ui.ink); setHintTextColor(ui.muted); background=ui.background(ui.field,14); minLines=1; maxLines=4; isEnabled=!chatSending; setPadding(ui.dp(12),ui.dp(10),ui.dp(12),ui.dp(10)) }
        editor.addTextChangedListener(object:android.text.TextWatcher {
            override fun beforeTextChanged(s:CharSequence?,start:Int,count:Int,after:Int) {}
            override fun onTextChanged(s:CharSequence?,start:Int,before:Int,count:Int) { drafts[current.id]=s?.toString().orEmpty() }
            override fun afterTextChanged(value:android.text.Editable?) {}
        })
        inputRow.addView(editor,LinearLayout.LayoutParams(0,-2,1f))
        inputRow.addView(ui.button(if(chatSending) "发送中" else "发送",true) { safe { sendDraft() } }.apply { isEnabled=!chatSending })
        composer.addView(inputRow); conversationHost.addView(composer)
        chatEditor=editor
        if(editing && samePeer) { editor.requestFocus(); editor.setSelection(cursor.coerceIn(0,editor.length())) }
    }

    private fun shareHistory(entry: app.clipbridge.history.HistoryLocalRecord) {
        val intent = if (entry.event.contentType == "text") {
            Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, entry.event.text ?: "")
        } else {
            val uris = entry.localFileReferences.values.map(Uri::parse)
            if (uris.isEmpty()) error("本地文件记录已不存在")
            if (uris.size == 1) Intent(Intent.ACTION_SEND).setType("application/octet-stream").putExtra(Intent.EXTRA_STREAM, uris.first())
            else Intent(Intent.ACTION_SEND_MULTIPLE).setType("application/octet-stream").putParcelableArrayListExtra(Intent.EXTRA_STREAM, ArrayList(uris))
        }
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        startActivity(Intent.createChooser(intent, "分享互传记录"))
    }

    private fun openLocalFiles() {
        val fallback = LocalFilesLauncher.openDownloadsOrFallback(this) ?: return
        if (fallback.isEmpty()) { message("Download/ClipBridge 里还没有本地接收文件"); return }
        ui.dialog().setTitle("ClipBridge 本地接收文件")
            .setItems(fallback.map { it.name }.toTypedArray()) { _, index ->
                val file = fallback[index]
                ui.dialog().setTitle(file.name).setItems(arrayOf("打开文件", "删除本地文件")) { _, action ->
                    if (action == 0) safe { startActivity(Intent(Intent.ACTION_VIEW).setData(file.uri).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)) }
                    else ui.dialog().setTitle("删除本地文件？").setMessage("这会删除设备上的“${file.name}”，不只是历史记录。此操作与删除历史记录分开。")
                        .setNegativeButton("保留文件", null)
                        .setPositiveButton("删除本地文件") { _, _ -> safe {
                            val deleted = contentResolver.delete(file.uri, null, null)
                            require(deleted > 0) { "本地文件没有删除，请在系统文件管理器中操作" }
                            message("已删除本地文件；历史记录仍可单独管理")
                        } }.show()
                }.setNegativeButton("关闭", null).show()
            }.setNegativeButton("关闭", null).show()
    }
    private fun devicesPage() {
        val service=BridgeService.current; val hub=service?.hub
        hub?.pending?.values?.sortedBy { it.expires }?.forEach { request ->
            val card=ui.accentCard(body, ui.mint)
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
            val permission=ui.accentCard(body, ui.mint)
            ui.deviceSummary(permission,"先允许本地网络访问","授权后才能搜索和连接附近设备。",10)
            action(permission,"开启本地网络权限",true) { startConnection() }
            ui.listAction(permission,"打开系统权限设置") { safe { startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,Uri.parse("package:$packageName"))) } }
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
            action(card,"打开对话",true) { openConversation(device.id) }
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
        val items=targets(); require(items.isNotEmpty()) { "还没有设备，请先添加设备" }
        ui.dialog().setTitle("发送到哪台设备").setItems(items.map { "${it.name} · ${it.address}" }.toTypedArray()) { _,which -> openConversation(items[which].id) }.setNegativeButton("取消",null).show()
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
            val card=ui.accentCard(body)
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
            ui.listAction(card,"删除设备",destructive=true) { deleteDevice(Target(trusted.id,trusted.name,trusted.address,false)) }
        }
        ui.heading(body,"更多选项")
        val advanced=ui.card(body)
        ui.listAction(advanced,if(advancedExpanded) "收起高级连接" else "高级连接","按 IP 配对、本地网络权限",7) {
            advancedExpanded=!advancedExpanded; signature=""; render()
        }
        if(advancedExpanded) {
            ui.divider(advanced)
            ui.listAction(advanced,"按 IP 发起配对","仍然需要双方核对验证码") { safe { pairByAddress() } }

            ui.listAction(advanced,"本地网络权限") { safe {
                if(!PlatformCompat.canUseLan(this)) PlatformCompat.requestLan(this) else message("当前系统允许本地网络访问")
            } }
        }
        val about=ui.card(body)
        ui.listAction(about,"开源软件许可","查看互传使用的开源组件",1) { safe {
            val text=assets.open("third-party-notices.txt").bufferedReader().use { it.readText() }
            ui.dialog().setTitle("开源软件许可").setMessage(text).setPositiveButton("关闭",null).show()
        } }
        body.addView(ui.text("手动粘贴后发送；图片、视频和文件以原文件传输。",12f,ui.muted))
    }
    private fun updateSettings() {
        ui.heading(body,"应用更新","当前版本 "+updates.versionName)
        val card=ui.accentCard(body)
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
                requireService().hub!!.pairAddress(input.text.toString().trim()); page=1; focusPairing=true; signature=""; render()
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
    private fun previewText(text: String) {
        val host=selectedHost(); require(text.toByteArray().size<=BridgeProtocol.MAX_TEXT) { "文字超过 256 KiB" }
        target()?.let { drafts[it.id]=text;openConversation(it.id) }; sharedText=null; signature=""; message("文字已填入，请点击发送")
    }
    private fun openReceived(uri:Uri) {
        if(android.provider.DocumentsContract.isTreeUri(uri) && contentResolver.getType(uri)==android.provider.DocumentsContract.Document.MIME_TYPE_DIR) {
            val docs=android.provider.DocumentsContract.buildChildDocumentsUriUsingTree(uri,android.provider.DocumentsContract.getDocumentId(uri))
            val entries=mutableListOf<Pair<String,Uri>>()
            contentResolver.query(docs,arrayOf(android.provider.DocumentsContract.Document.COLUMN_DOCUMENT_ID,android.provider.DocumentsContract.Document.COLUMN_DISPLAY_NAME),null,null,null)?.use { c->while(c.moveToNext())entries.add(c.getString(1) to android.provider.DocumentsContract.buildDocumentUriUsingTree(uri,c.getString(0))) }
            android.app.AlertDialog.Builder(this).setTitle("文件夹").setItems(entries.map { it.first }.toTypedArray()) { _,index->safe { openReceived(entries[index].second) } }.setNegativeButton("关闭",null).show();return
        }
        if(uri.authority==app.clipbridge.storage.ReceivedFileProvider.AUTHORITY) {
            val file=app.clipbridge.storage.ReceivedFileProvider.resolve(this,uri)
            if(file.isDirectory) {
                val children=file.listFiles()?.sortedWith(compareBy<java.io.File> { !it.isDirectory }.thenBy { it.name }) ?: emptyList()
                android.app.AlertDialog.Builder(this).setTitle(file.name).setItems(children.map { (if(it.isDirectory) "▣ " else "")+it.name }.toTypedArray()) { _,index->safe { openReceived(app.clipbridge.storage.ReceivedFileProvider.uri(this,children[index])) } }.setNegativeButton("关闭",null).show();return
            }
        }
        startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(uri,contentResolver.getType(uri) ?: "application/octet-stream").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
    }
    private fun pickFiles() { requireService();pickerPeer=target()?.id ?: error("请选择设备"); android.app.AlertDialog.Builder(this).setTitle("添加附件").setItems(arrayOf("选择文件（图片、视频及其他文件）","选择文件夹")) { _,index->safe { if(index==0) pickDocuments() else { @Suppress("DEPRECATION") startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION),44) } } }.show() }
    private fun pickDocuments() { requireService(); @Suppress("DEPRECATION") startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply { type="*/*"; addCategory(Intent.CATEGORY_OPENABLE); putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true); addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION) },42) }
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
                page=1; focusPairing=true; signature=""; val needsConfirmation=hub.pairInvitation(invitation); render()
                message(if(needsConfirmation) "正在连接电脑，请在两端核对验证码" else "设备已配对，正在重新连接")
            }
            return
        }
        if(requestCode in listOf(42,44) && resultCode==RESULT_OK && data!=null) safe { val uris=mutableListOf<Uri>(); data.clipData?.let { clip -> repeat(clip.itemCount) { uris.add(clip.getItemAt(it).uri) } }; data.data?.let(uris::add); confirmFiles(uris.distinct(),pickerPeer.ifBlank { selected }) }
    }
    private fun confirmFiles(uris: List<Uri>,peerId:String=selected) {
        val peer=targets().firstOrNull { it.id==peerId } ?: error("设备已移除，请重新选择")
        val draft=attachmentDrafts.getOrPut(peer.id) { mutableListOf() }
        val added=uris.distinct().filter { it !in draft }
        require(draft.size+added.size<=256) { "最多添加 256 个附件" }
        added.forEach { runCatching { contentResolver.takePersistableUriPermission(it,Intent.FLAG_GRANT_READ_URI_PERMISSION) } }
        draft.addAll(added); sharedUris=emptyList(); signature=""; openConversation(peer.id); message("附件已添加，点击发送 · 原画质")
    }
    private fun sendDraft() {
        if(chatSending) return
        val peer=target()?.takeIf { it.online } ?: error("请先连接目标设备")
        val text=drafts[peer.id].orEmpty(); val attachments=attachmentDrafts[peer.id]?.toList().orEmpty()
        require(text.isNotBlank() || attachments.isNotEmpty()) { "请输入消息或添加附件" }
        val service=requireService()
        check(conversations.begin(peer.id)) { "此设备正在发送" }
        fun finish(success:Boolean) {
            conversations.complete(peer.id,attachments,success)
            if(isFinishing || isDestroyed) return
            message(if(success) "已发送 · 原文件完整保留" else "发送失败，未发送内容已保留")
            signature=""; if(resumed) render()
        }
        fun files() {
            if(attachments.isEmpty()) finish(true)
            else try { service.sendFiles(peer.address,attachments,expectedPeerId=peer.id,complete=::finish) } catch(_:Exception) { finish(false) }
        }
        try {
            if(text.isBlank()) files()
            else service.sendText(peer.address,text,expectedPeerId=peer.id) { success ->
                if(success) { conversations.textSent(peer.id,text); files() } else finish(false)
            }
        } catch(e:Exception) { conversations.complete(peer.id,attachments,false); throw e }
        signature=""
    }
    @Suppress("DEPRECATION") private fun handleShare(value: Intent?) {
        if(value?.action !in listOf(Intent.ACTION_SEND,Intent.ACTION_SEND_MULTIPLE)) return
        sharedUris=if(value?.action==Intent.ACTION_SEND_MULTIPLE) value.getParcelableArrayListExtra<Uri>(Intent.EXTRA_STREAM) ?: arrayListOf() else listOfNotNull(value?.getParcelableExtra<Uri>(Intent.EXTRA_STREAM))
        sharedText=value?.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString(); page=0; value?.action=null; message("已收到分享内容，请选择目标后主动发送")
    }
    private fun installUpdate() {
        if(!packageManager.canRequestPackageInstalls()) { startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:$packageName"))); message("允许此来源后，返回互传再次点击安装更新"); return }
        require(BridgeService.current?.hasActiveTransfers()!=true) { "请等待传输完成后再安装更新" }
        message("正在核验安装包…")
        updates.prepareInstall { file,error -> runOnUiThread {
            if(file==null) message(error ?: "安装包不可用")
            else if(resumed && hasWindowFocus()) safe { require(BridgeService.current?.hasActiveTransfers()!=true) { "请等待传输完成后再安装更新" }; startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(Uri.parse("content://app.clipbridge.updates/package.apk"),"application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)) }
            else message("更新已校验，返回互传后可再次点击安装")
        } }
    }
    override fun onRequestPermissionsResult(requestCode: Int,permissions: Array<out String>,grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode,permissions,grantResults)
        if(requestCode==91) { if(PlatformCompat.canUseLan(this)) safe { startConnection(false) } else { message("未获得本地网络权限，可在系统权限设置中允许"); signature=""; render() } }
    }
}
