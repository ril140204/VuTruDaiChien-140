#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Kiem tra su ton tai cua cac MEMBER THEO TEN (khong obfuscate) trong ban 140.
Cac member nay Client.dll goi truc tiep, nen neu ban 140 van con ten goc
thi KHONG CAN mapping obfuscated -> 140 cho cac thanh phan do.
"""
import io
import re
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

N140 = Path(r"D:\game\VuTruDaiChien_140\tools\dump\dump.cs")
V132 = Path(r"D:\game\VuTruDaiChien_140\tools\Il2CppDumper\dump.cs")

# (class, ten member) can tim
WANT = [
    ('LoginUIManager', 'gI'),
    ('LoginUIManager', 'get_loginPanel'),
    ('LoginUIManager', 'get_characterChoosingPanel'),
    ('LoginUIManager', 'showCharacterChoosingPanel'),
    ('LoginPanel', 'get_accountField'),
    ('LoginPanel', 'get_passwordField'),
    ('LoginPanel', 'invokeLoginBtn'),
    ('CharacterChoosingPanel', 'get_characterChoosingPanel'),
    ('MyInputField', 'get_text'),
    ('MyInputField', 'set_text'),
    ('MyInputField', 'get_component'),
    ('MyButton', 'get_onClick'),
    ('MyButton', 'get_component'),
    ('MainGame', 'gI'),
    ('GameManager', 'gI'),
    ('GameManager', 'getMyPlayer'),
    ('Character', 'getId'),
    ('Character', 'getPosition'),
    ('Character', 'setPosition'),
    ('CharacterMenu', 'create'),
    ('CharacterMenu', 'get_characterSelfPanel'),
    ('CharacterSelfPanel', 'get_potentialPanel'),
    ('DeathPanel', 'get_returnButton'),
]


def scan(path, want):
    """Tra ve dict (class, member) -> [dong]"""
    res = {}
    byclass = {}
    for c, m in want:
        byclass.setdefault(c, []).append(m)

    cur = None
    cur_lines = []
    keep = False

    def flush():
        if not keep or cur is None:
            return
        for m in byclass.get(cur, []):
            hits = [l for l in cur_lines if re.search(r'\b' + re.escape(m) + r'\b', l)]
            if hits:
                res[(cur, m)] = hits[:3]

    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        for raw in f:
            line = raw.rstrip('\n')
            mm = re.match(r'^public\s+(?:static\s+|abstract\s+|sealed\s+|partial\s+)*'
                          r'class\s+([\w/`<>]+)\s*(?::\s*([^{]+?))?\s*'
                          r'(?://\s*TypeDefIndex:\s*(\d+))?\s*$', line)
            if mm:
                flush()
                cur = mm.group(1).split('/')[-1].split('`')[0]
                cur_lines = []
                keep = cur in byclass
                continue
            if keep:
                cur_lines.append(line.strip())
    flush()
    return res


def main():
    a = scan(V132, WANT)
    b = scan(N140, WANT)
    print(f'{"CLASS":<26}{"MEMBER":<30}{"132":<8}{"140":<8}STATUS')
    print('-' * 92)
    same = 0
    for c, m in WANT:
        h132 = (c, m) in a
        h140 = (c, m) in b
        st = 'OK' if (h132 and h140) else ('MISSING-140' if h132 else 'absent-both')
        if st == 'OK':
            same += 1
        print(f'{c:<26}{m:<30}{"Y" if h132 else "-":<8}{"Y" if h140 else "-":<8}{st}')
    print(f'\n-> {same}/{len(WANT)} members present in BOTH builds')
    print('\n=== chi tiet 140 ===')
    for c, m in WANT:
        if (c, m) in b:
            print(f'\n[{c}::{m}]')
            for h in b[(c, m)]:
                print('   ', h)


if __name__ == '__main__':
    main()
