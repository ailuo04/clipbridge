package app.clipbridge.ui
import android.app.Activity
import android.content.res.Configuration
import android.graphics.*
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.content.res.ColorStateList
import android.view.View
import android.view.Gravity
import android.widget.*
class BridgeUi(val activity: Activity) {
    val dark=activity.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK==Configuration.UI_MODE_NIGHT_YES
    val canvas=Color.parseColor(if(dark) "#111827" else "#F5F7FC")
    val surface=Color.parseColor(if(dark) "#1D293D" else "#FFFFFF")
    val ink=Color.parseColor(if(dark) "#EDF2FC" else "#172644")
    val muted=Color.parseColor(if(dark) "#A6B3CC" else "#73809A")
    val primary=Color.parseColor(if(dark) "#91AAFF" else "#3159E5")
    val tint=Color.parseColor(if(dark) "#2B3B61" else "#EDF2FF")
    val mint=Color.parseColor(if(dark) "#173F3B" else "#EAF4F1")
    val green=Color.parseColor(if(dark) "#80DDC3" else "#167B63")
    fun dp(n: Int)=(n*activity.resources.displayMetrics.density).toInt()
    fun background(color: Int,radius: Int=20)=GradientDrawable().apply { setColor(color); cornerRadius=dp(radius).toFloat() }
    fun column(padding: Int=0)=LinearLayout(activity).apply { orientation=LinearLayout.VERTICAL; setPadding(dp(padding),dp(padding),dp(padding),dp(padding)) }
    fun row()=LinearLayout(activity).apply { orientation=LinearLayout.HORIZONTAL; gravity=Gravity.CENTER_VERTICAL }
    fun text(value: String,size: Float=14f,color: Int=ink,bold: Boolean=false)=TextView(activity).apply { text=value; textSize=size; setTextColor(color); if(bold) setTypeface(typeface,Typeface.BOLD); setPadding(0,dp(3),0,dp(3)) }
    fun gap(parent: LinearLayout,height: Int=12) { parent.addView(View(activity),LinearLayout.LayoutParams(1,dp(height))) }
    fun card(parent: LinearLayout,color: Int=surface): LinearLayout = column(18).apply { background=background(color); parent.addView(this,LinearLayout.LayoutParams(-1,-2).apply { bottomMargin=dp(12) }) }
    fun button(label: String,primaryAction: Boolean=false,onClick: ()->Unit): Button = Button(activity).apply {
        text=label; isAllCaps=false; textSize=14f; minHeight=dp(48); minimumHeight=dp(48); minWidth=0; minimumWidth=0
        setTextColor(if(primaryAction && !dark) Color.WHITE else primary)
        val fill=if(primaryAction) { if(dark) tint else primary } else tint
        background=RippleDrawable(ColorStateList.valueOf(Color.parseColor("#223159E5")),background(fill,13),null)
        setPadding(dp(12),dp(8),dp(12),dp(8)); setOnClickListener { onClick() }
    }
    fun heading(parent: LinearLayout,title: String,detail: String?=null) { gap(parent,10); parent.addView(text(title,20f,bold=true)); if(detail!=null) parent.addView(text(detail,12f,muted)); gap(parent,10) }
    fun icon(kind: Int,color: Int=primary): View=object : View(activity) {
        val paint=Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color=color; style=Paint.Style.STROKE; strokeWidth=2.2f; strokeCap=Paint.Cap.ROUND; strokeJoin=Paint.Join.ROUND }
        override fun onDraw(c: Canvas) {
            super.onDraw(c); c.save(); c.scale(width/40f,height/40f)
            when(kind) {
                0 -> { c.drawRoundRect(5f,7f,35f,28f,3f,3f,paint); c.drawLine(1f,33f,39f,33f,paint); c.drawLine(15f,29f,25f,29f,paint) }
                1 -> { c.drawRoundRect(9f,4f,31f,36f,3f,3f,paint); c.drawLine(14f,14f,26f,14f,paint); c.drawLine(14f,21f,26f,21f,paint); c.drawLine(14f,28f,23f,28f,paint) }
                2 -> { c.drawRoundRect(8f,7f,32f,36f,3f,3f,paint); c.drawRoundRect(14f,3f,26f,11f,2f,2f,paint); c.drawLine(14f,19f,26f,19f,paint); c.drawLine(14f,26f,26f,26f,paint) }
                3 -> { c.drawLine(13f,5f,13f,32f,paint); c.drawLine(6f,25f,13f,32f,paint); c.drawLine(13f,32f,20f,25f,paint); c.drawLine(28f,35f,28f,8f,paint); c.drawLine(21f,15f,28f,8f,paint); c.drawLine(28f,8f,35f,15f,paint) }
                4 -> { c.drawCircle(20f,20f,12f,paint); c.drawCircle(20f,20f,4f,paint); for(i in 0..7) { c.save(); c.rotate(i*45f,20f,20f); c.drawLine(20f,3f,20f,7f,paint); c.restore() } }
                else -> { c.drawLine(8f,21f,17f,30f,paint); c.drawLine(17f,30f,33f,10f,paint) }
            }; c.restore()
        }
    }.apply { importantForAccessibility=View.IMPORTANT_FOR_ACCESSIBILITY_NO }
    fun size(bytes: Long): String {
        if(bytes<1024) return "$bytes B"; if(bytes<1048576) return String.format(java.util.Locale.getDefault(),"%.1f KB",bytes/1024.0)
        if(bytes<1073741824) return String.format(java.util.Locale.getDefault(),"%.1f MB",bytes/1048576.0)
        return String.format(java.util.Locale.getDefault(),"%.2f GB",bytes/1073741824.0)
    }
}
