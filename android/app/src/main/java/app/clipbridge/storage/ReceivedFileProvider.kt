package app.clipbridge.storage

import android.content.ContentProvider
import android.content.ContentValues
import android.content.Context
import android.database.Cursor
import android.database.MatrixCursor
import android.net.Uri
import android.os.ParcelFileDescriptor
import android.provider.OpenableColumns
import java.io.File

/** Read-only grants are limited to received files, never the app's keys or settings. */
class ReceivedFileProvider : ContentProvider() {
    companion object {
        const val AUTHORITY="app.clipbridge.received"
        fun root(context:Context)=File(context.getExternalFilesDir(null) ?: context.filesDir,"Received").apply { mkdirs() }.canonicalFile
        fun uri(context:Context,file:File):Uri { val base=root(context); val target=file.canonicalFile;require(target.path.startsWith(base.path+File.separator));return Uri.Builder().scheme("content").authority(AUTHORITY).apply { target.relativeTo(base).invariantSeparatorsPath.split('/').forEach(::appendPath) }.build() }
        fun resolve(context:Context,uri:Uri):File { require(uri.authority==AUTHORITY);val base=root(context);val target=File(base,uri.pathSegments.joinToString(File.separator)).canonicalFile;require(target.path.startsWith(base.path+File.separator));return target }
    }
    override fun onCreate()=true
    override fun getType(uri:Uri):String { val f=resolve(requireNotNull(context),uri);return if(f.isDirectory) "vnd.android.document/directory" else android.webkit.MimeTypeMap.getSingleton().getMimeTypeFromExtension(f.extension.lowercase(java.util.Locale.ROOT)) ?: "application/octet-stream" }
    override fun query(uri:Uri,projection:Array<out String>?,selection:String?,args:Array<out String>?,order:String?):Cursor { val f=resolve(requireNotNull(context),uri);val columns=projection ?: arrayOf(OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE);return MatrixCursor(columns).apply { addRow(columns.map { when(it){OpenableColumns.DISPLAY_NAME->f.name;OpenableColumns.SIZE->f.length();else->null} }) } }
    override fun openFile(uri:Uri,mode:String):ParcelFileDescriptor { require(mode=="r");val file=resolve(requireNotNull(context),uri);require(file.isFile);return ParcelFileDescriptor.open(file,ParcelFileDescriptor.MODE_READ_ONLY) }
    override fun insert(uri:Uri,values:ContentValues?):Uri?=throw UnsupportedOperationException()
    override fun update(uri:Uri,values:ContentValues?,selection:String?,args:Array<out String>?):Int=throw UnsupportedOperationException()
    override fun delete(uri:Uri,selection:String?,args:Array<out String>?):Int=throw UnsupportedOperationException()
}
