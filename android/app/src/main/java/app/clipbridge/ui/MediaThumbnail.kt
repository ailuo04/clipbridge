package app.clipbridge.ui

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.widget.ImageView
import java.util.concurrent.Executors

/** Display-only previews. The original URI is always used for transfer and opening. */
object MediaThumbnail {
    private val workers=Executors.newFixedThreadPool(2)
    private val cache=object: android.util.LruCache<String,Bitmap>(8*1024*1024) {
        override fun sizeOf(key:String,value:Bitmap)=value.byteCount
    }
    fun load(view:ImageView,uri:Uri) {
        val key=uri.toString(); view.tag=key
        cache.get(key)?.let { view.setImageBitmap(it); return }
        val context=view.context.applicationContext
        val weak=java.lang.ref.WeakReference(view)
        workers.execute {
            val image=runCatching {
                val type=context.contentResolver.getType(uri).orEmpty()
                if(type.startsWith("video/")) {
                    val reader=MediaMetadataRetriever()
                    try { reader.setDataSource(context,uri); reader.getScaledFrameAtTime(-1,MediaMetadataRetriever.OPTION_CLOSEST_SYNC,280,180) }
                    finally { reader.release() }
                } else {
                    val bounds=BitmapFactory.Options().apply { inJustDecodeBounds=true }
                    context.contentResolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it,null,bounds) }
                    if(bounds.outWidth<=0 || bounds.outHeight<=0) null else {
                        var sample=1
                        while(bounds.outWidth/sample>560 || bounds.outHeight/sample>360) sample*=2
                        val options=BitmapFactory.Options().apply { inSampleSize=sample }
                        context.contentResolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it,null,options) }
                    }
                }
            }.getOrNull()
            if(image!=null) {
                cache.put(key,image)
                weak.get()?.post { weak.get()?.takeIf { it.tag==key }?.setImageBitmap(image) }
            }
        }
    }
}
