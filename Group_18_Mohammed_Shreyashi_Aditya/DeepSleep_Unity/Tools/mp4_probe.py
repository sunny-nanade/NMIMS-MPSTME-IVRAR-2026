#!/usr/bin/env python3
"""Minimal MP4 probe (no ffmpeg): tracks, codec, resolution, duration, audio sample-size statistics (silent AAC frames are tiny)."""
import struct, sys, statistics

def boxes(f, start, end):
    pos = start
    while pos + 8 <= end:
        f.seek(pos)
        size, typ = struct.unpack(">I4s", f.read(8))
        hdr = 8
        if size == 1:
            size = struct.unpack(">Q", f.read(8))[0]; hdr = 16
        elif size == 0:
            size = end - pos
        yield typ.decode("latin1"), pos + hdr, pos + size
        pos += size

def find(f, start, end, path):
    for t, s, e in boxes(f, start, end):
        if t == path[0]:
            if len(path) == 1: yield t, s, e
            else: yield from find(f, s, e, path[1:])

def main(p):
    f = open(p, "rb")
    f.seek(0, 2); size = f.tell()
    out = {"file": p, "bytes": size}
    for _, ms, me in find(f, 0, size, ["moov"]):
        for _, s, e in find(f, ms, me, ["mvhd"]):
            f.seek(s); ver = f.read(1)[0]; f.read(3)
            if ver == 1: f.read(16); ts, dur = struct.unpack(">IQ", f.read(12))
            else: f.read(8); ts, dur = struct.unpack(">II", f.read(8))
            out["duration_s"] = dur / ts
        tracks = []
        for _, ts_, te in find(f, ms, me, ["trak"]):
            t = {}
            for _, s, e in find(f, ts_, te, ["mdia", "hdlr"]):
                f.seek(s + 8); t["type"] = f.read(4).decode("latin1")
            for _, s, e in find(f, ts_, te, ["tkhd"]):
                f.seek(s); ver = f.read(1)[0]
                f.seek(e - 8); w, h = struct.unpack(">II", f.read(8)); t["w"], t["h"] = w >> 16, h >> 16
            for _, s, e in find(f, ts_, te, ["mdia", "mdhd"]):
                f.seek(s); ver = f.read(1)[0]; f.read(3)
                if ver == 1: f.read(16); tsc, dur = struct.unpack(">IQ", f.read(12))
                else: f.read(8); tsc, dur = struct.unpack(">II", f.read(8))
                t["duration_s"] = dur / tsc; t["timescale"] = tsc
            for _, s, e in find(f, ts_, te, ["mdia", "minf", "stbl", "stsd"]):
                f.seek(s + 8 + 4); t["codec"] = f.read(4).decode("latin1")
            for _, s, e in find(f, ts_, te, ["mdia", "minf", "stbl", "stsz"]):
                f.seek(s + 4); fixed, n = struct.unpack(">II", f.read(8))
                sizes = [fixed] * n if fixed else list(struct.unpack(">%dI" % n, f.read(4 * n)))
                t["samples"] = n
                t["sample_bytes_mean"] = round(statistics.mean(sizes), 1)
                t["sample_bytes_median"] = statistics.median(sizes)
                t["sample_bytes_max"] = max(sizes)
                if n > 1: t["frac_samples_over_100_bytes"] = round(sum(1 for x in sizes if x > 100) / n, 3)
            for _, s, e in find(f, ts_, te, ["mdia", "minf", "stbl", "stts"]):
                f.seek(s + 4); cnt = struct.unpack(">I", f.read(4))[0]
                tot = 0; dsum = 0
                for _i in range(cnt):
                    c, d = struct.unpack(">II", f.read(8)); tot += c; dsum += c * d
                t["fps_or_rate"] = round(tot / (dsum / t["timescale"]), 3) if dsum else 0
            tracks.append(t)
        out["tracks"] = tracks
    for k, v in out.items(): print(k, v)

main(sys.argv[1])
