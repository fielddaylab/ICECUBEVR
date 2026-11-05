#!/usr/bin/env python3
"""N17 before/after comparison: frame-time delta + screenshot diffs.

  python3 tools/n17_diff.py /tmp/n17/before /tmp/n17/after [--md tools/n17_results.md]

For every phase present in both runs prints p50/p95 before -> after and the
screenshot difference (mean |delta| per channel 0-255, fraction of pixels
that changed by > 16/255, mean luma before/after). A phase whose mean luma
collapses toward 0 while the baseline is lit is flagged BLACK - that is the
"no object turns black" acceptance check.
"""
import argparse, json, os, sys
from PIL import Image, ImageChops, ImageStat

def load(d):
    with open(os.path.join(d, "frames.json")) as f:
        return json.load(f)

def shot_diff(a, b):
    ia = Image.open(a).convert("RGB"); ib = Image.open(b).convert("RGB")
    if ia.size != ib.size:
        ib = ib.resize(ia.size)
    d = ImageChops.difference(ia, ib)
    st = ImageStat.Stat(d)
    mean = sum(st.mean) / 3.0
    gray = d.convert("L")
    hist = gray.histogram()
    changed = sum(hist[17:]) / float(ia.size[0] * ia.size[1])
    la = ImageStat.Stat(ia.convert("L")).mean[0]
    lb = ImageStat.Stat(ib.convert("L")).mean[0]
    return mean, changed, la, lb

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("before"); ap.add_argument("after")
    ap.add_argument("--md", help="also write a markdown table here")
    args = ap.parse_args()
    B = load(args.before); A = load(args.after)
    bp = {p["label"]: p for p in B["phases"]}
    apx = {p["label"]: p for p in A["phases"]}

    rows = []
    for label in [p["label"] for p in B["phases"] if p["label"] in apx]:
        b, a = bp[label], apx[label]
        row = {"phase": label,
               "p50_before": b.get("p50_ms"), "p50_after": a.get("p50_ms"),
               "p95_before": b.get("p95_ms"), "p95_after": a.get("p95_ms")}
        if b.get("shot") and a.get("shot"):
            mean, changed, la, lb = shot_diff(os.path.join(args.before, b["shot"]),
                                              os.path.join(args.after, a["shot"]))
            row.update(diff_mean=mean, diff_changed=changed, luma_before=la, luma_after=lb,
                       black=(la > 20 and lb < 5))
        rows.append(row)

    def fmt(v, f="%.2f"):
        return f % v if isinstance(v, (int, float)) else "-"

    hdr = "| phase | p50 before | p50 after | dp50 | p95 before | p95 after | shot mean diff | px changed | luma before/after | flag |"
    sep = "|---|---|---|---|---|---|---|---|---|---|"
    lines = [hdr, sep]
    for r in rows:
        dp = (r["p50_before"] - r["p50_after"]) if r["p50_before"] is not None and r["p50_after"] is not None else None
        flag = "BLACK" if r.get("black") else ""
        lines.append("| %s | %s | %s | %s | %s | %s | %s | %s | %s/%s | %s |" % (
            r["phase"], fmt(r["p50_before"]), fmt(r["p50_after"]), fmt(dp, "%+.2f"),
            fmt(r["p95_before"]), fmt(r["p95_after"]),
            fmt(r.get("diff_mean")), fmt(r.get("diff_changed"), "%.3f"),
            fmt(r.get("luma_before"), "%.1f"), fmt(r.get("luma_after"), "%.1f"), flag))
    p50s = [(r["p50_before"], r["p50_after"]) for r in rows
            if r["p50_before"] is not None and r["p50_after"] is not None]
    if p50s:
        gain = sum(b - a for b, a in p50s) / len(p50s)
        lines.append("")
        lines.append("Mean p50 gain across phases: %+.2f ms (%s)" % (
            gain, "meets >= 1 ms kill criterion" if gain >= 1.0 else "BELOW 1 ms kill criterion"))
    out = "\n".join(lines)
    print(out)
    if args.md:
        with open(args.md, "w") as f:
            f.write("# N17 before/after (headless capture)\n\n")
            f.write("before: `%s` (%s)\n\nafter: `%s` (%s)\n\n" % (
                B.get("url"), B.get("utc"), A.get("url"), A.get("utc")))
            f.write(out + "\n")
            if any(r.get("black") for r in rows):
                f.write("\n**WARNING: a phase went black after the swap.**\n")
    return 0

if __name__ == "__main__":
    sys.exit(main())
