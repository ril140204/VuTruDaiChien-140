#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Mapping obfuscated type: ver132 -> ban 140, dua tren dump.cs Il2CppDumper.

Tin hieu chinh (theo thu tu uu tien):
  1. FIELD OFFSETS + kieu field  -- offset trong IL2CPP class layout rat on dinh
     giua cac build (cung thu tu field => cung offset).
  2. CONST VALUES -- gia tri const khong doi.
  3. BASE TYPE (normalize ten obfuscate -> X) + so method + so field.
  4. METHOD SHAPE (kieu tra ve + so tham so + static), da SORT de khong phu thuoc
     thu tu khai bao (obfuscator hay doi thu tu method).

Diem khoa: phai kiem chung ket qua voi cac cap da biet dung:
    DJLADAKKHMP -> BBOIDGFADKJ
    IPDLCILIFHD -> APFNHMGMNKL
"""
import io
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

BASE = Path(r"D:\game\VuTruDaiChien_140\tools")
V132 = BASE / "Il2CppDumper" / "dump.cs"
N140 = BASE / "dump" / "dump.cs"
REFS = BASE / "client_refs.json"
OUT = BASE / "type_mapping.json"

OBF_RE = re.compile(r'^[A-Z]{2}[A-Z0-9]{8,14}$')
KNOWN = {
    'DJLADAKKHMP': 'BBOIDGFADKJ',
    'IPDLCILIFHD': 'APFNHMGMNKL',
}

CLASS_RE = re.compile(
    r'^public\s+(?P<mods>(?:static\s+|abstract\s+|sealed\s+|partial\s+)*)class\s+'
    r'(?P<name>[\w/`<>]+)\s*(?::\s*(?P<base>[^{]+?))?\s*'
    r'(?://\s*TypeDefIndex:\s*(?P<tdi>\d+))?\s*$'
)
# field voi offset:  private string AILNBECANPP; // 0x18
FIELD_RE = re.compile(
    r'^\s*(?:public|private|protected|internal)?\s*'
    r'(?:static\s+)?(?:readonly\s+)?(?:volatile\s+)?'
    r'(?P<type>[\w\.<>\[\],\s]+?)\s+(?P<name>\w+)\s*'
    r'(?:=[^;]*)?;\s*(?://\s*(?P<off>0x[0-9a-fA-F]+))?\s*$'
)
CONST_RE = re.compile(
    r'^\s*public\s+const\s+(?P<type>[\w\.<>\[\],\s]+?)\s+(?P<name>\w+)\s*=\s*(?P<val>[^;]+);'
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
    t = re.sub(r'\b[A-Z]{2}[A-Z0-9]{8,14}\b', 'X', t)
    return re.sub(r'\s+', ' ', t).strip()


def parse(path):
    classes = {}
    cur = None
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        for raw in f:
            line = raw.rstrip('\n')
            m = CLASS_RE.match(line)
            if m:
                full = m.group('name')
                short = full.split('/')[-1].split('`')[0]
                cur = short
                classes[cur] = {
                    'base': norm_type(m.group('base') or ''),
                    'fields': [],       # (normType, offsetStr)
                    'consts': [],
                    'methods': [],      # (normRet, nargs, static)
                    'tdi': m.group('tdi'),
                    'full': full,
                }
                continue
            if cur is None:
                continue
            s = line.strip()
            if not s or s.startswith('//'):
                continue

            mc = CONST_RE.match(line)
            if mc:
                classes[cur]['consts'].append(mc.group('val').strip())
                continue

            mm = METHOD_RE.match(line)
            if mm:
                args = mm.group('args').strip()
                nargs = 0 if not args else len([a for a in args.split(',') if a.strip()])
                is_static = ' static ' in f' {line.strip()} '
                classes[cur]['methods'].append(
                    (norm_type(mm.group('ret')), nargs, is_static))
                continue

            mf = FIELD_RE.match(line)
            if mf and 'class ' not in s and 'struct ' not in s and 'enum ' not in s:
                classes[cur]['fields'].append(
                    (norm_type(mf.group('type')), mf.group('off') or ''))
                continue
    return classes


def field_key(c):
    """Khoa field: (offset, normType) - bo qua ten field."""
    out = []
    for t, off in c['fields']:
        out.append((off, t))
    return tuple(sorted(out, key=lambda x: (x[0] == '', x[0])))


def method_key(c):
    return tuple(sorted(c['methods']))


def main():
    v = parse(V132)
    n = parse(N140)
    refs = json.loads(REFS.read_text(encoding='utf-8'))
    typerefs = refs.get('typeRefs', [])
    client_members = refs.get('members', {})

    print(f'ver132={len(v)} classes, 140={len(n)} classes, typerefs={len(typerefs)}')

    obf_targets = [t for t in typerefs if OBF_RE.match(t)]
    print(f'obfuscated targets: {len(obf_targets)}')

    # Build index 140 theo field_key (offset + type)
    idx_fk = defaultdict(list)
    idx_fk_base = defaultdict(list)
    for name, c in n.items():
        idx_fk[field_key(c)].append(name)
        idx_fk_base[(norm_type(c['base']), field_key(c))].append(name)

    mapping = {}
    details = {}
    unresolved = []

    for obf in obf_targets:
        src = v.get(obf)
        if src is None:
            unresolved.append((obf, 'not-in-132-dump', []))
            continue

        fk = field_key(src)
        base = norm_type(src['base'])

        # 1) field offset + type, kem base
        c = sorted(set(idx_fk_base.get((base, fk), [])))
        why = None
        if len(c) == 1:
            mapping[obf] = c[0]
            details[obf] = 'field+base'
            continue
        if len(c) > 1:
            why = f'amb-field+base({len(c)})'
            cands = c
        else:
            # 2) field offset + type (bo base)
            c2 = sorted(set(idx_fk.get(fk, [])))
            if len(c2) == 1:
                mapping[obf] = c2[0]
                details[obf] = 'field'
                continue
            if len(c2) > 1:
                why = f'amb-field({len(c2)})'
                cands = c2
            else:
                # 3) const values
                cands = []
                if src['consts']:
                    cv = tuple(sorted(src['consts']))
                    for name, cc in n.items():
                        if tuple(sorted(cc['consts'])) == cv:
                            cands.append(name)
                    if len(cands) == 1:
                        mapping[obf] = cands[0]
                        details[obf] = 'const'
                        continue
                    if len(cands) > 1:
                        why = f'amb-const({len(cands)})'
                    else:
                        why = 'no-field-match'

        # 4) phan biet bang method shape
        if cands:
            mk = method_key(src)
            sub = [x for x in cands if method_key(n[x]) == mk]
            if len(sub) == 1:
                mapping[obf] = sub[0]
                details[obf] = why + '+method'
                continue
            if len(sub) > 1:
                # 5) dung so field/const de tach them
                sub2 = [x for x in sub if len(n[x]['fields']) == len(src['fields'])]
                if len(sub2) == 1:
                    mapping[obf] = sub2[0]
                    details[obf] = why + '+method+nfield'
                    continue
                unresolved.append((obf, f'{why}+amb-method({len(sub)})', sub[:8]))
                continue
            # 6) fallback: score bang overlap member-call
            want = set()
            for sig in client_members.get(obf, []):
                mm = re.search(r'\b([A-Z]{2}[A-Z0-9]{8,14})\b', sig)
                if mm:
                    want.add(mm.group(1))
            if want:
                best = None
                for x in cands:
                    xm = set(re.findall(r'\b([A-Z]{2}[A-Z0-9]{8,14})\b',
                                         ' '.join(str(m) for m in n[x]['methods'])))
                    xf = set(re.findall(r'\b([A-Z]{2}[A-Z0-9]{8,14})\b',
                                         ' '.join(str(f[0]) for f in n[x]['fields'])))
                    ov = len(want & (xm | xf))
                    if ov and (best is None or ov > best[0]):
                        best = (ov, x)
                if best and best[0] >= 1:
                    mapping[obf] = best[1]
                    details[obf] = f'{why}+member({best[0]})'
                    continue
            unresolved.append((obf, why, cands[:8]))
            continue

        unresolved.append((obf, why, []))

    print(f'\n=== RESOLVED: {len(mapping)} / {len(obf_targets)} ===')
    for k in sorted(mapping):
        print(f'  {k} -> {mapping[k]}   [{details.get(k, "?")}]')

    print(f'\n=== UNRESOLVED: {len(unresolved)} ===')
    for o, why, cands in unresolved:
        print(f'  {o}: {why}  {cands}')

    print('\n=== CHECK vs known pairs ===')
    ok = 0
    for old, new in KNOWN.items():
        got = mapping.get(old)
        m = got == new
        ok += 1 if m else 0
        print(f'  {old}: expected {new}, got {got}  [{"MATCH" if m else "MISMATCH"}]')
    print(f'  -> {ok}/{len(KNOWN)} known pairs verified')

    OUT.write_text(json.dumps(mapping, indent=2, ensure_ascii=False),
                   encoding='utf-8')
    print(f'\nSaved {len(mapping)} -> {OUT}')


if __name__ == '__main__':
    main()
