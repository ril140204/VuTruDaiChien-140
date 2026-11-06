#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Trich xuat phan IL cua mot type tu file IL dump."""
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

path = sys.argv[1]
target = sys.argv[2]
outfile = sys.argv[3] if len(sys.argv) > 3 else None

d = open(path, 'r', encoding='utf-8', errors='replace').read()

# tach theo "// ===== TYPE "
parts = re.split(r'(?m)^// ===== TYPE ', d)
found = []
for p in parts[1:]:
    header = p.split('\n', 1)[0].strip()
    if target.lower() in header.lower():
        found.append('// ===== TYPE ' + p)

text = '\n'.join(found)
if outfile:
    open(outfile, 'w', encoding='utf-8').write(text)
    print(f'found {len(found)} type(s) -> {outfile} ({len(text)} chars)')
else:
    print(text)
