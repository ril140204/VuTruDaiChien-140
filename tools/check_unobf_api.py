#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Kiem tra API cua cac class UNOBFUSCATED (co ten goc) trong ban 140."""
import io
import re
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

N140 = Path(r"D:\game\VuTruDaiChien_140\tools\dump\dump.cs")
V132 = Path(r"D:\game\VuTruDaiChien_140\tools\Il2CppDumper\dump.cs")

WANT = ['LoginUIManager', 'LoginPanel', 'CharacterChoosingPanel',
        'MyInputField', 'MyButton', 'MainGame', 'GameManager',
        'Character', 'CharacterSelfPanel', 'CharacterPotentialPanel',
        'CharacterMenu', 'DeathPanel', 'ZonePanel', 'ItemMapObject',
        'MyButtonNoText', 'Player', 'MainPlayer']


def dump_classes(path, want):
    out = {w: [] for w in want}
    cur = None
    keep = False
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        for raw in f:
            line = raw.rstrip('\n')
            m = re.match(r'^public\s+(?:static\s+|abstract\s+|sealed\s+|partial\s+)*'
                         r'class\s+([\w/`<>]+)\s*(?::\s*([^{]+?))?\s*'
                         r'(?://\s*TypeDefIndex:\s*(\d+))?\s*$', line)
            if m:
                short = m.group(1).split('/')[-1].split('`')[0]
                cur = short
                keep = cur in want
                if keep:
                    out[cur].append(f'class {m.group(1)}'
                                    + (f' : {m.group(2).strip()}' if m.group(2) else '')
                                    + f'   [TDI {m.group(3)}]')
                continue
            if not keep or cur is None:
                continue
            s = line.strip()
            if not s or s.startswith('//') or s in ('{', '}'):
                continue
            if s.startswith('public ') or s.startswith('private ') or s.startswith('protected '):
                out[cur].append('   ' + s)
    return out


def main():
    print('######## ver132 ########')
    a = dump_classes(V132, WANT)
    print('######## 140 ########')
    b = dump_classes(N140, WANT)

    for w in WANT:
        va, vb = a[w], b[w]
        print(f'\n===== {w} =====')
        print(f'-- 132 ({len(va)}):')
        for x in va[:40]:
            print('  ', x)
        print(f'-- 140 ({len(vb)}):')
        for x in vb[:40]:
            print('  ', x)


if __name__ == '__main__':
    main()
