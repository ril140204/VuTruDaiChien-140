#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Mapping type obfuscate (ver132) -> (ban 140) bang "ordered sequence signature".

Y tuong: obfuscator doi TEN nhung giu nguyen:
  - thu tu khai bao field/method
  - kieu tra ve / kieu tham so
  - so luong
=> "chuoi ky co thu tu" la khoa khop manh hon multiset.

Chung ta dung 3 muc do khop:
  L1: sequence method (ret,nargs) + sequence field type
  L2: chi sequence method
  L3: chi shape (so field, so method) + tap kieu field
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
OBF_RE = re.compile(r'^[A-Z]{2}[A-Z0-9]{8,14}$')


def is_obf(n):
    return bool(OBF_RE.match(n))


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


def seq_sig(cls):
    """(chuoi kieu field theo thu tu, chuoi (ret,nargs) method theo thu tu)"""
    fs = tuple(norm(t) for t, _ in cls['fields'])
    ms = tuple((norm(r), na) for r, _, na in cls['methods'])
    return fs, ms


def main():
    v = parse('Il2CppDumper/dump.cs')
    n = parse('dump/dump.cs')
    print(f'ver132={len(v)}, 140={len(n)}')

    # index 140
    idx_full = defaultdict(list)
    idx_mseq = defaultdict(list)
    idx_shape = defaultdict(list)
    for name, cls in n.items():
        fs, ms = seq_sig(cls)
        idx_full[(fs, ms)].append(name)
        idx_mseq[ms].append(name)
        idx_shape[(len(cls['fields']), len(cls['methods']),
                   tuple(sorted(fs)))].append(name)

    # type can remap
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
        src = v[obf]
        fs, ms = seq_sig(src)

        c = idx_full.get((fs, ms), [])
        if len(c) == 1:
            mapping[obf] = c[0]
            continue
        if len(c) > 1:
            unresolved.append((obf, f'amb-full({len(c)})'))
            continue

        c = idx_mseq.get(ms, [])
        if len(c) == 1:
            mapping[obf] = c[0]
            continue
        if len(c) > 1:
            # dung them: cung tap kieu field (dung thu tu)
            sub = [x for x in c if tuple(norm(t) for t, _ in n[x]['fields']) == fs]
            if len(sub) == 1:
                mapping[obf] = sub[0]
                continue
            unresolved.append((obf, f'amb-mseq({len(c)})'))
            continue

        c = idx_shape.get((len(src['fields']), len(src['methods']),
                           tuple(sorted(fs))), [])
        if len(c) == 1:
            mapping[obf] = c[0]
            continue
        if len(c) > 1:
            unresolved.append((obf, f'amb-shape({len(c)})'))
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
