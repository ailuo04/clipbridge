#!/usr/bin/env python3
"""Exercise theme switching on a disposable, isolated Android emulator package.

Copy the Android project to a temporary directory, then in that copy only:
1. Set applicationId to a unique ID like app.clipbridge.themetest0123456789ab.
2. Change the manifest provider authority to <that applicationId>.updates.
3. Build a debug APK and install it on a local emulator, alongside the real app.
4. Run this script with --serial emulator-5554 --package <that applicationId>.

The test replaces the disposable package's interface/update preferences with
synthetic data; it refuses real devices and the normal app.clipbridge package.
--exercise-system-theme temporarily changes, then restores, emulator night mode.
"""

import argparse
import re
import struct
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zlib

PACKAGE_PATTERN = re.compile(r"app\.clipbridge\.themetest[0-9a-f]{8,32}\Z")
LIGHT = (0xF5, 0xF7, 0xFC)
DARK = (0x11, 0x18, 0x27)


class TestFailure(Exception):
    pass


class Emulator:
    def __init__(self, adb, serial, package):
        self.command = [adb, "-s", serial]
        self.package = package
        self.width = self.height = 0

    def adb(self, *args, data=None, timeout=30):
        result = subprocess.run(
            self.command + list(args), input=data, stdout=subprocess.PIPE,
            stderr=subprocess.PIPE, timeout=timeout, check=False,
        )
        if result.returncode:
            raise TestFailure("adb {} failed: {}".format(
                " ".join(args), result.stderr.decode("utf-8", "replace").strip()
            ))
        return result.stdout

    def shell(self, *args):
        return self.adb("shell", *args).decode("utf-8", "replace").strip()

    def check_isolated_emulator(self, serial):
        if not re.fullmatch(r"emulator-\d+", serial):
            raise TestFailure("Only a local emulator serial is allowed.")
        if not PACKAGE_PATTERN.fullmatch(self.package):
            raise TestFailure("Only an app.clipbridge.themetest<hex> package is allowed.")
        if self.shell("getprop", "ro.kernel.qemu") != "1":
            raise TestFailure("The selected device is not an Android emulator.")
        self.shell("run-as", self.package, "id")  # Also requires a debuggable package.
        size = self.shell("wm", "size")
        sizes = re.findall(r"(?:Physical|Override) size:\s*(\d+)x(\d+)", size)
        if not sizes:
            raise TestFailure("Could not determine emulator screen size.")
        self.width, self.height = map(int, sizes[-1])

    def initialize(self):
        # Never clear data on a normal installation. Only the named disposable
        # test package may have these test-specific preferences replaced.
        self.shell("am", "force-stop", self.package)
        self.shell("run-as", self.package, "mkdir", "-p", "shared_prefs")
        prefs = {
            "interface": b'<?xml version="1.0" encoding="utf-8" standalone="yes"?>'
                         b'<map><int name="theme" value="0" />'
                         b'<boolean name="connection" value="false" />'
                         b'<string name="theme_test_sentinel">preserve</string></map>',
            "updates": b'<?xml version="1.0" encoding="utf-8" standalone="yes"?>'
                       b'<map><boolean name="automatic" value="false" />'
                       b'<boolean name="download" value="false" /></map>',
        }
        for name, contents in prefs.items():
            self.adb("shell", "run-as", self.package, "sh", "-c",
                     "'cat > shared_prefs/{}.xml'".format(name), data=contents)

    def launch(self):
        self.shell("am", "force-stop", self.package)
        output = self.shell("am", "start", "-W", "-n",
                            self.package + "/app.clipbridge.MainActivity")
        if "Error" in output or "Exception" in output:
            raise TestFailure("Unable to start isolated activity: " + output)
        self.wait_foreground("cold launch")

    def wait_foreground(self, operation):
        # Activity recreation is asynchronous. Require a stable foreground app,
        # not just the previous activity immediately after a click.
        time.sleep(1)
        deadline = time.monotonic() + 15
        consecutive = 0
        while time.monotonic() < deadline:
            activities = self.shell("dumpsys", "activity", "activities")
            foreground = re.search(
                r"topResumedActivity=.*?\b" + re.escape(self.package) + r"/",
                activities,
            )
            pid = self.shell("pidof", self.package) if foreground else ""
            consecutive = consecutive + 1 if foreground and pid else 0
            if consecutive >= 2:
                return
            time.sleep(0.5)
        raise TestFailure("{}: isolated app is no longer foreground/alive".format(operation))

    def ui(self):
        for _ in range(3):
            output = self.adb("exec-out", "uiautomator", "dump", "/dev/tty", timeout=40)
            start, end = output.find(b"<?xml"), output.rfind(b"</hierarchy>")
            if start >= 0 and end >= 0:
                try:
                    return ET.fromstring(output[start:end + len(b"</hierarchy>")])
                except ET.ParseError:
                    pass
            time.sleep(0.5)
        raise TestFailure("Could not obtain the emulator accessibility hierarchy.")

    def tap(self, node):
        bounds = re.fullmatch(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", node.get("bounds", ""))
        if not bounds:
            raise TestFailure("A UI element has no usable bounds.")
        x1, y1, x2, y2 = map(int, bounds.groups())
        self.shell("input", "tap", str((x1 + x2) // 2), str((y1 + y2) // 2))
        time.sleep(0.4)

    def open_settings(self):
        root = self.ui()
        button = next((n for n in root.iter("node")
                       if n.get("content-desc", "").split("，")[0] == "设置"), None)
        if button is None:
            raise TestFailure("Settings navigation was not found in the isolated app.")
        self.tap(button)

    def appearance(self):
        for _ in range(8):
            root = self.ui()
            button = next((n for n in root.iter("node")
                           if n.get("text", "").startswith("外观 · ")), None)
            if button is not None:
                return button
            self.shell("input", "swipe", str(self.width // 2), str(self.height * 3 // 4),
                       str(self.width // 2), str(self.height // 3), "300")
            time.sleep(0.3)
        raise TestFailure("Appearance setting was not found after scrolling.")

    def assert_appearance_label(self, label):
        self.open_settings()
        actual = self.appearance().get("text")
        if actual != "外观 · " + label:
            raise TestFailure("Expected appearance label {!r}, observed {!r}.".format(label, actual))
        print("PASS: appearance setting displays", label, flush=True)

    def choose(self, label):
        self.open_settings()
        self.tap(self.appearance())
        root = self.ui()
        choice = next((n for n in root.iter("node") if n.get("text") == label), None)
        if choice is None:
            raise TestFailure("Theme option {!r} was not found.".format(label))
        self.tap(choice)
        self.wait_foreground("selecting " + label)

    def assert_preference(self, value):
        raw = self.adb("shell", "run-as", self.package, "cat", "shared_prefs/interface.xml")
        root = ET.fromstring(raw)
        theme = root.find("int[@name='theme']")
        sentinel = root.find("string[@name='theme_test_sentinel']")
        connection = root.find("boolean[@name='connection']")
        if theme is None or theme.get("value") != str(value):
            raise TestFailure("Expected saved theme {}, but it was not present.".format(value))
        if sentinel is None or sentinel.text != "preserve" or connection is None or connection.get("value") != "false":
            raise TestFailure("Unrelated test preferences were not preserved.")

    def assert_canvas(self, expected):
        # Sample the application's unobstructed left canvas edge, away from the
        # status/navigation bars. Check actual rendered pixels, not only prefs.
        deadline = time.monotonic() + 10
        actual = None
        while time.monotonic() < deadline:
            png = self.adb("exec-out", "screencap", "-p", timeout=30)
            actual = png_pixel(png, 5, self.height // 6)
            if all(abs(a - b) <= 3 for a, b in zip(actual, expected)):
                return
            time.sleep(0.5)
        raise TestFailure("Expected canvas RGB {}, observed {}.".format(expected, actual))

    def assert_theme(self, value, expected, label):
        self.wait_foreground(label)
        self.assert_preference(value)
        if expected is not None:
            self.assert_canvas(expected)
        print("PASS:", label, flush=True)

    def system_night(self):
        text = self.shell("cmd", "uimode", "night")
        match = re.fullmatch(r"Night mode: (no|yes|auto)", text)
        if not match:
            raise TestFailure("Cannot safely restore current system night mode: " + text)
        return match.group(1)

    def set_system_night(self, mode):
        self.shell("cmd", "uimode", "night", mode)
        if self.system_night() != mode:
            raise TestFailure("System night mode did not change to " + mode)
        time.sleep(1)


def png_pixel(data, x, y):
    """Read one RGB pixel from an 8-bit non-interlaced Android screenshot PNG."""
    if not data.startswith(b"\x89PNG\r\n\x1a\n"):
        raise TestFailure("Emulator screenshot is not PNG data.")
    pos, compressed, header = 8, bytearray(), None
    while pos + 12 <= len(data):
        length = struct.unpack_from(">I", data, pos)[0]
        kind = data[pos + 4:pos + 8]
        chunk = data[pos + 8:pos + 8 + length]
        pos += length + 12
        if kind == b"IHDR":
            header = struct.unpack(">IIBBBBB", chunk)
        elif kind == b"IDAT":
            compressed.extend(chunk)
        elif kind == b"IEND":
            break
    if header is None:
        raise TestFailure("Emulator screenshot has no PNG header.")
    width, height, depth, color, compression, filtering, interlace = header
    if depth != 8 or color not in (2, 6) or compression or filtering or interlace or not (0 <= x < width and 0 <= y < height):
        raise TestFailure("Unsupported screenshot PNG format or pixel position.")
    bpp = 3 if color == 2 else 4
    stride, prefix = width * bpp, (x + 1) * bpp
    pixels = zlib.decompress(compressed)
    previous = bytearray(prefix)
    for row_index in range(y + 1):
        offset = row_index * (stride + 1)
        if offset + stride >= len(pixels):
            raise TestFailure("Incomplete screenshot PNG data.")
        filter_type = pixels[offset]
        row = bytearray(pixels[offset + 1:offset + 1 + prefix])
        for i in range(prefix):
            left = row[i - bpp] if i >= bpp else 0
            above = previous[i]
            upper_left = previous[i - bpp] if i >= bpp else 0
            if filter_type == 0:
                predictor = 0
            elif filter_type == 1:
                predictor = left
            elif filter_type == 2:
                predictor = above
            elif filter_type == 3:
                predictor = (left + above) // 2
            elif filter_type == 4:
                p = left + above - upper_left
                distances = (abs(p - left), abs(p - above), abs(p - upper_left))
                predictor = (left, above, upper_left)[distances.index(min(distances))]
            else:
                raise TestFailure("Unsupported screenshot PNG filter.")
            row[i] = (row[i] + predictor) & 255
        previous = row
    return tuple(previous[x * bpp:x * bpp + 3])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", default="adb", help="Path to adb (default: PATH)")
    parser.add_argument("--serial", required=True, help="Local emulator serial")
    parser.add_argument("--package", required=True, help="Disposable debug package ID")
    parser.add_argument("--exercise-system-theme", action="store_true",
                        help="Temporarily change emulator night mode and restore it")
    args = parser.parse_args()
    emulator = Emulator(args.adb, args.serial, args.package)
    emulator.check_isolated_emulator(args.serial)
    original_system = emulator.system_night()
    system_changed = False
    try:
        emulator.initialize()
        emulator.launch()
        emulator.choose("浅色")
        emulator.assert_theme(1, LIGHT, "light theme after switching")
        emulator.assert_appearance_label("浅色")
        emulator.launch()
        emulator.assert_theme(1, LIGHT, "light theme after cold launch")
        emulator.choose("深色")
        emulator.assert_theme(2, DARK, "dark theme after switching")
        emulator.assert_appearance_label("深色")
        emulator.launch()
        emulator.assert_theme(2, DARK, "dark theme after cold launch")
        if args.exercise_system_theme:
            system_changed = True
            emulator.set_system_night("no")
            emulator.assert_theme(2, DARK, "dark override while system is light")
            emulator.choose("浅色")
            emulator.set_system_night("yes")
            emulator.assert_theme(1, LIGHT, "light override while system is dark")
            emulator.launch()
            emulator.assert_theme(1, LIGHT, "light override after cold launch")
            emulator.choose("跟随系统")
            emulator.assert_theme(0, DARK, "follow system in dark mode")
            emulator.assert_appearance_label("跟随系统")
            emulator.launch()
            emulator.assert_theme(0, DARK, "follow system dark after cold launch")
            emulator.set_system_night("no")
            emulator.assert_theme(0, LIGHT, "follow system after switching to light")
            emulator.launch()
            emulator.assert_theme(0, LIGHT, "follow system light after cold launch")
        else:
            emulator.choose("跟随系统")
            expected = {"no": LIGHT, "yes": DARK}.get(original_system)
            emulator.assert_theme(0, expected, "follow system after switching")
            emulator.assert_appearance_label("跟随系统")
            emulator.launch()
            emulator.assert_theme(0, expected, "follow system after cold launch")
    finally:
        if system_changed:
            emulator.set_system_night(original_system)
            print("Restored emulator system night mode:", original_system, flush=True)
    print("PASS: Android theme regression", flush=True)


if __name__ == "__main__":
    try:
        main()
    except (TestFailure, OSError, subprocess.TimeoutExpired, ET.ParseError, zlib.error) as error:
        print("FAIL:", error, file=sys.stderr, flush=True)
        sys.exit(1)
