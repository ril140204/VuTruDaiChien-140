#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Xay mapping ten type obfuscate (ver132) -> ten tuong duong (ban 140).

Tieu chi:
  - Chuoi ky: tap kieu field (chu hoa -> X) + tap (kieu tra ve, so tham so) cua method
  - Khop chinh xac (exact) = mapping chắc chắn
  - Neu khong exact, dung so field + so method + kieu base de xep hang
"""
import io
import json
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

import importlib.util
spec = importlib.util.spec_from_file_location("mt", "map_types.py")
mt = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mt)

CLIENT_DLL = r"D:\game\ver132\Tool_Kep_12h\BepInEx\plugins\Client.dll"

# -------------------------------------------------------------------
# 1. Thu thap tat ca ten obfuscate ma Client.dll tham chieu
#    (TypeRef + string literal)
# -------------------------------------------------------------------
data = open(CLIENT_DLL, 'rb').read()

names_in_raw = set()
for chunk in data.split(b'\x00'):
    try:
        s = chunk.decode('ascii')
    except UnicodeDecodeError:
        continue
    if 2 <= len(s) <= 64:
        names_in_raw.add(s)

OBF_RE = re.compile(r'(?<![A-Za-z0-9])[A-Z]{2}[A-Z0-9]{8,14}(?![A-Za-z0-9])')
obf_names = sorted({m for s in names_in_raw for m in OBF_RE.findall(s)})

v = mt.parse_dump('Il2CppDumper/dump.cs')
n = mt.parse_dump('dump/dump.cs')

print(f'Client.dll obfuscated refs: {len(obf_names)}')
print(f'ver132 classes: {len(v)}   140 classes: {len(n)}')


# -------------------------------------------------------------------
# 2. Signature manh hon
# -------------------------------------------------------------------
def base_shape(base):
    """Kieu base: giu ten gon (khong obfuscate) -> 'X' neu obfuscate."""
    b = base.split('<')[0].strip()
    return mt.norm_type(b)


def sig2(cls):
    fs = tuple(sorted(mt.norm_type(t) for t, _ in cls['fields']))
    ms = tuple(sorted((mt.norm_type(r), n) for r, _, n in cls['methods']))
    return (base_shape(cls['base']), len(cls['fields']), len(cls['methods']), fs, ms)


def loose_sig(cls):
    """Chi giu so field/method + kieu field (khong ky method)."""
    fs = tuple(sorted(mt.norm_type(t) for t, _ in cls['fields']))
    return (len(cls['fields']), fs)


idx_exact = {}
for name, cls in n.items():
    idx_exact.setdefault(sig2(cls), []).append(name)

idx_loose = {}
for name, cls in n.items():
    idx_loose.setdefault(loose_sig(cls), []).append(name)

idx_shape = {}
for name, cls in n.items():
    idx_shape.setdefault((len(cls['fields']), len(cls['methods'])), []).append(name)

mapping = {}
report = {'exact': [], 'loose': [], 'shape': [], 'unresolved': []}

for obf in obf_names:
    if obf not in v:
        # co the la ten field/method, khong phai class
        report['unresolved'].append((obf, 'not-a-class-in-ver132'))
        continue

    src = v[obf]

    # --- 1) exact signature ---
    exact = idx_exact.get(sig2(src), [])
    if len(exact) == 1:
        mapping[obf] = exact[0]
        report['exact'].append((obf, exact[0]))
        continue
    if len(exact) > 1:
        # phan biich bang field-name? khong giong -> dung base + count
        report['unresolved'].append((obf, f'ambiguous-exact({len(exact)})'))
        continue

    # --- 2) loose: cung so field + cung tap kieu field ---
    loose = idx_loose.get(loose_sig(src), [])
    if len(loose) == 1:
        mapping[obf] = loose[0]
        report['loose'].append((obf, loose[0]))
        continue
    if len(loose) > 1:
        # loc theo base shape + so method
        cand = [c for c in loose
                if base_shape(n[c]['base']) == base_shape(src['base'])
                and len(n[c]['methods']) == len(src['methods'])]
        if len(cand) == 1:
            mapping[obf] = cand[0]
            report['loose'].append((obf, cand[0]))
            continue
        report['unresolved'].append((obf, f'ambiguous-loose({len(loose)})'))
        continue

    # --- 3) shape only: cung so field + cung so method ---
    shape = idx_shape.get((len(src['fields']), len(src['methods'])), [])
    if len(shape) == 1:
        mapping[obf] = shape[0]
        report['shape'].append((obf, shape[0]))
        continue
    if len(shape) > 1:
        # so sanh overlap kieu field
        sf = set(mt.norm_type(t) for t, _ in src['fields'])
        scored = []
        for c in shape:
            cf = set(mt.norm_type(t) for t, _ in n[c]['fields'])
            scored.append((len(sf & cf), c))
        scored.sort(reverse=True)
        best_score = scored[0][0]
        winners = [c for s, c in scored if s == best_score]
        if len(winners) == 1 and best_score >= max(1, len(sf) - 1):
            mapping[obf] = winners[0]
            report['shape'].append((obf, winners[0]))
            continue
        report['unresolved'].append((obf, f'ambiguous-shape({len(shape)})'))
        continue

    report['unresolved'].append((obf, 'no-candidate'))

print()
for kind in ('exact', 'loose', 'shape'):
    print(f'=== {kind.upper()}: {len(report[kind])} ===')
    for a, b in report[kind]:
        print(f'  {a} -> {b}')

print()
print(f'=== UNRESOLVED: {len(report["unresolved"])} ===')
for a, why in report['unresolved']:
    print(f'  {a}: {why}')

with open('type_mapping.json', 'w', encoding='utf-8') as f:
    json.dump(mapping, f, indent=2, ensure_ascii=False)
print(f'\nTotal mapped: {len(mapping)} -> type_mapping.json')
