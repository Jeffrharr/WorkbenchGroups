#!/usr/bin/env python3
"""Crop and shrink harness captures into Tests/Screenshots/ for embedding in a PR.

Screenshots have to be committed: GitHub renders `raw.githubusercontent.com` URLs inline, and
release-asset URLs redirect to a signed URL it will not proxy, so those arrive as a download
link instead of a picture. Committing them is therefore not a preference, it is the only way a
reviewer sees the evidence without opening files.

What is not forced is committing 2.3 MB of it. A raw 1920x1080 capture is mostly desert. Cropping
to the region under test and quantising to a 256-colour palette takes it to roughly 300 KB with no
loss of anything legible — and the result is easier to read, because it is not 60% sand.

Run after Tests/run_roundtrip.sh:  python3 Tests/collect_captures.py
"""

import os
import sys

from PIL import Image

HARNESS_REPORTS = os.path.expanduser(
    "~/Developer/RimWorldMods/RimWorldTestHarness/Runner/reports"
)
DEST = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Screenshots")

# Left edge through the second bench: bills tab, both benches with the connection line between
# them, the inspect pane, and the gizmo bar. Everything right of this is alerts and the date
# readout; everything above is sky.
CROP = (0, 360, 1210, 1050)

# Anything larger than this is a capture nobody cropped. The limit is deliberately close to the
# observed ~300 KB so it fails while the fix is still "crop it", not after a habit forms.
MAX_BYTES = 500 * 1024


def shrink(name: str) -> int:
    source = os.path.join(HARNESS_REPORTS, name)
    if not os.path.exists(source):
        raise SystemExit(f"no capture at {source} — run Tests/run_roundtrip.sh first")

    image = Image.open(source).crop(CROP)

    # Adaptive palette rather than a resize: RimWorld's UI is flat colour and crisp text, so
    # quantising is nearly lossless here while downscaling would blur exactly the row labels and
    # icons the capture exists to show.
    image = image.convert("P", palette=Image.ADAPTIVE, colors=256)

    os.makedirs(DEST, exist_ok=True)
    target = os.path.join(DEST, name)
    image.save(target, optimize=True)

    size = os.path.getsize(target)
    if size > MAX_BYTES:
        raise SystemExit(f"{name} is {size // 1024} KiB, over the {MAX_BYTES // 1024} KiB budget")

    return size


def main() -> None:
    names = sys.argv[1:] or ["seq_5_active_bill.png"]
    for name in names:
        print(f"{name}: {shrink(name) // 1024} KiB")


if __name__ == "__main__":
    main()
