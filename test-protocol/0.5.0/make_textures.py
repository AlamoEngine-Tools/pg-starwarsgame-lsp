"""Writes a generated TGA texture for PRV-56 (#418): an uncompressed 32-bit checkerboard, so the preview
has a TGA to filter and mipmap that is no game file.

    python make_textures.py C:\\temp\\aet-test-PRV-filter\\Data\\Art\\Textures\\EB_Basewall.tga

The header: no image id, no colour map, image type 2 (uncompressed true colour), 256x256, 32 bits per
pixel, descriptor 8 (eight alpha bits, rows bottom-up). Pixels are BGRA."""
import os
import struct
import sys

path = sys.argv[1]
size = 256
cell = 16

os.makedirs(os.path.dirname(path), exist_ok=True)

header = struct.pack("<BBBHHBHHHHBB", 0, 0, 2, 0, 0, 0, 0, 0, size, size, 32, 8)
pixels = bytearray()
for y in range(size):
    for x in range(size):
        light = ((x // cell) + (y // cell)) % 2 == 0
        b, g, r = (230, 230, 230) if light else (40, 90, 200)
        pixels += bytes((b, g, r, 255))

with open(path, "wb") as f:
    f.write(header + bytes(pixels))

print(f"wrote {path} ({size}x{size} TGA)")
