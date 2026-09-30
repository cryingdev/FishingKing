"""
Checks that every character used in the game's C# string literals exists in the UI fonts.

Usage:  python Tools/font_coverage.py <font.ttf> [<font.ttf> ...]
Pure Python (reads the TrueType 'cmap' table directly, formats 4 and 12).
"""
import os
import re
import struct
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))


def cmap_codepoints(path):
    data = open(path, "rb").read()
    num_tables = struct.unpack(">H", data[4:6])[0]
    cmap_off = None
    for i in range(num_tables):
        tag, _, off, _ = struct.unpack(">4sIII", data[12 + i * 16: 28 + i * 16])
        if tag == b"cmap":
            cmap_off = off
    if cmap_off is None:
        raise ValueError("no cmap in " + path)
    n = struct.unpack(">H", data[cmap_off + 2: cmap_off + 4])[0]
    cps = set()
    for i in range(n):
        pid, eid, off = struct.unpack(">HHI", data[cmap_off + 4 + i * 8: cmap_off + 12 + i * 8])
        sub = cmap_off + off
        fmt = struct.unpack(">H", data[sub: sub + 2])[0]
        if fmt == 4:
            seg2 = struct.unpack(">H", data[sub + 6: sub + 8])[0]
            segs = seg2 // 2
            ends = struct.unpack(">%dH" % segs, data[sub + 14: sub + 14 + seg2])
            starts = struct.unpack(">%dH" % segs, data[sub + 16 + seg2: sub + 16 + 2 * seg2])
            deltas = struct.unpack(">%dh" % segs, data[sub + 16 + 2 * seg2: sub + 16 + 3 * seg2])
            ro_pos = sub + 16 + 3 * seg2
            ranges = struct.unpack(">%dH" % segs, data[ro_pos: ro_pos + seg2])
            for s, e, d, r, k in zip(starts, ends, deltas, ranges, range(segs)):
                for c in range(s, e + 1):
                    if c == 0xFFFF:
                        continue
                    if r == 0:
                        g = (c + d) & 0xFFFF
                    else:
                        gi = ro_pos + k * 2 + r + (c - s) * 2
                        g = struct.unpack(">H", data[gi: gi + 2])[0]
                        if g:
                            g = (g + d) & 0xFFFF
                    if g:
                        cps.add(c)
        elif fmt == 12:
            ngroups = struct.unpack(">I", data[sub + 12: sub + 16])[0]
            for gi in range(ngroups):
                s, e, g = struct.unpack(">III", data[sub + 16 + gi * 12: sub + 28 + gi * 12])
                cps.update(range(s, e + 1))
    return cps


def used_characters():
    chars = {}
    lit = re.compile(r'\$?@?"((?:[^"\\\n]|\\.)*)"')
    for base, _, files in os.walk(os.path.join(ROOT, "Assets", "Scripts")):
        for fn in files:
            if not fn.endswith(".cs"):
                continue
            path = os.path.join(base, fn)
            for ln, line in enumerate(open(path, encoding="utf-8"), 1):
                if line.strip().startswith("//"):
                    continue
                for m in lit.finditer(line):
                    s = re.sub(r"\{[^}]*\}", "", m.group(1))       # drop interpolation holes
                    s = re.sub(r"</?(b|i|size|color)[^>]*>", "", s)  # drop rich-text tags
                    s = s.replace("\\n", "")
                    for ch in s:
                        if ch == " ":
                            continue
                        chars.setdefault(ch, f"{os.path.relpath(path, ROOT)}:{ln}")
    return chars


def main():
    fonts = sys.argv[1:]
    chars = used_characters()
    print(f"{len(chars)} distinct characters used in string literals")
    ok = True
    for f in fonts:
        cps = cmap_codepoints(f)
        missing = {c: where for c, where in chars.items() if ord(c) not in cps}
        print(f"{os.path.basename(f)}: {len(cps)} glyphs, missing {len(missing)}")
        for c, where in sorted(missing.items()):
            ok = False
            print(f"   U+{ord(c):04X} {c!r}  first used at {where}")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
