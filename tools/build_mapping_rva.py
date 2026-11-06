#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Mapping type obfuscate (ver132) -> type tuong duong (ban 140) dung RVA
(Runtime Virtual Address) lam khoa doi chieu.

Y tuong:
  Il2CppDumper xuat dump.cs voi moi method co "RVA: 0x.... Offset: 0x....".
  RVA la dia chi native trong GameAssembly.dll -> KHONG doi khi obfuscate
  doi ten (chi doi ten + xoa thu tu field).
  => 2 method trung RVA trong 2 ban = cung 1 ham.

Buoc:
  1. Parse dump.cs ca 2 ban -> {class: {method_rva: (name, ret, nargs)}}
  2. Với mỗi class ver132, tạo "rva-fingerprint" = frozenset(RVA cua method)
  3. Tìm class 140 có cùng / gần trùng fingerprint
"""
import io
import json
import re
import sys
from collections import defaultdict

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

RVA_RE = re.compile(r'//\s*RVA:\s*(0x[0-9A-Fa-f]+)')
CLASS_RE = re.compile(
    r'^public (?:static |abstract |sealed |partial )*class (\w+)'
    r'(?:\s*:\s*([^{]+?))?\s*(?://.*)?$'
)
METHOD_RE = re.compile(r'^\s*(?:public|private|protected|internal)?\s*'
                       r'(?:static\s+)?(?:unsafe\s+)?(?:virtual\s+)?(?:override\s+)?'
                       r'(?:abstract\s+)?([\w\.<>\[\],\s]+?)\s+(\w+)\s*\((.*?)\)\s*\{')
SKIP = {'const', 'static', 'readonly', 'volatile', 'unsafe', 'extern'}


def parse(path):
    """-> {class: {'rvas': set, 'methods': [(rva, ret, nargs)], 'fields': [(type,name)],
                  'base': str, 'nfields': int}}"""
    classes = {}
    cur = None
    depth = 0
    pending_rva = None
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            m = CLASS_RE.match(line.rstrip('\n'))
            if m and depth == 0:
                cur = m.group(1)
                classes[cur] = {'rvas': set(), 'methods': [], 'fields': [],
                                'base': (m.group(2) or '').strip()}
                pending_rva = None
                continue
            if cur is None:
                continue

            s = line.strip()
            if s.startswith('//'):
                rm = RVA_RE.search(s)
                if rm:
                    pending_rva = int(rm.group(1), 16)
                continue

            mm = METHOD_RE.match(line.rstrip('\n'))
            if mm:
                ret = ' '.join(t for t in mm.group(1).split() if t not in SKIP)
                args = mm.group(3).strip()
                nargs = 0 if not args else len([a for a in args.split(',') if a.strip()])
                classes[cur]['methods'].append((pending_rva, ret, nargs))
                if pending_rva:
                    classes[cur]['rvas'].add(pending_rva)
                pending_rva = None
                continue

            # field?
            fm = re.match(r'^\s*(?:public|private|protected|internal)?\s*'
                          r'(?:static\s+)?(?:readonly\s+)?(?:const\s+)?(?:volatile\s+)?'
                          r'([\w\.<>\[\],\s]+?)\s+(\w+)\s*(?:=[^;]*)?;\s*(?://.*)?$',
                          line.rstrip('\n'))
            if fm and 'class ' not in s and 'struct ' not in s and 'enum ' not in s:
                typ = ' '.join(t for t in fm.group(1).split() if t not in SKIP)
                classes[cur]['fields'].append((typ, fm.group(2)))
                continue

            depth += line.count('{') - line.count('}')
            if depth <= 0:
                cur = None
                depth = 0
    return classes


def main():
    v = parse('Il2CppDumper/dump.cs')
    n = parse('dump/dump.cs')

    print(f'ver132 classes: {len(v)}   140 classes: {len(n)}')

    # index 140 theo tap RVA
    idx_rva = defaultdict(set)
    for name, cls in n.items():
        if cls['rvas']:
            idx_rva[frozenset(cls['rvas'])].add(name)

    # index theo RVA don le -> class
    idx_single = defaultdict(set)
    for name, cls in n.items():
        for rva in cls['rvas']:
            idx_single[rva].add(name)

    # xuat tat ca type obfuscate ma Client.dll can
    import re as _re
    data = open(r"D:\game\ver132\Tool_Kep_12h\BepInEx\plugins\Client.dll", 'rb').read()
    strings = set()
    for chunk in data.split(b'\x00'):
        try:
            s = chunk.decode('ascii')
        except UnicodeDecodeError:
            continue
        if 2 <= len(s) <= 64:
            strings.add(s)
    OBF = _re.compile(r'(?<![A-Za-z0-9])[A-Z]{2}[A-Z0-9]{8,14}(?![A-Za-z0-9])')
    refs = sorted({m for s in strings for m in OBF.findall(s)})

    mapping = {}
    detail = []
    for obf in refs:
        if obf not in v:
            continue
        src = v[obf]
        if not src['rvas']:
            continue

        # 1) tap RVA giong het
        cands = idx_rva.get(frozenset(src['rvas']), [])
        if len(cands) == 1:
            mapping[obf] = cands[0]
            detail.append((obf, cands[0], 'exact-rva', len(src['rvas'])))
            continue
        if len(cands) > 1:
            # phan biech bang so field + so method
            scored = sorted(((len(cands and n[c]['fields']) * 100 + abs(len(n[c]['methods']) - len(src['methods'])), c)
                             for c in cands), key=lambda x: -x[0])
            best = [c for s, c in scored if s == scored[0][0]]
            if len(best) == 1:
                mapping[obf] = best[0]
                detail.append((obf, best[0], 'rva-multi+shape', len(src['rvas'])))
                continue
            detail.append((obf, None, f'ambiguous-rva({len(cands)})', len(src['rvas'])))
            continue

        # 2) overlap RVA: dem bao nhiêu RVA trong src ton tai o 140
        best_score = 0
        best_name = None
        best_tie = False
        rva_to_names = defaultdict(set)
        for rva in src['rvas']:
            for c in idx_single.get(rva, ()):
                rva_to_names[c].add(rva)
        for c, common in rva_to_names.items():
            if len(common) > best_score:
                best_score = len(common)
                best_name = c
                best_tie = False
            elif len(common) == best_score:
                best_tie = True
        if best_name and best_score >= max(3, len(src['rvas']) * 0.7) and not best_tie:
            mapping[obf] = best_name
            detail.append((obf, best_name, f'rva-overlap-{best_score}/{len(src["rvas"])}', len(src['rvas'])))
            continue

        detail.append((obf, None, 'no-rva-match', len(src['rvas'])))

    print(f'\n=== RESOLVED: {len(mapping)} ===')
    for a, b, how, cnt in detail:
        if b:
            print(f'  {a} -> {b}   [{how}, {cnt} rva]')

    print(f'\n=== UNRESOLVED ===')
    for a, b, how, cnt in detail:
        if not b:
            print(f'  {a}: {how} ({cnt} rva)')

    with open('type_mapping.json', 'w', encoding='utf-8') as f:
        json.dump(mapping, f, indent=2, ensure_ascii=False)
    print(f'\nTotal mapped: {len(mapping)}')


if __name__ == '__main__':
    main()
