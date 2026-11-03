package app.clipbridge.protocol

import com.google.zxing.BarcodeFormat
import com.google.zxing.BinaryBitmap
import com.google.zxing.MultiFormatReader
import com.google.zxing.RGBLuminanceSource
import com.google.zxing.common.HybridBinarizer
import com.google.zxing.qrcode.QRCodeWriter
import org.junit.Assert.assertEquals
import org.junit.Test

class QrInteropTest {
    @Test fun scannerDecodesAndValidatesInvitation() {
        val text="clipbridge:pair:1:0123456789abcdef0123456789abcdef:10.51.2.197"
        val matrix=QRCodeWriter().encode(text,BarcodeFormat.QR_CODE,256,256)
        val pixels=IntArray(matrix.width*matrix.height) { index ->
            if(matrix[index%matrix.width,index/matrix.width]) 0xff000000.toInt() else 0xffffffff.toInt()
        }
        val bitmap=BinaryBitmap(HybridBinarizer(RGBLuminanceSource(matrix.width,matrix.height,pixels)))
        val invitation=PairingInvitationCodec.parse(MultiFormatReader().decode(bitmap).text)
        assertEquals("0123456789abcdef0123456789abcdef",invitation.deviceId)
        assertEquals("10.51.2.197",invitation.address)
    }
}