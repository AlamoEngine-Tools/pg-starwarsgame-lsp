import struct, sys

SRC = sys.argv[1]
OUT = sys.argv[2]

def chunk(t, body, container):
    raw = len(body) | (0x80000000 if container else 0)
    return struct.pack("<II", t, raw) + body

def children(b, s, e):
    out, p = [], s
    while p < e:
        t, raw = struct.unpack_from("<II", b, p)
        ln = raw & 0x7FFFFFFF
        out.append((t, raw & 0x80000000 != 0, b[p + 8:p + 8 + ln]))
        p += 8 + ln
    return out

def build(src, emitter_extra, list_extra):
    b = open(src, "rb").read()
    (rt, rc, rbody), = children(b, 0, len(b))
    parts = []
    for t, c, body in children(rbody, 0, len(rbody)):
        if t == 0x800:
            ems = children(body, 0, len(body))
            new_list = b""
            for i, (et, ec, ebody) in enumerate(ems):
                if i == 0:
                    ebody = ebody + emitter_extra
                new_list += chunk(et, ebody, ec)
            new_list += list_extra
            parts.append(chunk(t, new_list, c))
        else:
            parts.append(chunk(t, body, c))
    return chunk(rt, b"".join(parts), rc)

link_group = chunk(0x100, struct.pack("<I", 3), False)
unknown = chunk(0x7F, struct.pack("<I", 0), False)
open(OUT + "_linked.alo", "wb").write(build(SRC, link_group + unknown, b""))
open(OUT + "_broken.alo", "wb").write(build(SRC, b"", unknown))
print("written", OUT + "_linked.alo", OUT + "_broken.alo")
