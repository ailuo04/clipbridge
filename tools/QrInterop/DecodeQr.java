import com.google.zxing.BinaryBitmap;
import com.google.zxing.MultiFormatReader;
import com.google.zxing.RGBLuminanceSource;
import com.google.zxing.common.HybridBinarizer;
import java.awt.image.BufferedImage;
import java.io.File;
import javax.imageio.ImageIO;

public final class DecodeQr {
    public static void main(String[] args) throws Exception {
        BufferedImage image = ImageIO.read(new File(args[0]));
        if (image == null) throw new IllegalStateException("Could not read synthetic Windows QR image");
        int width = image.getWidth(), height = image.getHeight();
        int[] pixels = image.getRGB(0, 0, width, height, null, 0, width);
        BinaryBitmap bitmap = new BinaryBitmap(new HybridBinarizer(new RGBLuminanceSource(width, height, pixels)));
        String actual = new MultiFormatReader().decode(bitmap).getText();
        String expected = "clipbridge:pair:1:0123456789abcdef0123456789abcdef:10.51.2.197";
        if (!expected.equals(actual)) throw new IllegalStateException("Decoded QR payload did not match");
        System.out.println("PASS Windows QR image decoded by the Android ZXing core version");
    }
}