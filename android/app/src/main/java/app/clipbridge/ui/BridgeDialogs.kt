package app.clipbridge.ui

import android.app.Dialog
import android.content.DialogInterface
import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.view.Gravity
import android.view.View
import android.view.Window
import android.view.WindowManager
import android.widget.*

/** App-owned sheets only: never substitutes Android permission/document/APK-install UI. */
class BridgeDialogs(private val ui: BridgeUi) {
    private data class Action(val label: String, val kind: Int, val callback: ((DialogInterface,Int) -> Unit)?)
    private val open = mutableSetOf<Dialog>()
    fun builder() = Builder()
    fun dismissAll() { open.toList().forEach { it.dismiss() }; open.clear() }

    inner class Builder {
        private var title = ""
        private var message: String? = null
        private var custom: View? = null
        private var choices: Array<String>? = null
        private var selected = -1
        private var choose: ((DialogInterface,Int) -> Unit)? = null
        private val actions = mutableListOf<Action>()

        fun setTitle(value: String) = apply { title = value }
        fun setMessage(value: String) = apply { message = value }
        fun setView(view: View) = apply { custom = view }
        fun setItems(items: Array<String>, callback: (DialogInterface,Int) -> Unit) = apply { choices=items; choose=callback }
        fun setSingleChoiceItems(items: Array<String>, checked: Int, callback: (DialogInterface,Int) -> Unit) = apply { choices=items; selected=checked; choose=callback }
        fun setPositiveButton(label: String, callback: ((DialogInterface,Int) -> Unit)? = null) = action(label,DialogInterface.BUTTON_POSITIVE,callback)
        fun setNegativeButton(label: String, callback: ((DialogInterface,Int) -> Unit)? = null) = action(label,DialogInterface.BUTTON_NEGATIVE,callback)
        fun setNeutralButton(label: String, callback: ((DialogInterface,Int) -> Unit)? = null) = action(label,DialogInterface.BUTTON_NEUTRAL,callback)
        private fun action(label: String, kind: Int, callback: ((DialogInterface,Int) -> Unit)?) = apply {
            actions.removeAll { it.kind == kind }; actions.add(Action(label,kind,callback))
        }

        fun show(): Dialog {
            val dialog=Dialog(ui.activity)
            dialog.requestWindowFeature(Window.FEATURE_NO_TITLE)
            val frame=FrameLayout(ui.activity).apply { setPadding(ui.dp(12),ui.dp(8),ui.dp(12),ui.dp(12)) }
            val sheet=ui.column(20).apply { background=ui.glass(radius=28) }
            val header=ui.row()
            header.addView(ui.text(title,20f,bold=true).apply { isAccessibilityHeading=true },LinearLayout.LayoutParams(0,-2,1f))
            header.addView(ui.button("×") { dialog.dismiss() }.apply {
                contentDescription="关闭弹窗"; textSize=22f; background=ui.ripple(ui.background(Color.TRANSPARENT,24),24)
            },LinearLayout.LayoutParams(ui.dp(48),ui.dp(48)))
            sheet.addView(header); ui.gap(sheet,8)

            val content=ui.column()
            message?.let { content.addView(ui.text(it,14f,ui.muted)); ui.gap(content,12) }
            choices?.forEachIndexed { index,label ->
                val choice=ui.listAction(content,label) { dialog.dismiss(); choose?.invoke(dialog,index) }
                if(index == selected) {
                    choice.background=ui.glass(ui.tint,16)
                    (choice.getChildAt(choice.childCount-1) as? TextView)?.apply { text="✓"; setTextColor(ui.primary) }
                    choice.isSelected=true
                }
            }
            custom?.let { view ->
                check(view.parent == null) { "弹窗内容已经显示" }
                content.addView(view,LinearLayout.LayoutParams(-1,-2))
            }
            val scroller=object : ScrollView(ui.activity) {
                override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
                    val limit=(resources.displayMetrics.heightPixels * .60f).toInt()
                    val available=if(View.MeasureSpec.getMode(heightMeasureSpec)==View.MeasureSpec.UNSPECIFIED) limit else View.MeasureSpec.getSize(heightMeasureSpec).coerceAtMost(limit)
                    super.onMeasure(widthMeasureSpec,View.MeasureSpec.makeMeasureSpec(available,View.MeasureSpec.AT_MOST))
                }
            }.apply { isFillViewport=false; clipToPadding=false; isVerticalScrollBarEnabled=false; addView(content) }
            sheet.addView(scroller,LinearLayout.LayoutParams(-1,-2,1f))
            val negative=actions.firstOrNull { it.kind==DialogInterface.BUTTON_NEGATIVE }
            val positive=actions.firstOrNull { it.kind==DialogInterface.BUTTON_POSITIVE }
            fun button(action: Action) = ui.button(action.label,action.kind==DialogInterface.BUTTON_POSITIVE) {
                dialog.dismiss(); action.callback?.invoke(dialog,action.kind)
            }
            when {
                negative != null && positive != null -> ui.actionPair(sheet,button(negative),button(positive))
                negative != null || positive != null -> { ui.gap(sheet,12); sheet.addView(button(positive ?: negative!!),LinearLayout.LayoutParams(-1,-2)) }
            }
            actions.firstOrNull { it.kind==DialogInterface.BUTTON_NEUTRAL }?.let { neutral ->
                ui.gap(sheet,8)
                sheet.addView(button(neutral).apply { setTextColor(ui.danger) },LinearLayout.LayoutParams(-1,-2))
            }
            frame.addView(sheet,FrameLayout.LayoutParams(-1,-2))
            dialog.setContentView(frame)
            dialog.setOnDismissListener { open.remove(dialog) }
            dialog.window?.apply {
                setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
                addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND)
                setDimAmount(if(ui.dark) .48f else .24f)
                setGravity(Gravity.BOTTOM)
                setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE)
            }
            open.add(dialog)
            dialog.show()
            dialog.window?.setLayout(ui.activity.resources.displayMetrics.widthPixels.coerceAtMost(ui.dp(584)),WindowManager.LayoutParams.WRAP_CONTENT)
            return dialog
        }
    }
}
