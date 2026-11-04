package app.clipbridge.ui

import android.graphics.*
import android.graphics.drawable.Drawable

/** Locally painted glass; works on Android 10 without screen capture or a blur service. */
internal class GlassDrawable(
    private val fill: Int,
    private val edge: Int,
    private val accent: Int,
    private val radius: Float,
    private val dark: Boolean
) : Drawable() {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private var opacity = 255
    private var filter: ColorFilter? = null

    override fun draw(canvas: Canvas) {
        if (bounds.isEmpty) return
        val rect = RectF(bounds).apply { inset(.75f, .75f) }
        val top = mix(fill, Color.WHITE, if (dark) .055f else .38f)
        paint.alpha = opacity; paint.colorFilter = filter
        paint.style = Paint.Style.FILL
        paint.shader = LinearGradient(rect.left, rect.top, rect.right, rect.bottom,
            intArrayOf(top, fill, mix(fill, accent, .035f)), null, Shader.TileMode.CLAMP)
        canvas.drawRoundRect(rect, radius, radius, paint)
        val save = canvas.save()
        val path = Path().apply { addRoundRect(rect, radius, radius, Path.Direction.CW) }
        canvas.clipPath(path)
        paint.shader = RadialGradient(rect.right * .85f, rect.top,
            rect.width().coerceAtLeast(1f), Color.argb(if (dark) 20 else 34, Color.red(accent), Color.green(accent), Color.blue(accent)), Color.TRANSPARENT, Shader.TileMode.CLAMP)
        canvas.drawRect(rect, paint)
        canvas.restoreToCount(save)
        paint.shader = null; paint.color = edge; paint.alpha = opacity
        paint.style = Paint.Style.STROKE; paint.strokeWidth = 1.4f
        canvas.drawRoundRect(rect, radius, radius, paint)
        paint.style = Paint.Style.FILL
    }
    override fun getOutline(outline: Outline) { outline.setRoundRect(bounds,radius) }
    override fun setAlpha(alpha: Int) { opacity = alpha.coerceIn(0,255); invalidateSelf() }
    override fun setColorFilter(colorFilter: ColorFilter?) { filter = colorFilter; invalidateSelf() }
    @Deprecated("Drawable opacity is no longer used") override fun getOpacity() = PixelFormat.TRANSLUCENT

    companion object {
        fun mix(from: Int, to: Int, fraction: Float): Int {
            val f = fraction.coerceIn(0f, 1f)
            fun channel(a: Int,b: Int) = (a + (b-a)*f).toInt()
            return Color.argb(channel(Color.alpha(from),Color.alpha(to)), channel(Color.red(from),Color.red(to)), channel(Color.green(from),Color.green(to)), channel(Color.blue(from),Color.blue(to)))
        }
    }
}
