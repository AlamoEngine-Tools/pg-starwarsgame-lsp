"""Writes a mega texture pair for PRV-38 (#166): the atlas as a Windows bitmap under the .tga name,
which is how two shipped mods keep theirs, and the .mtd naming one 32x32 icon in it.

    python make_icons.py C:\\temp\\aet-test-PRV-bmp\\Data\\Art\\Textures\\mt_commandbar

The bitmap: BITMAPINFOHEADER, 32 bits per pixel, uncompressed (BI_RGB), bottom-up, the alpha in the
fourth byte. The directory: a uint32 count, then 81-byte records of name[64], x, y, width, height
(uint32 each) and an alpha byte."""
import os
import struct
import sys

prefix = sys.argv[1]
size = 32
icon = "i_button_bmp.tga"

os.makedirs(os.path.dirname(prefix), exist_ok=True)

rows = []
for y in range(size - 1, -1, -1):  # bottom-up: the last image row is written first
    row = bytearray()
    for x in range(size):
        edge = x < 2 or y < 2 or x >= size - 2 or y >= size - 2
        b, g, r, a = (255, 255, 255, 255) if edge else (40, 40, 220, 200)
        row += bytes((b, g, r, a))
    rows.append(bytes(row))
pixels = b"".join(rows)

info = struct.pack("<IiiHHIIiiII", 40, size, size, 1, 32, 0, len(pixels), 2835, 2835, 0, 0)
header = struct.pack("<2sIHHI", b"BM", 14 + len(info) + len(pixels), 0, 0, 14 + len(info))
with open(prefix + ".tga", "wb") as f:
    f.write(header + info + pixels)

record = icon.encode("ascii").ljust(64, b"\0") + struct.pack("<IIII", 0, 0, size, size) + b"\x01"
with open(prefix + ".mtd", "wb") as f:
    f.write(struct.pack("<I", 1) + record)

print(f"wrote {prefix}.tga (bitmap) and {prefix}.mtd ({icon})")
