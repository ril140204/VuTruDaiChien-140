#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Đối chiếu type tương đương giữa 2 bản game (ver132 vs 140) dựa trên
cấu trúc field/method (tên bị obfuscate đổi theo seed mỗi build).

Cách hoạt động:
  1. Parse dump.cs của cả 2 bản -> lấy (tên class, tập field, tập method)
  2. Với mỗi type cần tìm trong Client.dll (ver132), tính "signature"
     = chữ ký cấu trúc (loại bỏ tên riêng bị obfuscate)
  3. Tìm type ở bản 140 có signature khớp
"""
import re
import sys

CLASS_RE = re.compile(
    r'^public (?:static |abstract |sealed |partial )*class (\w+)'
    r'(?:\s*:\s*([^{]+?))?\s*(?://.*)?$'
)
# Field: [modifiers] <type> <name> [= init]; [// offset]
FIELD_RE = re.compile(
    r'^\s*(?:public|private|protected|internal)?\s*'
    r'(?:static\s+)?(?:readonly\s+)?(?:const\s+)?(?:volatile\s+)?'
    r'([\w\.<>\[\],\s]+?)\s+(\w+)\s*(?:=[^;]*)?;\s*(?://.*)?$'
)
# Method: [modifiers] <ret> <name>(<args>) {
METHOD_RE = re.compile(
    r'^\s*(?:public|private|protected|internal)?\s*'
    r'(?:static\s+)?(?:unsafe\s+)?(?:virtual\s+)?(?:override\s+)?(?:abstract\s+)?'
    r'([\w\.<>\[\],\s]+?)\s+(\w+)\s*\((.*?)\)\s*\{'
)

SKIP = {'const', 'static', 'readonly', 'volatile', 'unsafe', 'extern',
        'public', 'private', 'protected', 'internal'}


def parse_dump(path):
    """Parse dump.cs -> {class_name: {'fields': [...], 'methods': [...], 'base': str}}"""
    classes = {}
    cur = None
    depth = 0          # độ sâu { } bên trong class hiện tại
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            stripped = line.rstrip('\n')
            m = CLASS_RE.match(stripped)
            if m and depth == 0:
                cur = m.group(1)
                classes[cur] = {
                    'fields': [], 'methods': [],
                    'base': (m.group(2) or '').strip(),
                }
                continue
            if cur is None:
                continue
            s = stripped.strip()
            if s.startswith('//'):
                continue
            # field?
            fm = FIELD_RE.match(stripped)
            if fm:
                typ = ' '.join(t for t in fm.group(1).split() if t not in SKIP)
                classes[cur]['fields'].append((typ, fm.group(2)))
                continue
            # method?
            mm = METHOD_RE.match(stripped)
            if mm:
                ret = ' '.join(t for t in mm.group(1).split() if t not in SKIP)
                args = mm.group(3).strip()
                nargs = 0 if not args else len([a for a in args.split(',') if a.strip()])
                classes[cur]['methods'].append((ret, mm.group(2), nargs))
                continue
            # cập nhật độ sâu
            depth += stripped.count('{') - stripped.count('}')
            if depth <= 0:
                cur = None
                depth = 0
    return classes


def norm_type(t):
    """Chuẩn hoá tên type: thay tên obfuscate (chữ hoa dài) bằng X."""
    return re.sub(r'\b[A-Z]{2}[A-Z0-9]{8}\b', 'X', t)


def signature(cls):
    """Signature không phụ thuộc tên obfuscate."""
    fsig = tuple(sorted(norm_type(t) for t, _ in cls['fields']))
    msig = tuple(sorted((norm_type(r), n) for r, _, n in cls['methods']))
    return fsig, msig


def main():
    v132 = parse_dump(sys.argv[1])
    n140 = parse_dump(sys.argv[2])
    targets = sys.argv[3:]

    idx = {}
    for name, cls in n140.items():
        idx.setdefault(signature(cls), []).append(name)

    print(f"parsed: ver132={len(v132)} classes, 140={len(n140)} classes")

    for t in targets:
        if t not in v132:
            print(f"\n=== {t} === NOT FOUND in ver132 dump")
            continue
        sig = signature(v132[t])
        vf, vm = sig
        exact = idx.get(sig, [])
        print(f"\n=== {t} ===")
        print(f"  ver132: fields={len(v132[t]['fields'])} methods={len(v132[t]['methods'])} "
              f"base={v132[t]['base'][:40]}")
        print(f"  field types: {[f[0] for f in v132[t]['fields']]}")
        if exact:
            print(f"  *** EXACT MATCH in 140: {exact}")
        else:
            cand = []
            for name, cls in n140.items():
                fs, ms = signature(cls)
                same_f = sum(1 for x in fs if x in vf)
                same_m = sum(1 for x in ms if x in vm)
                score = same_f + same_m
                if score >= max(3, (len(vf) + len(vm)) * 0.5):
                    cand.append((score, name, len(cls['fields']), len(cls['methods'])))
            cand.sort(reverse=True)
            print("  no exact match. Top candidates:")
            for score, name, nf, nm in cand[:8]:
                print(f"    {name}: score={score} fields={nf} methods={nm}")


if __name__ == '__main__':
    main()
