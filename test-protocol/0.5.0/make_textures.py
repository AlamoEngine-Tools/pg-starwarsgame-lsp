"""Writes generated textures for the preview tests, and renames the textures a copied model names, so no
game file is part of a fixture.

    python make_textures.py C:\\temp\\aet-test-PRV-filter\\Data\\Art\\Textures\\EB_Basewall.tga
    python make_textures.py tga <path> [size]
    python make_textures.py png <path> [size]
    python make_textures.py dds <path> [size]
    python make_textures.py rename <model> OLD=NEW [OLD=NEW ...]

A bare path writes the TGA PRV-56 (#418) filters and mipmaps. The TGA is uncompressed 32-bit true colour
(image type 2, descriptor 8: eight alpha bits, rows bottom-up), pixels BGRA. The PNG is 8-bit RGBA (colour
type 6), one filter-0 scanline after another, deflated. The DDS is uncompressed 32-bit A8R8G8B8 with no mip
chain. Each format draws its own colours, so a screenshot tells them apart.

`rename` rewrites texture names inside a model in place - for PRV-59 (#420), whose models are copies of a
game model naming textures the test needs under other names and formats. A name is stored with its length,
so OLD and NEW must be the same length; a name the model does not carry is an error, not a silent no-op."""
import os
import struct
import sys
import zlib

CELL = 16


def checker(size, light, dark):
    """Rows top-down, each pixel (r, g, b, a)."""
    return [
        [light if ((x // CELL) + (y // CELL)) % 2 == 0 else dark for x in range(size)]
        for y in range(size)
    ]


def write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)


def tga(size):
    rows = checker(size, (230, 230, 230, 255), (200, 90, 40, 255))
    header = struct.pack("<BBBHHBHHHHBB", 0, 0, 2, 0, 0, 0, 0, 0, size, size, 32, 8)
    pixels = bytearray()
    # Written in the order PRV-56's first version wrote them, so its texture stays byte for byte the same.
    for row in rows:
        for r, g, b, a in row:
            pixels += bytes((b, g, r, a))
    return header + bytes(pixels)


def png(size):
    rows = checker(size, (240, 200, 40, 255), (40, 160, 70, 255))

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    raw = bytearray()
    for row in rows:
        raw.append(0)
        for pixel in row:
            raw += bytes(pixel)
    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def dds(size):
    rows = checker(size, (220, 220, 250, 255), (90, 40, 160, 255))
    # DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PITCH | DDSD_PIXELFORMAT
    flags = 0x1 | 0x2 | 0x4 | 0x8 | 0x1000
    # DDPF_ALPHAPIXELS | DDPF_RGB, 32 bits, masks for A8R8G8B8.
    pixel_format = struct.pack("<IIIIIIII", 32, 0x41, 0, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000)
    header = (struct.pack("<IIIIIII", 124, flags, size, size, size * 4, 0, 0) + b"\0" * 44 + pixel_format
              + struct.pack("<IIIII", 0x1000, 0, 0, 0, 0))
    pixels = bytearray()
    for row in rows:
        for r, g, b, a in row:
            pixels += bytes((b, g, r, a))
    return b"DDS " + header + bytes(pixels)


def rename(model, pairs):
    with open(model, "rb") as f:
        data = f.read()
    for pair in pairs:
        old, new = pair.split("=", 1)
        if len(old) != len(new):
            sys.exit(f"rename: '{old}' and '{new}' differ in length")
        count = data.count(old.encode("ascii"))
        if count == 0:
            sys.exit(f"rename: {model} names no '{old}'")
        data = data.replace(old.encode("ascii"), new.encode("ascii"))
        print(f"renamed {old} -> {new} in {model} ({count}x)")
    with open(model, "wb") as f:
        f.write(data)


WRITERS = {"tga": tga, "png": png, "dds": dds}

if __name__ == "__main__":
    args = sys.argv[1:]
    if args and args[0] == "rename":
        rename(args[1], args[2:])
    elif args and args[0] in WRITERS:
        size = int(args[2]) if len(args) > 2 else 128
        write(args[1], WRITERS[args[0]](size))
        print(f"wrote {args[1]} ({size}x{size} {args[0].upper()})")
    else:
        write(args[0], tga(256))
        print(f"wrote {args[0]} (256x256 TGA)")
