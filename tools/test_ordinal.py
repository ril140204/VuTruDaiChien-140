#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Kiem chung gia thuyet "ordinal mapping": obfuscator dat ten obfuscate theo
thu tu co dinh (token order) => vi tri (index) cua type trong danh sach
obfuscated giu nguyen giua 2 build.

Kiem chung bang cac cap da biet dung:
    DJLADAKKHMP -> BBOIDGFADKJ
    IPDLCILIFHD -> APFNHMGMNKL
"""
import io
import json
import re
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

OBF_RE = re.compile(r'^[A-Z]{2}[A-Z0-9]{8,14}$')
CLASS_RE = re.compile(r'^public (?:static |abstract |sealed |partial )*class (\w+)')

V132 = Path(r"D:\game\VuTruDaiChien_140\tools\Il2CppDumper\dump.cs")
N140 = Path(r"D:\game\VuTruDaiChien_140\tools\dump\dump.cs")

KNOWN = {
    'DJLADAKKHMP': 'BBOIDGFADKJ',
    'IPDLCILIFHD': 'APFNHMGMNKL',
}


def obf_list(path):
    """Danh sach ten obfuscated theo thu tu xuat hien trong dump.cs (token order)."""
    out = []
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            m = CLASS_RE.match(line.rstrip('\n'))
            if m and OBF_RE.match(m.group(1)):
                out.append(m.group(1))
    return out


def main():
    a = obf_list(V132)
    b = obf_list(N140)
    print(f"ver132 obf classes: {len(a)}")
    print(f"140    obf classes: {len(b)}")

    ia = {n: i for i, n in enumerate(a)}
    ib = {n: i for i, n in enumerate(b)}

    print("\n=== KIEM CHUNG CAP DA BIET ===")
    ok = 0
    for old, new in KNOWN.items():
        if old not in ia:
            print(f"  {old}: NOT in ver132 obf list")
            continue
        if new not in ib:
            print(f"  {new}: NOT in 140 obf list")
            continue
        delta = ib[new] - ia[old]
        print(f"  {old} (idx {ia[old]}) -> {new} (idx {ib[new]})  delta={delta}")
        ok += 1

    # Thu mapping bang ordinal: lay danh sach type bi Client.dll reference
    # roi trai theo index tuong doi.
    client = Path(r"D:\game\ver132\Tool_Kep_12h\BepInEx\plugins\Client.dll").read_bytes()

    # Lay danh sach string 11 ky tu ascii trong file
    cand = set()
    for m in re.finditer(rb'(?<![A-Za-z0-9_])([A-Z]{2}[A-Z0-9]{8,14})(?![A-Za-z0-9_])', client):
        s = m.group(1).decode('ascii')
        if len(s) == 11:
            cand.add(s)
    print(f"\n11-char obfuscated-looking strings in Client.dll: {len(cand)}")

    # Chi giu nhung cai co that su la class trong ver132
    real = sorted([c for c in cand if c in ia], key=lambda x: ia[x])
    print(f"of which are classes in ver132: {len(real)}")
    for c in real:
        if c not in ia:
            continue

    # Mapping ordinal: giu thu tu tuong doi theo vi tri trong list
    mapping = {}
    unresolved = []
    for c in real:
        i = ia[c]
        # Dung offset tu 2 cap da biet de uoc luong offset chung
        # (neu offset khac nhau -> co the do obfuscator chen them class)
        pass

    # Cach 1: offset chung (gia su danh sach cung do dai va cung thu tu tuong doi)
    if len(a) == len(b):
        print("\nHypothesis A: same length -> direct offset")
        for c in real:
            mapping[c] = b[ia[c]]
    else:
        print(f"\nHypothesis A skipped: len differ ({len(a)} vs {len(b)})")
        # Cach 2: tuong doi theo "rank" cua class
        # dung he so: index_trong_obf_list / len
        for c in real:
            frac = ia[c] / len(a)
            target = frac * len(b)
            lo = int(target) - 200
            hi = int(target) + 200
            lo = max(0, lo)
            hi = min(len(b) - 1, hi)
            # chon candidate co "shape" giong nhat -> can them
            unresolved.append((c, lo, hi))

    # Kiem chung mapping voi cap da biet
    print("\n=== CHECK mapping voi cap da biet ===")
    for old, new in KNOWN.items():
        got = mapping.get(old)
        flag = 'MATCH' if got == new else 'MISMATCH'
        print(f"  {old}: expected {new}, got {got}  [{flag}]")

    Path(r"D:\game\VuTruDaiChien_140\tools\type_mapping_ordinal.json").write_text(
        json.dumps(mapping, indent=2), encoding='utf-8')
    print(f"\nSaved {len(mapping)} mapping -> type_mapping_ordinal.json")


if __name__ == '__main__':
    main()
