#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Tìm type tương đương IPDLCILIFHD trong bản 140 (5 field: obj, string, int, bool, static-self)."""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

import importlib.util
spec = importlib.util.spec_from_file_location("mt", "map_types.py")
mt = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mt)

v = mt.parse_dump('Il2CppDumper/dump.cs')
n = mt.parse_dump('dump/dump.cs')

target = v['IPDLCILIFHD']
print('ver132 fields:', target['fields'])
print('ver132 methods:', len(target['methods']))
print('ver132 method signatures:', sorted({(m[0], m[2]) for m in target['methods']}))

print('\nCANDIDATES in 140 with 5 fields (string, int, bool + self-ref):')
cands = []
for name, cls in n.items():
    f = cls['fields']
    if len(f) != 5:
        continue
    types = [x[0] for x in f]
    if types.count('string') != 1 or types.count('int') != 1 or types.count('bool') != 1:
        continue
    if not any(t == name for t, _ in f):
        continue
    cands.append((name, types, len(cls['methods']), cls['base'][:30]))

for c in cands[:25]:
    print(' ', c)

print(f'\ntotal candidates: {len(cands)}')
