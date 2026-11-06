#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Trich tat ca lenh ghep binary (Message2) trong QLTK:
tim theo mau:  ldc.i4[.s] <so>  -> newobj QLTK.Message2::.ctor
va ghi lai (command, ham, payload calls).
"""
import io
import re
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

path = Path(sys.argv[1] if len(sys.argv) > 1 else 'rt_qltk_mainform.txt')
d = path.read_text(encoding='utf-8', errors='replace')

lines = d.split('\n')

# tim cac method block
cur_method = None
cur_sig = None
block = []
blocks = []

for ln in lines:
    m = re.match(r'\s*// --- METHOD (.+)$', ln)
    if m:
        if cur_method and block:
            blocks.append((cur_method, cur_sig, block))
        cur_method = m.group(1).strip()
        cur_sig = None
        block = []
        continue
    m = re.match(r'\s*// sig: (.+)$', ln)
    if m and cur_method:
        cur_sig = m.group(1).strip()
        continue
    block.append(ln)

if cur_method and block:
    blocks.append((cur_method, cur_sig, block))

print(f'total methods: {len(blocks)}\n')

# 1) tim method tao Message2 voi command literal
print('=== COMMANDS (Message2 ctor) ===')
cmds = []
for name, sig, blk in blocks:
    text = '\n'.join(blk)
    if 'QLTK.Message2::.ctor' not in text:
        continue
    # command = ldc.i4.s X hoac ldc.i4 X ngay truoc newobj
    m = re.search(
        r'IL_[0-9a-f]+:\s+ldc\.i4(?:\.s)?\s+(-?\d+)\s*\n\s*IL_[0-9a-f]+:\s+newobj QLTK\.Message2::\.ctor',
        text)
    if not m:
        print(f'  [??] {name}  (khong tim thay command literal)')
        continue
    cmd = int(m.group(1))
    # payload: cac Write* call sau newobj
    writes = re.findall(r'IL_[0-9a-f]+:\s+callvirt QLTK\.Message2::(Write\w+)', text)
    # so tham so la push truoc do
    cmds.append((cmd, name, sig, writes))
    print(f'  {cmd:>5}  {name}')
    print(f'          sig: {sig}')
    print(f'          payload: {writes if writes else "(khong co)"}')

print(f'\ntotal commands: {len(cmds)}')

# 2) tim cac ldc.i4.s -1xx chung (bao cao them)
print('\n=== ALL ldc.i4.s negatives trong MainForm (unique) ===')
neg = sorted({int(x) for x in re.findall(r'ldc\.i4\.s\s+(-?\d+)', d)})
print([x for x in neg if x < -50])

# 3) tim cho co "WriteString"/"WriteInt" de xac dinh payload count
print('\n=== PAYLOAD DETAIL ===')
for cmd, name, sig, writes in sorted(cmds):
    print(f'cmd={cmd:<5} {name:<35} writes={writes}')
