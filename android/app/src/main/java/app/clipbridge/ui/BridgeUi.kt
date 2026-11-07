package app.clipbridge.ui

import android.app.Activity
import android.content.res.ColorStateList
import android.content.res.Configuration
import android.graphics.*
import android.graphics.drawable.*
import android.view.Gravity
import android.view.View
import android.view.accessibility.AccessibilityNodeInfo
import android.widget.*

class BridgeUi(val activity: Activity) {
    val dark = activity.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK == Configuration.UI_MODE_NIGHT_YES
    val canvas = color("#E4E9F4", "#0E1422")
    val surface = color("#FFFFFF", "#232E43")
    val field = color("#F2F5FC", "#263145")
    val ink = color("#1D2845", "#EDF2FC")
    val muted = color("#586681", "#B0BDD4")
    val primary = color("#515CE0", "#A8B8FF")
    val primaryFill = color("#4B57D2", "#4A59C9")
    val tint = color("#DFE5FF", "#2E3D5E")
    val mint = color("#D8F0E5", "#1C3B36")
    val green = color("#166F54", "#97E2C9")
    val danger = color("#AD354F", "#FFA0B3")
    val line = color("#D8E0EE", "#3A475F")
    private val sheets = BridgeDialogs(this)
    val stackActions get() = UiPresentation.stackActions(activity.resources.configuration.screenWidthDp, activity.resources.configuration.fontScale)
    private fun color(light: String, night: String) = Color.parseColor(if(dark) night else light)
    fun dp(n: Int) = (n * activity.resources.displayMetrics.density + .5f).toInt()
    fun background(color: Int, radius: Int = 22) = GradientDrawable().apply { setColor(color); cornerRadius = dp(radius).toFloat() }
    fun glass(color: Int = surface, radius: Int = 24): Drawable = GlassDrawable(color, if(dark) line else Color.WHITE, primary, dp(radius).toFloat(), dark)
    fun backdrop(): Drawable = GradientDrawable(GradientDrawable.Orientation.TL_BR, intArrayOf(canvas, GlassDrawable.mix(canvas,tint,.48f), GlassDrawable.mix(canvas,mint,.4f)))
    fun ripple(drawable: Drawable, radius: Int = 18): Drawable = RippleDrawable(ColorStateList.valueOf(GlassDrawable.mix(Color.TRANSPARENT, primary, .14f)), drawable, background(Color.WHITE,radius))
    fun column(padding: Int = 0) = LinearLayout(activity).apply {
        orientation = LinearLayout.VERTICAL; setPadding(dp(padding),dp(padding),dp(padding),dp(padding))
    }
    fun row() = LinearLayout(activity).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
    fun text(value: String, size: Float = 14f, color: Int = ink, bold: Boolean = false) = TextView(activity).apply {
        text = value; textSize = size; setTextColor(color); typeface = Typeface.create(if(bold) "sans-serif-medium" else "sans-serif", if(bold) Typeface.BOLD else Typeface.NORMAL)
        includeFontPadding = true; setLineSpacing(dp(2).toFloat(),1f); setPadding(0,dp(2),0,dp(2))
    }
    fun gap(parent: LinearLayout, height: Int = 12) { parent.addView(View(activity),LinearLayout.LayoutParams(1,dp(height))) }
    /** Cards carry a visible rim so each surface separates from the canvas on its own, not by shadow alone. */
    fun card(parent: LinearLayout, color: Int = surface): LinearLayout = column(18).apply {
        background = glass(color); elevation = dp(3).toFloat(); clipToOutline = true
        parent.addView(this,LinearLayout.LayoutParams(-1,-2).apply { bottomMargin = dp(14) })
    }
    /** Accent cards sit on a near-identical canvas tone, so they get an explicit accent rim. */
    fun accentCard(parent: LinearLayout, color: Int = tint): LinearLayout = card(parent, color).apply {
        background = rimmed(color)
    }
    fun rimmed(color: Int, radius: Int = 24): Drawable = GradientDrawable().apply {
        setColor(color); cornerRadius = dp(radius).toFloat()
        setStroke(dp(1), GlassDrawable.mix(canvas, primary, if(dark) .45f else .34f))
    }
    fun button(label: String, primaryAction: Boolean = false, onClick: () -> Unit): Button = Button(activity).apply {
        text = label; isAllCaps = false; textSize = 14f; typeface = Typeface.create("sans-serif-medium",Typeface.NORMAL)
        minHeight = dp(50); minimumHeight = dp(50); minWidth = 0; minimumWidth = 0
        stateListAnimator = null; elevation = 0f
        val enabled = if(primaryAction) Color.WHITE else primary
        setTextColor(ColorStateList(arrayOf(intArrayOf(-android.R.attr.state_enabled),intArrayOf()),intArrayOf(muted,enabled)))
        val active: Drawable = if(primaryAction) GradientDrawable(GradientDrawable.Orientation.TL_BR,intArrayOf(GlassDrawable.mix(primaryFill,Color.WHITE,.12f),primaryFill)).apply {
            cornerRadius = dp(17).toFloat(); setStroke(dp(1),GlassDrawable.mix(primaryFill,Color.WHITE,.30f))
        } else glass(GlassDrawable.mix(surface,tint,.4f),17)
        background = StateListDrawable().apply {
            addState(intArrayOf(-android.R.attr.state_enabled),background(field,17))
            addState(intArrayOf(),ripple(active,17))
        }
        setPadding(dp(14),dp(10),dp(14),dp(10)); setOnClickListener { onClick() }
    }
    fun heading(parent: LinearLayout, title: String, detail: String? = null) {
        gap(parent,18)
        parent.addView(text(title,17f,ink,true).apply { isAccessibilityHeading = true; letterSpacing = -.01f })
        if(detail != null) parent.addView(text(detail,12f,muted).apply { setPadding(0,0,0,0) })
        gap(parent,10)
    }
    /** Inline section header with a right-aligned count, for lists that follow it. */
    fun sectionLabel(parent: LinearLayout, title: String, count: String) {
        val row = row()
        row.addView(text(title,12f,muted,true).apply { letterSpacing = .06f }, LinearLayout.LayoutParams(0,-2,1f))
        row.addView(text(count,12f,muted))
        parent.addView(row, LinearLayout.LayoutParams(-1,-2).apply { bottomMargin = dp(8) })
    }
    fun pageHeader(parent: LinearLayout, title: String, detail: String) {
        parent.addView(text("CLIPBRIDGE",10f,primary,true).apply { letterSpacing = .16f; importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO })
        parent.addView(text(title,27f,ink,true).apply { isAccessibilityHeading = true; letterSpacing = -.02f })
        parent.addView(text(detail,13f,muted)); gap(parent,16)
    }
    fun badge(value: String, color: Int = green): TextView = text(value,11f,color,true).apply {
        background = background(GlassDrawable.mix(surface,color,if(dark) .15f else .08f),20)
        setPadding(dp(10),dp(5),dp(10),dp(5)); gravity = Gravity.CENTER
    }
    fun iconTile(kind: Int, color: Int = primary, side: Int = 44): View = FrameLayout(activity).apply {
        background = glass(GlassDrawable.mix(surface,color,if(dark) .12f else .06f),15)
        addView(icon(kind,color),FrameLayout.LayoutParams(dp(side-18),dp(side-18),Gravity.CENTER))
    }
    fun deviceSummary(parent: LinearLayout, title: String, detail: String, kind: Int = 0, status: String? = null, connected: Boolean = false) {
        val row = row()
        row.addView(iconTile(kind),LinearLayout.LayoutParams(dp(48),dp(48)).apply { marginEnd = dp(12) })
        val labels = column(); labels.addView(text(title,18f,bold=true)); labels.addView(text(detail,12f,muted))
        row.addView(labels,LinearLayout.LayoutParams(0,-2,1f)); parent.addView(row)
        if(status != null) { gap(parent,10); parent.addView(badge(status,if(connected) green else muted)) }
    }
    fun listAction(parent: LinearLayout, title: String, detail: String? = null, kind: Int? = null, destructive: Boolean = false, onClick: () -> Unit): LinearLayout {
        val item = row().apply {
            minimumHeight = dp(56); setPadding(dp(2),dp(9),dp(2),dp(9)); background = ripple(background(Color.TRANSPARENT,16),16)
            isClickable = true; isFocusable = true
            contentDescription = title + (detail?.let { "，$it" } ?: "")
            accessibilityDelegate = object : View.AccessibilityDelegate() {
                override fun onInitializeAccessibilityNodeInfo(host: View, info: AccessibilityNodeInfo) { super.onInitializeAccessibilityNodeInfo(host,info); info.className = Button::class.java.name }
            }
            setOnClickListener { onClick() }
        }
        if(kind != null) item.addView(iconTile(kind,if(destructive) danger else primary,38),LinearLayout.LayoutParams(dp(38),dp(38)).apply { marginEnd=dp(12) })
        val labels=column(); labels.addView(text(title,14f,if(destructive) danger else ink,true))
        if(detail != null) labels.addView(text(detail,12f,muted))
        item.addView(labels,LinearLayout.LayoutParams(0,-2,1f))
        item.addView(text("›",24f,muted).apply { importantForAccessibility=View.IMPORTANT_FOR_ACCESSIBILITY_NO },LinearLayout.LayoutParams(dp(24),-2))
        parent.addView(item,LinearLayout.LayoutParams(-1,-2)); return item
    }
    fun divider(parent: LinearLayout) { parent.addView(View(activity).apply { setBackgroundColor(GlassDrawable.mix(surface,line,.65f)) },LinearLayout.LayoutParams(-1,dp(1)).apply { topMargin=dp(5); bottomMargin=dp(5) }) }
    fun actionPair(parent: LinearLayout, first: Button, second: Button) {
        val actions = if(stackActions) column() else row()
        if(stackActions) {
            actions.addView(first,LinearLayout.LayoutParams(-1,-2)); actions.addView(second,LinearLayout.LayoutParams(-1,-2).apply { topMargin=dp(8) })
        } else {
            actions.addView(first,LinearLayout.LayoutParams(0,-2,1f).apply { marginEnd=dp(4) }); actions.addView(second,LinearLayout.LayoutParams(0,-2,1f).apply { marginStart=dp(4) })
        }
        parent.addView(actions,LinearLayout.LayoutParams(-1,-2).apply { topMargin=dp(12) })
    }
    fun empty(parent: LinearLayout, title: String, detail: String, kind: Int = 0) {
        val card=card(parent,GlassDrawable.mix(surface,tint,.3f)); deviceSummary(card,title,detail,kind)
    }
    fun progress(value: Int, description: String): ProgressBar = ProgressBar(activity,null,android.R.attr.progressBarStyleHorizontal).apply {
        max = 100; progress = value.coerceIn(0,100); minimumHeight=dp(8)
        progressTintList=ColorStateList.valueOf(primaryFill); progressBackgroundTintList=ColorStateList.valueOf(line)
        contentDescription = description; accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
        layoutParams=LinearLayout.LayoutParams(-1,dp(12)).apply { topMargin=dp(8); bottomMargin=dp(4) }
    }
    fun navigationItem(label: String, kind: Int, selected: Boolean, count: Int, onClick: () -> Unit): View = column(8).apply {
        gravity=Gravity.CENTER; minimumHeight=dp(64); isSelected=selected; isClickable=true; isFocusable=true
        background=ripple(if(selected) glass(tint,20) else background(Color.TRANSPARENT,20),20)
        contentDescription=label+(if(count>0) "，$count 个待处理" else "")+(if(selected) "，已选择" else "")
        addView(icon(kind,if(selected) primary else muted),LinearLayout.LayoutParams(dp(23),dp(23)))
        val caption=text(label+(if(count>0) " · "+if(count>99) "99+" else count else ""),11f,if(selected) primary else muted,selected).apply {
            gravity=Gravity.CENTER; setPadding(0,dp(4),0,0); includeFontPadding=false
        }
        addView(caption,LinearLayout.LayoutParams(-1,-2))
        setOnClickListener { onClick() }
    }
    fun input(hintText: String, value: String = ""): EditText = EditText(activity).apply {
        hint=hintText; setText(value); textSize=16f; isSingleLine=true
        setTextColor(ink); setHintTextColor(muted); background=background(field,15)
        minimumHeight=dp(52); setPadding(dp(14),dp(12),dp(14),dp(12))
    }
    fun dialog() = sheets.builder()
    fun dismissDialogs() = sheets.dismissAll()
    fun size(bytes: Long) = UiPresentation.size(bytes)

    fun icon(kind: Int, color: Int = primary): View = object : View(activity) {
        val paint=Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color=color; style=Paint.Style.STROKE; strokeWidth=1.9f; strokeCap=Paint.Cap.ROUND; strokeJoin=Paint.Join.ROUND }
        val glyphPath=Path().apply {
            when(kind) {
                9 -> { moveTo(4f,12f); lineTo(4f,32f); lineTo(36f,32f); lineTo(36f,12f); lineTo(19f,12f); lineTo(15f,7f); lineTo(4f,7f); close() }
                10 -> { moveTo(20f,4f); lineTo(34f,10f); lineTo(32f,25f); quadTo(29f,34f,20f,38f); quadTo(11f,34f,8f,25f); lineTo(6f,10f); close() }
            }
        }
        override fun onDraw(c: Canvas) {
            super.onDraw(c); c.save(); c.scale(width/40f,height/40f)
            when(kind) {
                0 -> { c.drawRoundRect(5f,7f,35f,28f,4f,4f,paint); c.drawLine(1f,33f,39f,33f,paint); c.drawLine(15f,29f,25f,29f,paint) }
                1 -> { c.drawRoundRect(9f,4f,31f,36f,4f,4f,paint); c.drawLine(14f,14f,26f,14f,paint); c.drawLine(14f,21f,26f,21f,paint); c.drawLine(14f,28f,23f,28f,paint) }
                2 -> { c.drawRoundRect(8f,7f,32f,36f,4f,4f,paint); c.drawRoundRect(14f,3f,26f,11f,2f,2f,paint); c.drawLine(14f,19f,26f,19f,paint); c.drawLine(14f,26f,26f,26f,paint) }
                3 -> { c.drawLine(13f,5f,13f,32f,paint); c.drawLine(6f,25f,13f,32f,paint); c.drawLine(13f,32f,20f,25f,paint); c.drawLine(28f,35f,28f,8f,paint); c.drawLine(21f,15f,28f,8f,paint); c.drawLine(28f,8f,35f,15f,paint) }
                4 -> { c.drawCircle(20f,20f,11f,paint); c.drawCircle(20f,20f,4f,paint); for(i in 0..7) { c.save(); c.rotate(i*45f,20f,20f); c.drawLine(20f,3f,20f,7f,paint); c.restore() } }
                5 -> { c.drawLine(8f,21f,17f,30f,paint); c.drawLine(17f,30f,33f,10f,paint) }
                6 -> { for(i in 0..3) { c.save(); c.rotate(i*90f,20f,20f); c.drawLine(5f,14f,5f,6f,paint); c.drawLine(5f,6f,14f,6f,paint); c.restore() }; c.drawRoundRect(13f,13f,27f,27f,2f,2f,paint); c.drawLine(9f,20f,31f,20f,paint) }
                7 -> { c.drawArc(6f,6f,34f,34f,35f,270f,false,paint); c.drawLine(29f,5f,29f,14f,paint); c.drawLine(29f,14f,37f,14f,paint) }
                8 -> { c.drawRoundRect(11f,3f,29f,37f,4f,4f,paint); c.drawLine(18f,32f,22f,32f,paint) }
                9 -> c.drawPath(glyphPath,paint)
                10 -> { c.drawPath(glyphPath,paint); c.drawLine(13f,21f,18f,26f,paint); c.drawLine(18f,26f,27f,16f,paint) }
                else -> { c.drawLine(20f,5f,20f,26f,paint); c.drawLine(13f,19f,20f,26f,paint); c.drawLine(20f,26f,27f,19f,paint); c.drawLine(7f,26f,7f,34f,paint); c.drawLine(7f,34f,33f,34f,paint); c.drawLine(33f,34f,33f,26f,paint) }
            }; c.restore()
        }
    }.apply { importantForAccessibility=View.IMPORTANT_FOR_ACCESSIBILITY_NO }
}
