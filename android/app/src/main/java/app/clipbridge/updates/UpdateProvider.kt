package app.clipbridge.updates
import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.database.MatrixCursor
import android.net.Uri
import android.os.ParcelFileDescriptor
import android.provider.OpenableColumns
class UpdateProvider : ContentProvider() {
    override fun onCreate()=true
    private fun file(uri: Uri): java.io.File {
        require(uri.authority=="app.clipbridge.updates" && uri.path=="/package.apk")
        return java.io.File(requireNotNull(context).filesDir,"updates/update.apk")
    }
    override fun getType(uri: Uri)="application/vnd.android.package-archive"
    override fun query(uri: Uri,projection: Array<out String>?,selection: String?,args: Array<out String>?,order: String?): Cursor {
        val f=file(uri); val columns=projection ?: arrayOf(OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE)
        return MatrixCursor(columns).apply { addRow(columns.map { when(it) { OpenableColumns.DISPLAY_NAME->"ClipBridge-update.apk"; OpenableColumns.SIZE->f.length(); else->null } }) }
    }
    override fun openFile(uri: Uri,mode: String): ParcelFileDescriptor { require(mode=="r"); return ParcelFileDescriptor.open(file(uri),ParcelFileDescriptor.MODE_READ_ONLY) }
    override fun insert(uri: Uri,values: ContentValues?): Uri?=throw UnsupportedOperationException()
    override fun delete(uri: Uri,selection: String?,args: Array<out String>?): Int=throw UnsupportedOperationException()
    override fun update(uri: Uri,values: ContentValues?,selection: String?,args: Array<out String>?): Int=throw UnsupportedOperationException()
}
