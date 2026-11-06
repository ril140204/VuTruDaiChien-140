#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Mapping type obfuscate (ver132) -> (ban 140) bang "semantic fingerprint".

Vi du: 2 type deu co field kieu (string, int, bool) + 1 static self + 1 method
(string,int) + 1 method (bool) -> cung 1 "vai tro" trong game.

Tien do:
  1. Với mỗi class trong dump, tach thanh 2 nhom:
     - "public API" : field/method co ten GOC (chua obfuscate), vd: 'show', 'clear',
       'LoginPanel', 'accountField'...  -> dung de dinh danh vai tro
     - "shape"      : so field, so method, tap kieu field (chu hoa -> X)
  2. So sanh 2 ban bang 3 khoa: (tieu chi public API, shape, tap kieu field)
"""
import io
import json
import re
import sys
from collections import defaultdict

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

CLASS_RE = re.compile(
    r'^public (?:static |abstract |sealed |partial )*class (\w+)'
    r'(?:\s*:\s*([^{]+?))?\s*(?://.*)?$'
)
FIELD_RE = re.compile(
    r'^\s*(?:public|private|protected|internal)?\s*'
    r'(?:static\s+)?(?:readonly\s+)?(?:const\s+)?(?:volatile\s+)?'
    r'([\w\.<>\[\],\s]+?)\s+(\w+)\s*(?:=[^;]*)?;\s*(?://.*)?$'
)
METHOD_RE = re.compile(
    r'^\s*(?:public|private|protected|internal)?\s*'
    r'(?:static\s+)?(?:unsafe\s+)?(?:virtual\s+)?(?:override\s+)?(?:abstract\s+)?'
    r'([\w\.<>\[\],\s]+?)\s+(\w+)\s*\((.*?)\)\s*\{'
)
SKIP = {'const', 'static', 'readonly', 'volatile', 'unsafe', 'extern',
        'public', 'private', 'protected', 'internal'}

# Ten co chu "obfuscate" = toan chu in hoa >= 8 ky tu, dau bang 2 chu hoa
OBF_RE = re.compile(r'^[A-Z]{2}[A-Z0-9]{8,14}$')


def is_obf(name):
    return bool(OBF_RE.match(name))


def parse(path):
    classes = {}
    cur = None
    depth = 0
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            m = CLASS_RE.match(line.rstrip('\n'))
            if m and depth == 0:
                cur = m.group(1)
                classes[cur] = {'fields': [], 'methods': [], 'base': (m.group(2) or '').strip()}
                continue
            if cur is None:
                continue
            s = line.strip()
            if s.startswith('//'):
                continue
            mm = METHOD_RE.match(line.rstrip('\n'))
            if mm:
                ret = ' '.join(t for t in mm.group(1).split() if t not in SKIP)
                args = mm.group(3).strip()
                nargs = 0 if not args else len([a for a in args.split(',') if a.strip()])
                classes[cur]['methods'].append((ret, mm.group(2), nargs))
                continue
            fm = FIELD_RE.match(line.rstrip('\n'))
            if fm and 'class ' not in s and 'struct ' not in s and 'enum ' not in s:
                typ = ' '.join(t for t in fm.group(1).split() if t not in SKIP)
                classes[cur]['fields'].append((typ, fm.group(2)))
                continue
            depth += line.count('{') - line.count('}')
            if depth <= 0:
                cur = None
                depth = 0
    return classes


def norm(t):
    return re.sub(r'\b[A-Z]{2}[A-Z0-9]{8,14}\b', 'X', t)


def fingerprint(cls):
    """Tra ve (public_api, shape) de so sanh."""
    api = set()
    for t, nm in cls['fields']:
        if not is_obf(nm):
            api.add('F:' + nm)
    for r, nm, na in cls['methods']:
        if not is_obf(nm):
            api.add('M:' + nm + '/' + str(na))
    if cls['base'] and not is_obf(cls['base'].split('<')[0].strip()):
        api.add('B:' + cls['base'].split('<')[0].strip())

    ftypes = tuple(sorted(norm(t) for t, _ in cls['fields']))
    mtypes = tuple(sorted((norm(r), na) for r, _, na in cls['methods']))
    shape = (len(cls['fields']), len(cls['methods']), ftypes, mtypes)
    return frozenset(api), shape


def main():
    v = parse('Il2CppDumper/dump.cs')
    n = parse('dump/dump.cs')
    print(f'ver132={len(v)} classes, 140={len(n)} classes')

    # Build index cho 140
    idx_fp = defaultdict(list)
    idx_shape = defaultdict(list)
    idx_api = defaultdict(list)
    for name, cls in n.items():
        api, shape = fingerprint(cls)
        idx_fp[(api, shape)].append(name)
        idx_shape[shape].append(name)
        if api:
            idx_api[api].append(name)

    # type can remap
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
    refs = sorted({s for s in strings if is_obf(s)})

    mapping = {}
    unresolved = []
    for obf in refs:
        if obf not in v:
            unresolved.append((obf, 'not-class'))
            continue
        api, shape = fingerprint(v[obf])

        # 1) exact fingerprint
        c = idx_fp.get((api, shape), [])
        if len(c) == 1:
            mapping[obf] = c[0]
            continue
        if len(c) > 1:
            unresolved.append((obf, f'ambiguous-fp({len(c)})'))
            continue

        # 2) cung shape
        c = idx_shape.get(shape, [])
        if len(c) == 1:
            mapping[obf] = c[0]
            continue
        if len(c) > 1:
            # dung overlap API
            sa = api
            scored = sorted(((len(sa & idx_api.get(fingerprint(n[x])[0], set())), x) for x in c),
                           key=lambda t: -t[0])
            if scored and scored[0][0] > 0 and (len(scored) == 1 or scored[0][0] > scored[1][0]):
                mapping[obf] = scored[0][1]
                continue
            unresolved.append((obf, f'ambiguous-shape({len(c)})'))
            continue

        # 3) cung tap kieu field (shape field) + so method
        fsig = shape[2]
        cand = [name for name, cls in n.items()
                if tuple(sorted(norm(t) for t, _ in cls['fields'])) == fsig
                and len(cls['methods']) == shape[1]]
        if len(cand) == 1:
            mapping[obf] = cand[0]
            continue
        if len(cand) > 1:
            unresolved.append((obf, f'ambiguous-fs({len(cand)})'))
            continue

        unresolved.append((obf, 'no-match'))

    print(f'\n=== RESOLVED: {len(mapping)} / {len(refs)} ===')
    for a, b in sorted(mapping.items()):
        print(f'  {a} -> {b}')

    print(f'\n=== UNRESOLVED: {len(unresolved)} ===')
    for a, why in unresolved:
        print(f'  {a}: {why}')

    with open('type_mapping.json', 'w', encoding='utf-8') as f:
        json.dump(mapping, f, indent=2, ensure_ascii=False)
    print(f'\nTotal mapped: {len(mapping)}')


if __name__ == '__main__':
    main()
