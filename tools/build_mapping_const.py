#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Mapping obfuscated type: ver132 -> ban 140.

Tin hieu dung (theo thu tu uu tien):
  1. CONST VALUES  -- gia tri const khong doi giua cac build (chi TEN doi).
                       Day la tin hieu manh nhat.
  2. METHOD SHAPE  -- (kieu tra ve, so tham so) theo thu tu + base type.
  3. FIELD SHAPE  -- kieu field (normalize ten obfuscate -> X) theo thu tu.

Moi muc do tach biet danh sach ung vien, neu >1 thi thu muc do tiep theo.
Neu van >1, dung "score" (so tin hieu khop) va yeu cau score >= threshold.
"""
import io
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

V132 = Path(r"D:\game\VuTruDaiChien_140\tools\Il2CppDumper\dump.cs")
N140 = Path(r"D:\game\VuTruDaiChien_140\tools\dump\dump.cs")
CLIENT = Path(r"D:\game\ver132\Tool_Kep_12h\BepInEx\plugins\Client.dll")
OUT = Path(r"D:\game\VuTruDaiChien_140\tools\type_mapping.json")

OBF_RE = re.compile(r'^[A-Z]{2}[A-Z0-9]{8,14}$')
KNOWN = {
    'DJLADAKKHMP': 'BBOIDGFADKJ',
    'IPDLCILIFHD': 'APFNHMGMNKL',
}


# ---------------------------------------------------------------- parser
CLASS_RE = re.compile(
    r'^public\s+(?P<mods>(?:static\s+|abstract\s+|sealed\s+|partial\s+)*)class\s+'
    r'(?P<name>\w+)\s*(?::\s*(?P<base>[^{]+?))?\s*(?://.*)?$'
)
CONST_RE = re.compile(
    r'^\s*public\s+const\s+(?P<type>[\w\.<>\[\],\s]+?)\s+(?P<name>\w+)\s*=\s*(?P<val>[^;]+);'
)
FIELD_RE = re.compile(
    r'^\s*(?:public|private|protected|internal)?\s*'
    r'(?:static\s+)?(?:readonly\s+)?(?:volatile\s+)?'
    r'(?P<type>[\w\.<>\[\],\s]+?)\s+(?P<name>\w+)\s*(?:=[^;]*)?;\s*(?://.*)?$'
)
METHOD_RE = re.compile(
    r'^\s*(?:public|private|protected|internal)?\s*'
    r'(?:static\s+)?(?:unsafe\s+)?(?:virtual\s+)?(?:override\s+)?(?:abstract\s+)?'
    r'(?P<ret>[\w\.<>\[\],\s]+?)\s+(?P<name>\w+)\s*\((?P<args>.*?)\)\s*\{'
)
MODS = {'const', 'static', 'readonly', 'volatile', 'unsafe', 'extern',
        'public', 'private', 'protected', 'internal'}


def norm_type(t):
    t = ' '.join(x for x in t.split() if x not in MODS)
    return re.sub(r'\b[A-Z]{2}[A-Z0-9]{8,14}\b', 'X', t.strip())


def parse(path):
    """dump.cs -> {name: {...}}"""
    classes = {}
    cur = None
    depth = 0
    pending_ctor = None
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        for raw in f:
            line = raw.rstrip('\n')
            if cur is None:
                m = CLASS_RE.match(line)
                if m:
                    cur = m.group('name')
                    classes[cur] = {
                        'mods': m.group('mods').split(),
                        'base': norm_type(m.group('base') or ''),
                        'consts': [],       # danh sach gia tri const theo thu tu
                        'fields': [],       # danh sach kieu field theo thu tu
                        'methods': [],      # danh sach (retNorm, nargs, nameNorm)
                        'nconst': 0,
                    }
                    depth = 0
                continue

            s = line.strip()
            if not s or s.startswith('//'):
                continue

            mc = CONST_RE.match(line)
            if mc:
                classes[cur]['consts'].append(mc.group('val').strip())
                classes[cur]['nconst'] += 1
                continue

            mm = METHOD_RE.match(line)
            if mm:
                args = mm.group('args').strip()
                nargs = 0 if not args else len([a for a in args.split(',') if a.strip()])
                mname = mm.group('name')
                # ten method cung bi obfuscate -> chi giu .ctor/.cctor
                mname_n = mname if mname in ('.ctor', '.cctor') else 'X'
                classes[cur]['methods'].append(
                    (norm_type(mm.group('ret')), nargs, mname_n))
                continue

            mf = FIELD_RE.match(line)
            if mf and 'class ' not in s and 'struct ' not in s and 'enum ' not in s:
                classes[cur]['fields'].append(norm_type(mf.group('type')))
                continue

            depth += line.count('{') - line.count('}')
            if depth <= 0:
                cur = None
                depth = 0
    return classes


# ---------------------------------------------------------------- signals
def sig_const(c):
    return tuple(c['consts'])


def sig_method(c):
    return (c['base'], tuple((r, n) for r, n, _ in c['methods']))


def sig_field(c):
    return (c['base'], tuple(c['fields']))


def sig_loose(c):
    return (c['base'],
            tuple((r, n) for r, n, _ in c['methods']),
            tuple(c['fields']))


def client_obf_types():
    data = CLIENT.read_bytes()
    cand = set()
    for m in re.finditer(rb'(?<![A-Za-z0-9_])([A-Z]{2}[A-Z0-9]{8,14})(?![A-Za-z0-9_])',
                         data):
        s = m.group(1).decode('ascii')
        if len(s) == 11:
            cand.add(s)
    return cand


def main():
    v = parse(V132)
    n = parse(N140)
    print(f'ver132={len(v)} classes, 140={len(n)} classes')

    cand = client_obf_types()
    refs = sorted([c for c in cand if c in v])
    print(f'Client.dll references {len(cand)} obf strings, {len(refs)} are ver132 classes')

    # Build indices cho 140
    idx = {
        'const': defaultdict(list),
        'method': defaultdict(list),
        'field': defaultdict(list),
        'loose': defaultdict(list),
    }
    for name, c in n.items():
        idx['const'][sig_const(c)].append(name)
        idx['method'][sig_method(c)].append(name)
        idx['field'][sig_field(c)].append(name)
        idx['loose'][sig_loose(c)].append(name)

    mapping = {}
    unresolved = []
    for obf in refs:
        src = v[obf]
        found = None
        reason = 'no-match'

        # 1) const values (bo qua class khong co const)
        if src['consts']:
            c = idx['const'].get(sig_const(src), [])
            if len(c) == 1:
                found, reason = c[0], 'const'
            elif len(c) > 1:
                reason = f'amb-const({len(c)})'
                # thu khoai them bang method shape
                sub = [x for x in c if sig_method(n[x]) == sig_method(src)]
                if len(sub) == 1:
                    found, reason = sub[0], 'const+method'
                else:
                    sub2 = [x for x in c if sig_field(n[x]) == sig_field(src)]
                    if len(sub2) == 1:
                        found, reason = sub2[0], 'const+field'
                    else:
                        reason = f'amb-const({len(c)})'
                        unresolved.append((obf, reason, c))
                        continue
            else:
                reason = 'const-miss'

        if found is None and reason == 'const-miss':
            # 2) method shape
            c = idx['method'].get(sig_method(src), [])
            if len(c) == 1:
                found, reason = c[0], 'method'
            elif len(c) > 1:
                sub = [x for x in c if sig_field(n[x]) == sig_field(src)]
                if len(sub) == 1:
                    found, reason = sub[0], 'method+field'
                else:
                    unresolved.append((obf, f'amb-method({len(c)})', c))
                    continue
            else:
                reason = 'method-miss'

        if found is None and reason == 'method-miss':
            c = idx['field'].get(sig_field(src), [])
            if len(c) == 1:
                found, reason = c[0], 'field'
            elif len(c) > 1:
                unresolved.append((obf, f'amb-field({len(c)})', c))
                continue
            else:
                reason = 'field-miss'

        if found is None and reason == 'field-miss':
            c = idx['loose'].get(sig_loose(src), [])
            if len(c) == 1:
                found, reason = c[0], 'loose'
            else:
                unresolved.append((obf, f'loose-miss({len(c)})', c[:8]))
                continue

        mapping[obf] = found
        print(f'  {obf} -> {found}   [{reason}]')

    print(f'\n=== RESOLVED: {len(mapping)} / {len(refs)} ===')
    print(f'\n=== UNRESOLVED: {len(unresolved)} ===')
    for o, why, cands in unresolved:
        print(f'  {o}: {why}  candidates={cands[:6]}')

    # sanity check
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
