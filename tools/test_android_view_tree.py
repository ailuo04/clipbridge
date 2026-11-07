"""Headless guard against re-adding a view that BridgeUi.card already parented.

A second addView on a view that already has a parent throws
IllegalStateException while the page is built, so no unit test that skips the
view tree can catch it. 0.5.5 shipped exactly that crash: MainActivity
conversationPage() called ui.card(body) (which already adds the card to body)
and then added the same card to body again, so opening the conversation page
crashed and the page never rendered again.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
ACTIVITY = ROOT / "android/app/src/main/java/app/clipbridge/MainActivity.kt"
UI = ROOT / "android/app/src/main/java/app/clipbridge/ui/BridgeUi.kt"

FUNCTION = re.compile(r'fun\s+(\w+)\s*\(')


def self_parenting_helpers(source):
    """BridgeUi helpers that add the view they return to a caller-supplied parent."""
    helpers = set()
    for match in FUNCTION.finditer(source):
        start = source.find("{", match.end())
        if start < 0:
            continue
        depth = 0
        end = -1
        for index in range(start, len(source)):
            if source[index] == "{":
                depth += 1
            elif source[index] == "}":
                depth -= 1
                if depth == 0:
                    end = index
                    break
        if end < 0:
            continue
        if re.search(r'parent\.addView\(this', source[start:end + 1]):
            helpers.add(match.group(1))
    return helpers


def double_parented(source, helper):
    """Views assigned from ui.<helper>() that are also passed to an addView call."""
    found = []
    for match in re.finditer(r'^\s*(?:val\s+)?(\w+)\s*=\s*ui\.' + helper + r'\(', source, re.MULTILINE):
        name = match.group(1)
        if re.search(r'\.addView\(\s*' + re.escape(name) + r'\s*[,)]', source):
            found.append((source.count("\n", 0, match.start()) + 1, name))
    return found


class AndroidViewTreeTest(unittest.TestCase):
    def test_card_is_parented_by_its_helper(self):
        helpers = self_parenting_helpers(UI.read_text(encoding="utf-8"))
        self.assertIn("card", helpers, "BridgeUi.card must keep adding itself to its parent")

    def test_self_parenting_helpers_are_not_added_twice(self):
        helpers = self_parenting_helpers(UI.read_text(encoding="utf-8"))
        source = ACTIVITY.read_text(encoding="utf-8")
        for helper in sorted(helpers):
            with self.subTest(helper=helper):
                self.assertEqual(
                    double_parented(source, helper), [],
                    "ui." + helper + "() already parents the view; adding it to a parent again "
                    "throws IllegalStateException while rendering",
                )


if __name__ == "__main__":
    unittest.main()
