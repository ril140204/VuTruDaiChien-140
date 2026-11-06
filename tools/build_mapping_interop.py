#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Mapping obfuscated type: ver132 -> ban 140, dua tren interop shape.

Tin hieu (theo thu tu uu tien):
  A. SHAPE EXACT  -- tap field + tap method (return type + so tham so + modifiers)
                     giong het -> mapping chắc chan.
  B. SHAPE LOOSE  -- bo phan biet private/protected, chi giu public API.
  C. FIELD COUNT + METHOD COUNT + base type.
  D. Members ma Client.dll goi (tu client_refs.json) -> doi chieu voi
     method/field co ten (obfuscated) tuong ung trong 140.

Tat ca deu chuan hoa ten obfuscate thanh "X" truoc khi so sanh.
"""
import io
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

BASE = Path(r"D:\game\VuTruDaiChien_140\tools")
SHAPE132 = BASE / "shape_132.json"
SHAPE140 = BASE / "shape_140.json"
REFS = BASE / "client_refs.json"
OUT = BASE / "type_mapping.json"

OBF_RE = re.compile(r'^[A-Z]{2}[A-Z0-9]{8,14}$')
KNOWN = {
    'DJLADAKKHMP': 'BBOIDGFADKJ',
    'IPDLCILIFHD': 'APFNHMGMNKL',
}


def norm_obf(s):
    """Thay ten obfuscated (>=10 ky tu hoa) bang X de so sanh shape."""
    return re.sub(r'\b[A-Z]{2}[A-Z0-9]{8,14}\b', 'X', s)


def norm_type_token(t):
    t = t.strip()
    if OBF_RE.match(t):
        return 'X'
    return t


def load_shape(path):
    raw = json.loads(path.read_text(encoding='utf-8'))
    out = {}
    for name, sh in raw.items():
        out[name] = {
            'base': sh.get('baseType', ''),
            'fields': [norm_obf(f) for f in sh.get('fields', [])],
            'methods': [norm_obf(m) for m in sh.get('methods', [])],
        }
    return out


def main():
    s132 = load_shape(SHAPE132)
    s140 = load_shape(SHAPE140)
    refs = json.loads(REFS.read_text(encoding='utf-8'))
    members = refs.get('members', {})
    typerefs = refs.get('typeRefs', [])

    print(f'shape132={len(s132)} shape140={len(s140)} typerefs={len(typerefs)}')

    # Build index cho 140 theo nhieu muc do
    idx_exact = defaultdict(list)
    idx_public = defaultdict(list)
    idx_counts = defaultdict(list)
    idx_methodnames = defaultdict(list)
    idx_fieldnames = defaultdict(list)

    for name, sh in s140.items():
        b = norm_obf(sh['base'])
        fs = tuple(sorted(sh['fields']))
        ms = tuple(sorted(sh['methods']))
        idx_exact[(b, fs, ms)].append(name)
        idx_exact[(b, fs, ms)].append(name)

        pub_f = tuple(sorted(f for f in sh['fields'] if ' X ' in f or not OBF_RE.match(f.split()[-1])))
        pub_m = tuple(sorted(m for m in sh['methods'] if ' P ' in m or ' S ' in m))
        idx_public[(b, pub_f, pub_m)].append(name)

        idx_counts[(b, len(sh['fields']), len(sh['methods']))].append(name)

        mnames = frozenset(re.findall(r'\b([A-Z]{2}[A-Z0-9]{8,14})\b', ' '.join(sh['methods'])))
        idx_methodnames[mnames].append(name)
        fnames = frozenset(re.findall(r'\b([A-Z]{2}[A-Z0-9]{8,14})\b', ' '.join(sh['fields'])))
        idx_fieldnames[fnames].append(name)

    # Gom members ma Client.dll goi theo type
    client_members = {k: v for k, v in members.items()}

    mapping = {}
    unresolved = []
    details = {}

    for obf in typerefs:
        if not OBF_RE.match(obf):
            continue  # ten unobfuscated -> giu nguyen
        src = s132.get(obf)
        if src is None:
            unresolved.append((obf, 'not-in-132-interop'))
            continue

        b = norm_obf(src['base'])
        fs = tuple(sorted(src['fields']))
        ms = tuple(sorted(src['methods']))

        # A) exact shape
        c = idx_exact.get((b, fs, ms), [])
        c = sorted(set(c))
        if len(c) == 1:
            mapping[obf] = c[0]
            details[obf] = 'exact'
            continue
        if len(c) > 1:
            # B) dung method names ma Client goi de tach
            want = set()
            for sig in client_members.get(obf, []):
                m = re.search(r'\b([A-Z]{2}[A-Z0-9]{8,14})\b', sig)
                if m:
                    want.add(m.group(1))
            sub = []
            for x in c:
                xm = set(re.findall(r'\b([A-Z]{2}[A-Z0-9]{8,14})\b', ' '.join(s140[x]['methods'])))
                xf = set(re.findall(r'\b([A-Z]{2}[A-Z0-9]{8,14})\b', ' '.join(s140[x]['fields'])))
                overlap = len(want & (xm | xf))
                sub.append((overlap, x))
            sub.sort(reverse=True)
            if sub and sub[0][0] > 0 and (len(sub) == 1 or sub[0][0] > sub[1][0]):
                mapping[obf] = sub[0][1]
                details[obf] = f'exact+member({sub[0][0]})'
                continue
            unresolved.append((obf, f'amb-exact({len(c)})'))
            continue

        # B) public-only shape
        pub_f = tuple(sorted(f for f in src['fields'] if not OBF_RE.match(f.split()[-1])))
        pub_m = tuple(sorted(m for m in src['methods'] if ' P ' in m or ' S ' in m))
        c = sorted(set(idx_public.get((b, pub_f, pub_m), [])))
        if len(c) == 1:
            mapping[obf] = c[0]
            details[obf] = 'public'
            continue
        if len(c) > 1:
            unresolved.append((obf, f'amb-public({len(c)})'))
            continue

        # C) counts
        c = sorted(set(idx_counts.get((b, len(src['fields']), len(src['methods'])), [])))
        if len(c) == 1:
            mapping[obf] = c[0]
            details[obf] = 'counts'
            continue
        if len(c) > 1:
            unresolved.append((obf, f'amb-counts({len(c)})'))
            continue

        # D) method-name set khop Client refs
        want = set()
        for sig in client_members.get(obf, []):
            m = re.search(r'\b([A-Z]{2}[A-Z0-9]{8,14})\b', sig)
            if m:
                want.add(m.group(1))
        if want:
            best = None
            for name, sh in s140.items():
                xm = set(re.findall(r'\b([A-Z]{2}[A-Z0-9]{8,14})\b', ' '.join(sh['methods'])))
                xf = set(re.findall(r'\b([A-Z]{2}[A-Z0-9]{8,14})\b', ' '.join(sh['fields'])))
                overlap = len(want & (xm | xf))
                if overlap and (best is None or overlap > best[0]):
                    best = (overlap, name)
                    if best[0] == len(want):
                        break
            if best and best[0] >= max(1, len(want) - 1):
                mapping[obf] = best[1]
                details[obf] = f'member-overlap({best[0]}/{len(want)})'
                continue

        unresolved.append((obf, 'no-match'))

    print(f'\n=== RESOLVED: {len(mapping)} ===')
    for k in sorted(mapping):
        print(f'  {k} -> {mapping[k]}   [{details.get(k, "?")}]')

    print(f'\n=== UNRESOLVED: {len(unresolved)} ===')
    for o, why in unresolved:
        print(f'  {o}: {why}')

    print('\n=== CHECK vs known pairs ===')
    for old, new in KNOWN.items():
        got = mapping.get(old)
        print(f'  {old}: expected {new}, got {got}  '
              f'[{"MATCH" if got == new else "MISMATCH"}]')

    OUT.write_text(json.dumps(mapping, indent=2, ensure_ascii=False),
                   encoding='utf-8')
    print(f'\nSaved {len(mapping)} -> {OUT}')


if __name__ == '__main__':
    main()
