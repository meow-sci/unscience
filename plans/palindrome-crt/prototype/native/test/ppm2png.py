#!/usr/bin/env python3
"""Minimal PPM(P6)/PGM(P5) -> PNG converter (stdlib only) so frames can be eyeballed."""
import struct, sys, zlib
def main(src, dst):
    data = open(src, "rb").read()
    parts = data.split(b"\n", 3)
    magic, (w, h), body = parts[0], map(int, parts[1].split()), parts[3]
    ch = 3 if magic == b"P6" else 1
    raw = b"".join(b"\x00" + body[y * w * ch:(y + 1) * w * ch] for y in range(h))
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2 if ch == 3 else 0, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b"")
    open(dst, "wb").write(png)
main(sys.argv[1], sys.argv[2])
