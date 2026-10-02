#!/usr/bin/env python3
"""Parse Serilog compact-JSON logs: slowest routes, error clusters, tenants driving load, per-correlation-id trace.
Usage: python log_analyzer.py app.log [--cid <correlationId>]"""
import json, sys, collections, statistics

def load(path):
    for line in open(path, encoding="utf-8"):
        try: yield json.loads(line)
        except json.JSONDecodeError: continue

def main(path, cid=None):
    events = list(load(path))
    if cid:
        for e in sorted((e for e in events if e.get("correlationId") == cid), key=lambda e: e.get("@t", "")):
            print(e.get("@t"), e.get("@l", "Info"), e.get("@mt") or e.get("@m"))
        return
    slow = collections.defaultdict(list)
    errors = collections.Counter()
    for e in events:
        if e.get("@mt", "").startswith("SLOW_REQUEST"): slow[e.get("Route", "?")].append(e.get("Ms", 0))
        if e.get("@l") in ("Error", "Fatal"): errors[(e.get("@mt", "?")[:80], (e.get("@x") or "").split("\n")[0][:80])] += 1
    print("== Slow routes (count, p95 ms) ==")
    for r, v in sorted(slow.items(), key=lambda kv: -len(kv[1]))[:10]:
        v.sort(); print(f"{r:60} {len(v):5} {v[int(len(v) * .95) - 1] if len(v) > 1 else v[0]}")
    print("\n== Top error clusters ==")
    for (m, x), n in errors.most_common(10): print(f"{n:5}  {m} | {x}")

if __name__ == "__main__":
    if len(sys.argv) < 2: sys.exit(__doc__)
    main(sys.argv[1], sys.argv[3] if len(sys.argv) > 3 and sys.argv[2] == "--cid" else None)
