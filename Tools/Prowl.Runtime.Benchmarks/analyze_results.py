"""Analyze BenchmarkDotNet joined JSON: pair Linq vs Loop and summarize winners.

Usage: python analyze_results.py <report-full.json> [allocation-probe.csv]
Emits a compact per-(Type, Category, SourceKind, Count) comparison and
aggregate roll-ups used to build the LINQ allowlist.
"""
import csv
import json
import sys
from collections import defaultdict

path = sys.argv[1]
with open(path, "r", encoding="utf-8-sig") as f:
    data = json.load(f)

allocation_probe = {}
if len(sys.argv) > 2:
    with open(sys.argv[2], "r", encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            key = (row["Operation"], row["Implementation"], row["SourceKind"], int(row["Count"]))
            allocation_probe[key] = float(row["AllocatedBytesPerOperation"])
else:
    print("WARNING: no allocation probe supplied; BenchmarkDotNet .NET 10 Server GC byte values may be invalid.")


def parse_params(p):
    # "SourceKind=Array&Count=0"
    d = {}
    for part in p.split("&"):
        if "=" in part:
            k, v = part.split("=", 1)
            d[k] = v
    return d


rows = {}
for b in data["Benchmarks"]:
    t = b["Type"]
    method = b["Method"]  # Linq_All / Loop_All
    variant, category = method.split("_", 1)  # Linq / Loop, All
    params = parse_params(b["Parameters"])
    sk = params.get("SourceKind", "?")
    cnt = int(params.get("Count", -1))
    mean = b["Statistics"]["Mean"]
    alloc = b["Memory"]["BytesAllocatedPerOperation"]
    key = (t, category, sk, cnt)
    rows.setdefault(key, {})[variant] = (mean, alloc)

# Per-pair comparison
paired = []
for key, v in rows.items():
    if "Linq" not in v or "Loop" not in v:
        continue
    t, category, sk, cnt = key
    lmean, bdn_lalloc = v["Linq"]
    pmean, bdn_palloc = v["Loop"]
    lalloc = allocation_probe.get((category, "Linq", sk, cnt), bdn_lalloc)
    palloc = allocation_probe.get((category, "Loop", sk, cnt), bdn_palloc)
    ratio = lmean / pmean if pmean else float("inf")  # linq/loop time
    paired.append((t, category, sk, cnt, lmean, pmean, ratio, lalloc, palloc))

paired.sort(key=lambda r: (r[0], r[1], r[2], r[3]))

print("=== PER-PAIR (Linq vs Loop) ===")
print(f"{'Category':<26}{'Src':<9}{'Cnt':>5}  {'Linq ns':>12}{'Loop ns':>12}{'L/L':>7}  {'LinqAlloc':>10}{'LoopAlloc':>10}")
for t, cat, sk, cnt, lmean, pmean, ratio, lalloc, palloc in paired:
    flag = ""
    if ratio <= 1.05:
        flag = "LINQ~=/win"
    elif ratio <= 1.15:
        flag = "close"
    else:
        flag = "LOOP win"
    if lalloc > palloc:
        flag += " +alloc"
    print(f"{cat:<26}{sk:<9}{cnt:>5}  {lmean:>12.2f}{pmean:>12.2f}{ratio:>7.2f}  {lalloc:>10}{palloc:>10}  {flag}")

# Aggregate per (Type, Category): geomean-ish and alloc behavior
print("\n=== AGGREGATE PER CATEGORY (across all sources/sizes) ===")
agg = defaultdict(list)
for t, cat, sk, cnt, lmean, pmean, ratio, lalloc, palloc in paired:
    agg[(t, cat)].append((ratio, lalloc, palloc, sk, cnt))

def geomean(xs):
    prod = 1.0
    for x in xs:
        prod *= x
    return prod ** (1.0 / len(xs))

summary = []
for (t, cat), items in sorted(agg.items()):
    ratios = [i[0] for i in items]
    gm = geomean(ratios)
    worst = max(ratios)
    best = min(ratios)
    linq_allocs = any(i[1] > 0 for i in items)
    loop_allocs = any(i[2] > 0 for i in items)
    alloc_regress = any(i[1] > i[2] for i in items)
    # Where does loop clearly win (ratio>1.15)?
    loop_win_cases = [f"{i[3]}/{i[4]}" for i in items if i[0] > 1.15]
    summary.append((t, cat, gm, best, worst, linq_allocs, alloc_regress, loop_win_cases))
    print(f"{t}.{cat}: geomean L/L={gm:.2f} range[{best:.2f},{worst:.2f}] "
          f"linqAlloc={'Y' if linq_allocs else 'N'} allocRegress={'Y' if alloc_regress else 'N'} "
          f"loopWins={loop_win_cases}")

# Allowlist recommendation
print("\n=== COARSE CATEGORY SIGNAL (final allowlist must remain source/operator-specific) ===")
for t, cat, gm, best, worst, linq_allocs, alloc_regress, loop_win_cases in summary:
    # Allow LINQ if geomean <=1.10 and no alloc regression and worst case not terrible
    if gm <= 1.10 and not alloc_regress and worst <= 1.25:
        verdict = "ALLOW LINQ"
    elif gm <= 1.10 and not alloc_regress:
        verdict = "ALLOW LINQ (non-hot only; worst-case slow on some source)"
    else:
        verdict = "PREFER LOOP"
    print(f"{t}.{cat}: {verdict}  (geomean {gm:.2f}, worst {worst:.2f}, allocRegress={'Y' if alloc_regress else 'N'})")
