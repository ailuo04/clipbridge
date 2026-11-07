"""Headless checks of the actual mobile color tokens; does not render or launch Android."""
from pathlib import Path
import re
import unittest

SOURCE = (Path(__file__).resolve().parents[1] / "android/app/src/main/java/app/clipbridge/ui/BridgeUi.kt").read_text(encoding="utf-8")


def token(name, dark):
    match = re.search(r'val ' + name + r' = color\("(#[0-9A-Fa-f]{6})", "(#[0-9A-Fa-f]{6})"\)', SOURCE)
    if match is None:
        raise AssertionError("Missing readable UI token: " + name)
    return tuple(int(match.group(2 if dark else 1)[i:i+2], 16) for i in (1, 3, 5))


def contrast(a, b):
    def luminance(rgb):
        parts = [v / 255 for v in rgb]
        return sum(weight * (v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4)
                   for v, weight in zip(parts, (.2126, .7152, .0722)))
    x, y = sorted((luminance(a), luminance(b)))
    return (y + .05) / (x + .05)


class MobileStyleTest(unittest.TestCase):
    def test_primary_white_labels_remain_legible_at_both_gradient_stops(self):
        amount = re.search(r'GlassDrawable.mix\(primaryFill,Color.WHITE,([.0-9]+)f\)', SOURCE)
        self.assertIsNotNone(amount, "Primary gradient highlight must be included in the contrast check")
        for dark in (False, True):
            fill = token("primaryFill", dark)
            top = tuple(int(v + (255 - v) * float(amount.group(1))) for v in fill)
            for stop in (fill, top):
                with self.subTest(dark=dark, stop=stop):
                    self.assertGreaterEqual(contrast((255, 255, 255), stop), 4.5)

    def test_secondary_text_is_readable_on_main_card_surfaces(self):
        for dark in (False, True):
            for background in ("canvas", "surface", "tint"):
                with self.subTest(dark=dark, background=background):
                    self.assertGreaterEqual(contrast(token("muted", dark), token(background, dark)), 4.5)


if __name__ == "__main__":
    unittest.main()
