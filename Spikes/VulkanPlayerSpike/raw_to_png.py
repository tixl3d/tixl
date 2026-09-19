#!/usr/bin/env python3
"""
Converts a capture written by `--capture` into a PNG, using only the standard library.

Usage: raw_to_png.py capture.raw [capture.png]
"""
import pathlib
import struct
import sys
import zlib


def main():
    source = pathlib.Path(sys.argv[1])
    target = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else source.with_suffix(".png")

    data = source.read_bytes()
    newline = data.index(b"\n")
    channel_order, width, height = data[:newline].decode().split()
    width, height = int(width), int(height)
    pixels = data[newline + 1:]

    rows = bytearray()
    stride = width * 4
    for y in range(height):
        row = bytearray(pixels[y * stride:(y + 1) * stride])
        if channel_order == "BGRA":
            row[0::4], row[2::4] = row[2::4], row[0::4]
        rows += b"\x00" + row  # filter type 0 per row

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(bytes(rows), 6))
           + chunk(b"IEND", b""))
    target.write_bytes(png)
    print(f"{target} ({width}x{height}, from {channel_order})")


if __name__ == "__main__":
    main()
