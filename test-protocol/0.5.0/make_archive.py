"""Writes PRJ-17's archive: a MEG v1 holding one script and one texture of the test's own, so nothing
the game or the baseline ships can stand in for it.

    python make_archive.py <out.meg>
"""
import struct, sys, zlib

OUT = sys.argv[1]

ENTRIES = [
    ("DATA\\SCRIPTS\\GAMEOBJECT\\SCRATCHSWAP.LUA", b"-- PRJ-17: resolves only through the archive\n"),
    ("DATA\\ART\\TEXTURES\\SCRATCH_PACKED.TGA", bytes(18)),
]

# The file table is sorted by the CRC32 of the name, as the game looks entries up by it.
names = [n.encode("ascii") for n, _ in ENTRIES]
header = struct.pack("<II", len(ENTRIES), len(ENTRIES))
name_table = b"".join(struct.pack("<H", len(n)) + n for n in names)
data_start = len(header) + len(name_table) + 20 * len(ENTRIES)

records, offset = [], data_start
for index, (name, (_, body)) in enumerate(zip(names, ENTRIES)):
    records.append((zlib.crc32(name) & 0xFFFFFFFF, len(body), offset, index))
    offset += len(body)
records.sort()

file_table = b"".join(struct.pack("<IIIII", crc, position, size, start, name_index)
                      for position, (crc, size, start, name_index) in enumerate(records))

with open(OUT, "wb") as out:
    out.write(header + name_table + file_table + b"".join(body for _, body in ENTRIES))
