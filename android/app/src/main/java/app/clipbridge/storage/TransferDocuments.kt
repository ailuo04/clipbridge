package app.clipbridge.storage

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import app.clipbridge.protocol.FileProtocol

data class TransferDocument(val uri:Uri,val name:String,val relativePath:String?,val directory:Boolean)
object TransferDocuments {
    fun expand(context:Context,selected:List<Uri>):List<TransferDocument> {
        val tree=selected.any { DocumentsContract.isTreeUri(it) };val result=mutableListOf<TransferDocument>();val roots=mutableSetOf<String>()
        fun name(uri:Uri):String=context.contentResolver.query(uri,arrayOf(android.provider.OpenableColumns.DISPLAY_NAME),null,null,null)?.use { if(it.moveToFirst()) it.getString(0) else null } ?: error("无法读取文件名")
        fun visit(uri:Uri,relative:String?,directory:Boolean,root:Uri) {
            require(result.size<256) { "一次最多发送 256 个文件和目录条目，请分批发送" };val display=relative?.substringAfterLast('/') ?: name(uri);FileProtocol.validateName(display);relative?.let(FileProtocol::validateRelativePath);result.add(TransferDocument(uri,display,relative,directory))
            if(directory) { val id=DocumentsContract.getDocumentId(uri);val children=DocumentsContract.buildChildDocumentsUriUsingTree(root,id);context.contentResolver.query(children,arrayOf(DocumentsContract.Document.COLUMN_DOCUMENT_ID,DocumentsContract.Document.COLUMN_DISPLAY_NAME,DocumentsContract.Document.COLUMN_MIME_TYPE),null,null,null)?.use { c->while(c.moveToNext()){val childName=c.getString(1);FileProtocol.validateName(childName);visit(DocumentsContract.buildDocumentUriUsingTree(root,c.getString(0)),relative+"/"+childName,c.getString(2)==DocumentsContract.Document.MIME_TYPE_DIR,root)} } ?: error("无法读取文件夹") }
        }
        selected.forEach { input->val isTree=DocumentsContract.isTreeUri(input);val uri=if(isTree) DocumentsContract.buildDocumentUriUsingTree(input,DocumentsContract.getTreeDocumentId(input)) else input;val display=name(uri);var unique=display;var suffix=1;while(!roots.add(unique.lowercase(java.util.Locale.ROOT))){unique=display+" ("+suffix+++")"};visit(uri,if(tree)unique else null,isTree,input) }
        return result
    }
}
