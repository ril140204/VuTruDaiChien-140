#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Trich xuat user strings (ldstr) tu IL dump cua Client.dll."""
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

path = sys.argv[1] if len(sys.argv) > 1 else 'client_il.txt'
d = open(path, 'r', encoding='utf-8', errors='replace').read()

ss = re.findall(r'ldstr\s+"(.*?)"', d)
print(f'ldstr count: {len(ss)}')

seen = []
for s in ss:
    if s not in seen:
        seen.append(s)
print(f'unique: {len(seen)}\n')

for s in seen:
    print(repr(s))
