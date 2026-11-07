package app.clipbridge.ui

import android.app.Activity
import android.app.DownloadManager
import android.content.Intent
import android.content.ActivityNotFoundException
import android.net.Uri
import android.provider.MediaStore

data class LocalReceivedFile(val uri: Uri, val name: String)

object LocalFilesLauncher {
    fun openDownloadsOrFallback(activity: Activity): List<LocalReceivedFile>? {
        val intent = Intent(DownloadManager.ACTION_VIEW_DOWNLOADS)
        try {
            activity.startActivity(intent)
            return null
        } catch (_: ActivityNotFoundException) {
            // Fall back to this app's MediaStore rows without broad storage access.
        }
        val projection = arrayOf(MediaStore.Downloads._ID, MediaStore.Downloads.DISPLAY_NAME)
        val result = mutableListOf<LocalReceivedFile>()
        activity.contentResolver.query(
            MediaStore.Downloads.EXTERNAL_CONTENT_URI,
            projection,
            "${MediaStore.Downloads.RELATIVE_PATH} = ?",
            arrayOf("Download/ClipBridge/"),
            "${MediaStore.Downloads.DATE_ADDED} DESC"
        )?.use { cursor ->
            val id = cursor.getColumnIndexOrThrow(MediaStore.Downloads._ID)
            val name = cursor.getColumnIndexOrThrow(MediaStore.Downloads.DISPLAY_NAME)
            while (cursor.moveToNext()) {
                val uri = Uri.withAppendedPath(MediaStore.Downloads.EXTERNAL_CONTENT_URI, cursor.getLong(id).toString())
                result += LocalReceivedFile(uri, cursor.getString(name) ?: "收到的文件")
            }
        }
        return result
    }
}

