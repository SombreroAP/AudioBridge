#!/usr/bin/env python3
"""Generates the AudioBridge application icon (assets/AudioBridge.ico).

Kept as a script rather than a checked-in binary blob so the icon can be tweaked and
regenerated. Renders at 4x and downsamples for anti-aliasing, then packs several sizes
into one .ico -- Windows picks 16px for the taskbar and 256px for large tiles, and an icon
that only looks right at one size looks wrong everywhere else.
"""
import struct, zlib, math, pathlib

SS = 4  # supersampling factor

# Green, light at the top-left to dark at the bottom-right.
TOP = (0x4A, 0xE0, 0x92)
BOTTOM = (0x12, 0x9E, 0x53)
BAR = (0xFF, 0xFF, 0xFF)

# Five bars, heights as a fraction of the icon. Symmetric, tallest in the middle:
# reads as an audio level meter even at 16 pixels.
BAR_HEIGHTS = [0.30, 0.56, 0.78, 0.56, 0.30]


def rounded_rect_contains(x, y, size, radius):
    if x < radius and y < radius:
        return math.hypot(x - radius, y - radius) <= radius
    if x > size - radius and y < radius:
        return math.hypot(x - (size - radius), y - radius) <= radius
    if x < radius and y > size - radius:
        return math.hypot(x - radius, y - (size - radius)) <= radius
    if x > size - radius and y > size - radius:
        return math.hypot(x - (size - radius), y - (size - radius)) <= radius
    return 0 <= x <= size and 0 <= y <= size


def render(size):
    big = size * SS
    radius = big * 0.22
    # Bar geometry, in supersampled units.
    count = len(BAR_HEIGHTS)
    bar_w = big * 0.088
    gap = big * 0.055
    total_w = count * bar_w + (count - 1) * gap
    left0 = (big - total_w) / 2
    bar_r = bar_w / 2

    rows = []
    for py in range(big):
        row = []
        for px in range(big):
            x, y = px + 0.5, py + 0.5
            if not rounded_rect_contains(x, y, big, radius):
                row.append((0, 0, 0, 0))
                continue

            t = (x / big + y / big) / 2
            bg = tuple(int(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3))

            pixel = bg + (255,)
            for index, height in enumerate(BAR_HEIGHTS):
                left = left0 + index * (bar_w + gap)
                if not (left <= x <= left + bar_w):
                    continue
                bar_h = big * height
                top = (big - bar_h) / 2
                bottom = top + bar_h
                cx = left + bar_r
                # Rounded caps: inside the straight section, or within the end circles.
                inside = (top + bar_r <= y <= bottom - bar_r) or \
                         math.hypot(x - cx, y - (top + bar_r)) <= bar_r or \
                         math.hypot(x - cx, y - (bottom - bar_r)) <= bar_r
                if inside:
                    pixel = BAR + (255,)
                break
            row.append(pixel)
        rows.append(row)

    # Box-downsample back to the target size.
    out = bytearray()
    for y in range(size):
        out.append(0)  # PNG filter type 0
        for x in range(size):
            r = g = b = a = 0
            for dy in range(SS):
                for dx in range(SS):
                    pr, pg, pb, pa = rows[y * SS + dy][x * SS + dx]
                    # Weight colour by alpha so transparent pixels don't darken the edges.
                    r += pr * pa; g += pg * pa; b += pb * pa; a += pa
            if a:
                out += bytes((r // a, g // a, b // a, a // (SS * SS)))
            else:
                out += b"\x00\x00\x00\x00"
    return bytes(out)


def png(size, raw):
    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)  # 8-bit RGBA
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def main():
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [png(s, render(s)) for s in sizes]

    # ICO: header, one directory entry per image, then the image data.
    out = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    for size, data in zip(sizes, images):
        out += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    out += b"".join(images)

    root = pathlib.Path(__file__).resolve().parent.parent
    (root / "assets" / "AudioBridge.ico").write_bytes(out)
    (root / "assets" / "AudioBridge-256.png").write_bytes(images[-1])
    print(f"assets/AudioBridge.ico  {len(out):,} bytes  sizes={sizes}")


if __name__ == "__main__":
    main()
