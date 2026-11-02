package app.clipbridge.updates

import android.app.job.JobInfo
import android.app.job.JobScheduler
import android.content.ComponentName
import android.content.Context
import android.content.pm.PackageManager
import android.net.ConnectivityManager
import app.clipbridge.protocol.BridgeProtocol
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.util.Base64
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

class UpdateRepository private constructor(private val context: Context) {
    companion object {
        @field:android.annotation.SuppressLint("StaticFieldLeak") // Only applicationContext can enter the private constructor.
        @Volatile private var instance: UpdateRepository?=null
        fun get(context: Context): UpdateRepository = instance ?: synchronized(this) { instance ?: UpdateRepository(context.applicationContext).also { instance=it } }
        const val CHECK_JOB=2101; const val DOWNLOAD_JOB=2102
    }
    private val worker=Executors.newSingleThreadExecutor()
    private val busy=AtomicBoolean(false)
    private val prefs=context.getSharedPreferences("updates",Context.MODE_PRIVATE)
    private val directory=File(context.filesDir,"updates").apply { mkdirs() }
    private val envelopeFile=File(directory,"manifest.json")
    val apk=File(directory,"update.apk")
    @Volatile var available: AndroidUpdate?=null; private set
    @Volatile var state="尚未检查更新"; private set
    @Volatile var ready=false; private set
    @Volatile var progress=0; private set
    @Volatile private var stopped=false
    val currentCode: Long get()=context.packageManager.getPackageInfo(context.packageName,0).longVersionCode
    val versionName: String get()=context.packageManager.getPackageInfo(context.packageName,0).versionName ?: ""
    fun autoCheck()=prefs.getBoolean("automatic",true)
    fun autoDownload()=prefs.getBoolean("download",true)
    fun settings(check: Boolean,download: Boolean) { prefs.edit().putBoolean("automatic",check).putBoolean("download",download).apply(); schedule() }
    fun schedule() {
        val scheduler=context.getSystemService(JobScheduler::class.java)
        if(!autoCheck()) { scheduler.cancel(CHECK_JOB); scheduler.cancel(DOWNLOAD_JOB); return }
        val component=ComponentName(context,UpdateJobService::class.java)
        scheduler.schedule(JobInfo.Builder(CHECK_JOB,component).setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).setPeriodic(24*60*60*1000L).setPersisted(true).build())
        if(!autoDownload()) scheduler.cancel(DOWNLOAD_JOB)
    }
    private fun scheduleDownload() {
        if(autoCheck() && autoDownload()) context.getSystemService(JobScheduler::class.java).schedule(JobInfo.Builder(DOWNLOAD_JOB,ComponentName(context,UpdateJobService::class.java)).setRequiredNetworkType(JobInfo.NETWORK_TYPE_UNMETERED).setBackoffCriteria(60*60*1000L,JobInfo.BACKOFF_POLICY_EXPONENTIAL).build())
    }
    private fun connection(url: String): HttpURLConnection {
        require(UpdateProtocol.trustedUrl(url)) { "更新下载地址不受信任" }
        return (URL(url).openConnection() as HttpURLConnection).apply { connectTimeout=12000; readTimeout=20000; instanceFollowRedirects=false; setRequestProperty("User-Agent","ClipBridge-Android"); setRequestProperty("Accept","application/json") }
    }
    private fun open(url: String): HttpURLConnection {
        var current=url
        repeat(6) {
            val c=connection(current); val code=c.responseCode
            if(code==200) return c
            if(code in listOf(301,302,303,307,308)) {
                val location=c.getHeaderField("Location"); c.disconnect()
                if(location==null) throw IOException("更新地址重定向无效")
                current=URL(URL(current),location).toString()
                if(!UpdateProtocol.trustedUrl(current)) throw IOException("更新跳转地址不受信任")
            } else { c.disconnect(); throw IOException("更新请求失败：HTTP $code") }
        }
        throw IOException("更新跳转次数过多")
    }
    private fun fetch(url: String,max: Int): ByteArray {
        val c=open(url)
        try {
            require(c.contentLengthLong<=max) { "更新响应过大" }
            return c.inputStream.use { input -> val output=java.io.ByteArrayOutputStream(); val buffer=ByteArray(8192)
                while(true) { check(!stopped); val n=input.read(buffer); if(n<0) break; require(output.size()+n<=max); output.write(buffer,0,n) }; output.toByteArray() }
        } finally { c.disconnect() }
    }
    private fun envelope(): String {
        val raw=try { fetch(UpdateProtocol.RAW,UpdateProtocol.MAX_MANIFEST) } catch(_: IOException) {
            val j=JSONObject(fetch(UpdateProtocol.API,2*UpdateProtocol.MAX_MANIFEST).toString(Charsets.UTF_8)); require(j.getString("encoding")=="base64")
            val bytes=Base64.getMimeDecoder().decode(j.getString("content")); require(bytes.size<=UpdateProtocol.MAX_MANIFEST); bytes
        }
        return raw.toString(Charsets.UTF_8)
    }
    fun check(force: Boolean=false,manualDownload: Boolean=false,completed: (Boolean)->Unit={}) {
        if(!busy.compareAndSet(false,true)) { completed(false); return }
        stopped=false
        worker.execute {
            var retry=false
            try {
                state="正在检查更新…"
                if(!force && System.currentTimeMillis()-prefs.getLong("last",0)<6*60*60*1000L && envelopeFile.exists()) {
                    available=UpdateProtocol.parse(envelopeFile.readText(),currentCode)
                } else {
                    val text=envelope(); val update=UpdateProtocol.parse(text,currentCode)
                    val temp=File(directory,"manifest.part"); temp.writeText(text); check(temp.renameTo(envelopeFile))
                    available=update; prefs.edit().putLong("last",System.currentTimeMillis()).apply()
                }
                val update=available
                if(update==null) { apk.delete(); ready=false; state="当前已是最新版本"; return@execute }
                ready=apk.exists() && runCatching { verify(apk,update); true }.getOrDefault(false)
                if(ready) { state="更新 ${update.version} 已就绪"; return@execute }
                apk.delete()
                if(manualDownload || (autoDownload() && !context.getSystemService(ConnectivityManager::class.java).isActiveNetworkMetered)) {
                    download(update); ready=true; state="更新 ${update.version} 已就绪"
                } else { state="发现 ${update.version}，等待非计费网络下载"; scheduleDownload() }
            } catch(e: Exception) { if(stopped) state="更新任务已取消" else { state="更新未完成："+(e.message ?: "网络或校验失败"); retry=e is IOException }; ready=runCatching { val cached=UpdateProtocol.parse(envelopeFile.readText(),currentCode) ?: return@runCatching false; verify(apk,cached); available=cached; state="更新 ${cached.version} 已就绪；暂未检查到更新信息"; true }.getOrDefault(false) }
            finally { busy.set(false); completed(retry) }
        }
    }
    fun cancel() { stopped=true }
    private fun download(update: AndroidUpdate) {
        state="正在下载 ${update.version}…"; progress=0
        val part=File(directory,"update.part"); val c=open(update.url)
        try {
            require(c.contentLengthLong<0 || c.contentLengthLong==update.size) { "更新包长度不符" }
            var count=0L
            c.inputStream.use { input -> part.outputStream().use { output -> val buffer=ByteArray(65536)
                while(true) { check(!stopped); val n=input.read(buffer); if(n<0) break; count+=n; require(count<=update.size); output.write(buffer,0,n); progress=(count*100/update.size).toInt() } } }
            require(count==update.size); verify(part,update); check(part.renameTo(apk))
        } finally { c.disconnect(); part.delete() }
    }
    fun verify(file: File,update: AndroidUpdate) {
        UpdateProtocol.verifyPayload(file,update)
        val manager=context.packageManager
        val info=manager.getPackageArchiveInfo(file.absolutePath,PackageManager.GET_SIGNING_CERTIFICATES) ?: error("无法验证 APK")

        val archive=info.signingInfo?.apkContentsSigners ?: error("APK 签名缺失")
        val installed=manager.getPackageInfo(context.packageName,PackageManager.GET_SIGNING_CERTIFICATES).signingInfo?.apkContentsSigners ?: error("当前签名缺失")
        fun pins(signers: Array<android.content.pm.Signature>) = signers.map { BridgeProtocol.hex(MessageDigest.getInstance("SHA-256").digest(it.toByteArray())) }.sorted()
        UpdateProtocol.verifyIdentity(info.packageName,info.longVersionCode,currentCode,pins(installed),pins(archive),update)
    }
    fun prepareInstall(done: (File?,String?)->Unit) {
        worker.execute { try { val update=UpdateProtocol.parse(envelopeFile.readText(),currentCode) ?: error("当前没有可安装更新"); verify(apk,update); done(apk,null) } catch(e: Exception) { ready=false; done(null,e.message ?: "更新包不可用") } }
    }
}
