package app.clipbridge.ui

import org.junit.Assert.*
import org.junit.Test

class UiPresentationTest {
    @Test fun progressUsesRealCountersAndClampsInvalidValues() {
        assertEquals(50, UiPresentation.percent(5L, 10L))
        assertEquals(0, UiPresentation.percent(-5L, 10L))
        assertEquals(100, UiPresentation.percent(15L, 10L))
        assertEquals(0, UiPresentation.percent(5L, 0L))
        assertEquals(0, UiPresentation.percent(5L, -1L))
    }
    @Test fun largeTransfersDoNotOverflowDuringPercentageCalculation() {
        assertEquals(50, UiPresentation.percent(Long.MAX_VALUE / 2, Long.MAX_VALUE))
        assertEquals(100, UiPresentation.percent(Long.MAX_VALUE, Long.MAX_VALUE))
        assertEquals(25, UiPresentation.percent(1024L * 1024 * 1024, 4L * 1024 * 1024 * 1024))
    }
    @Test fun activeTransfersAreSeparateFromResults() {
        for (state in listOf("准备文件", "等待对方接受", "传输中")) assertEquals("active", UiPresentation.transferGroup(state))
        assertEquals("waiting", UiPresentation.transferGroup("待接受"))
        assertEquals("finished", UiPresentation.transferGroup("对方已保存并校验"))
        assertEquals("finished", UiPresentation.transferGroup("已保存到 Download/ClipBridge"))
        assertEquals("unsuccessful", UiPresentation.transferGroup("失败：连接中断"))
        assertEquals("unsuccessful", UiPresentation.transferGroup("已取消"))
        assertEquals("unsuccessful", UiPresentation.transferGroup("已拒绝或超时"))
        assertEquals("other", UiPresentation.transferGroup("未来的未知状态"))
    }
    @Test fun preparationCountersAreNotPresentedAsFileBytes() {
        assertEquals("files", UiPresentation.progressUnit("准备文件"))
        assertEquals("bytes", UiPresentation.progressUnit("传输中"))
    }
    @Test fun narrowScreensAndLargeTextStackActions() {
        assertEquals(true, UiPresentation.stackActions(320, 1.0f))
        assertEquals(false, UiPresentation.stackActions(393, 1.0f))
        assertEquals(true, UiPresentation.stackActions(393, 1.4f))
        assertEquals(true, UiPresentation.stackActions(800, 2.0f))
    }
    @Test fun updateStatesDoNotImplyAutomaticInstallation() {
        assertEquals("checking", UiPresentation.updateKind("正在检查更新…", false))
        assertEquals("downloading", UiPresentation.updateKind("正在下载 0.6.0…", false))
        assertEquals("ready", UiPresentation.updateKind("更新 0.6.0 已就绪", true))
        assertEquals("error", UiPresentation.updateKind("更新未完成：签名无效", false))
        assertEquals("latest", UiPresentation.updateKind("当前已是最新版本", false))
        assertEquals("idle", UiPresentation.updateKind("更新任务已取消", false))
    }
    @Test fun readyCachedPackageWinsOverASubsequentCheckWarning() {
        assertEquals("ready", UiPresentation.updateKind("更新 0.6.0 已就绪；暂未检查到更新信息", true))
    }
    @Test fun pendingRestorationIsNotOverwrittenByTheFirstEmptyFrame() {
        assertEquals(640, UiPresentation.scrollOffset(640,0,true,false))
        assertEquals(280, UiPresentation.scrollOffset(640,280,false,false))
        assertEquals(0, UiPresentation.scrollOffset(640,280,true,true))
        assertEquals(0, UiPresentation.scrollOffset(-10,-20,true,false))
    }
    @Test fun byteSizesHaveConsistentReadableUnits() {
        assertEquals("0 B", UiPresentation.size(-1L))
        assertEquals("512 B", UiPresentation.size(512L))
        assertEquals("1.0 KB", UiPresentation.size(1024L))
        assertEquals("1.0 MB", UiPresentation.size(1048576L))
        assertEquals("1.00 GB", UiPresentation.size(1073741824L))
    }
}
