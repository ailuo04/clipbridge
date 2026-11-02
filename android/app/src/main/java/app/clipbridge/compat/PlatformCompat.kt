package app.clipbridge.compat
import android.app.Activity
import android.content.pm.PackageManager
import android.os.Build
object PlatformCompat {
    const val LAN_PERMISSION="android.permission.ACCESS_LOCAL_NETWORK"
    fun needsLanPermission(sdk: Int,target: Int): Boolean = sdk>=37 && target>=37
    fun canUseLan(activity: Activity): Boolean = !needsLanPermission(Build.VERSION.SDK_INT,activity.applicationInfo.targetSdkVersion) || activity.checkSelfPermission(LAN_PERMISSION)==PackageManager.PERMISSION_GRANTED
    fun requestLan(activity: Activity) { activity.requestPermissions(arrayOf(LAN_PERMISSION),91) }
}
