#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Mapping obfuscated type: ver132 -> ban 140 (final, co phep GIAO dung).

Moi tin hieu tao ra mot TAP UNG VIEN (candidate set), va cac tap duoc GIAO
nhau theo thu tu uu tien. Tin hieu manh nhat dat truoc.

Tin hieu:
  1. CONST VALUES            - gia tri const giong het (gia tri khong doi giua build)
  2. FIELD LAYOUT + BASE     - (offset, kieu field) + base type
  3. FIELD LAYOUT            - (offset, kieu field)
  4. METHOD SHAPE            - (kieu tra ve, so tham so, static, virtual) da sort
  5. MEMBER REQUIREMENTS     - Client.dll goi type nay voi N member cu the
                              (type 140 phai co member cung kieu tra ve + so tham so)

Ket qua kiem chung bang cap da biet dung: DJLADAKKHMP -> BBOIDGFADKJ
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
KNOWN = {'DJLADAKKHMP': 'BBOIDGFADKJ'}

CLASS_RE = re.compile(
    r'^public\s+(?P<mods>(?:static\s+|abstract\s+|sealed\s+|partial\s+)*)class\s+'
    r'(?P<name>[\w/`<>]+)\s*(?::\s*(?P<base>[^{]+?))?\s*'
    r'(?://\s*TypeDefIndex:\s*(?P<tdi>\d+))?\s*$'
)
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
    r'(?P<mods>(?:static\s+|unsafe\s+|virtual\s+|override\s+|abstract\s+|sealed\s+|new\s+)*)'
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
                cur = full.split('/')[-1].split('`')[0]
                classes[cur] = {
                    'base': norm_type(m.group('base') or ''),
                    'fields': [],
                    'consts': [],
                    'methods': [],
                    'tdi': m.group('tdi'),
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
                mods = mm.group('mods') or ''
                classes[cur]['methods'].append({
                    'ret': norm_type(mm.group('ret')),
                    'nargs': nargs,
                    'static': 'static' in mods,
                    'virt': 'virtual' in mods or 'override' in mods,
                })
                continue

            mf = FIELD_RE.match(line)
            if mf and 'class ' not in s and 'struct ' not in s and 'enum ' not in s:
                classes[cur]['fields'].append({
                    'type': norm_type(mf.group('type')),
                    'off': mf.group('off') or '',
                })
                continue
    return classes


def field_key(c):
    return tuple(sorted(((f['off'], f['type']) for f in c['fields']),
                        key=lambda x: (x[0] == '', x[0])))


def method_shape(c):
    return tuple(sorted((m['ret'], m['nargs'], m['static'], m['virt'])
                        for m in c['methods']))


def parse_req(sig):
    """'M System.Int32 get_X()' -> (kind, retNorm, nargs)"""
    m = re.match(r'^([MFX])\s+(\S+)\s+([^\(]*)\((.*)\)$', sig.strip())
    if not m:
        return None
    kind, ret, _name, args = m.groups()
    nargs = 0 if not args.strip() else len([a for a in args.split(',') if a.strip()])
    return (kind, norm_type(ret), nargs)


def main():
    v = parse(V132)
    n = parse(N140)
    refs = json.loads(REFS.read_text(encoding='utf-8'))
    typerefs = refs.get('typeRefs', [])
    members = refs.get('members', {})

    print(f'ver132={len(v)} classes, 140={len(n)} classes, typerefs={len(typerefs)}')
    obf_targets = [t for t in typerefs if OBF_RE.match(t)]
    print(f'obfuscated targets: {len(obf_targets)}\n')

    # ---- index 140: CHI type obfuscated ----
    idx_const = defaultdict(set)
    idx_field_base = defaultdict(set)
    idx_field = defaultdict(set)
    idx_shape = defaultdict(set)
    idx_counts = defaultdict(set)

    n_obf = {name: c for name, c in n.items() if OBF_RE.match(name)}
    for name, c in n_obf.items():
        if c['consts']:
            idx_const[tuple(sorted(c['consts']))].add(name)
        idx_field_base[(norm_type(c['base']), field_key(c))].add(name)
        idx_field[field_key(c)].add(name)
        idx_shape[method_shape(c)].add(name)
        idx_counts[(norm_type(c['base']), len(c['fields']), len(c['methods']))].add(name)

    print(f'140 obfuscated types: {len(n_obf)}\n')

    def has_members(name, reqs):
        c = n[name]
        for kind, ret, nargs in reqs:
            if kind == 'M':
                if not any(m['ret'] == ret and m['nargs'] == nargs
                           for m in c['methods']):
                    return False
            elif kind == 'F':
                if not any(f['type'] == ret for f in c['fields']):
                    return False
        return True

    mapping = {}
    details = {}
    unresolved = []

    for obf in sorted(obf_targets):
        src = v.get(obf)
        if src is None:
            unresolved.append((obf, 'not-in-132-dump', []))
            continue

        reqs = [r for r in (parse_req(s) for s in members.get(obf, [])) if r]
        base = norm_type(src['base'])
        fk = field_key(src)
        mk = method_shape(src)

        # cac tap ung vien theo tin hieu
        s_const = idx_const.get(tuple(sorted(src['consts'])), set()) if src['consts'] else None
        s_fb = idx_field_base.get((base, fk), set())
        s_f = idx_field.get(fk, set())
        s_m = idx_shape.get(mk, set())
        s_c = idx_counts.get((base, len(src['fields']), len(src['methods'])), set())
        s_mem = {name for name in n_obf if has_members(name, reqs)} if reqs else None

        # ap dung lan luot, GIAO don dan
        cur = set(n_obf)
        used = []
        for label, s in (('const', s_const), ('field+base', s_fb), ('field', s_f),
                         ('method-shape', s_m), ('counts', s_c), ('member-req', s_mem)):
            if s is None:
                continue
            inter = cur & s
            if not inter:
                continue          # tin hieu nay khong giup - bo qua
            if len(inter) < len(cur):
                cur = inter
                used.append(label)
            if len(cur) == 1:
                break

        if len(cur) == 1:
            hit = next(iter(cur))
            mapping[obf] = hit
            details[obf] = '+'.join(used) or 'unique'
            continue

        # van >1: dung them - so field, const, base
        if len(cur) > 1:
            sub = [x for x in cur if len(n[x]['fields']) == len(src['fields'])]
            if len(sub) == 1:
                mapping[obf] = sub[0]
                details[obf] = '+'.join(used) + '+nfield'
                continue
            if src['consts']:
                sub2 = [x for x in sub if tuple(sorted(n[x]['consts'])) ==
                        tuple(sorted(src['consts']))]
                if len(sub2) == 1:
                    mapping[obf] = sub2[0]
                    details[obf] = '+'.join(used) + '+nfield+const'
                    continue
            sub3 = [x for x in sub if norm_type(n[x]['base']) == base]
            if len(sub3) == 1:
                mapping[obf] = sub3[0]
                details[obf] = '+'.join(used) + '+nfield+base'
                continue
            cur = set(sub3 or sub)

        unresolved.append((obf, '+'.join(used) or 'none', sorted(cur)[:10]))

    print(f'=== RESOLVED: {len(mapping)} / {len(obf_targets)} ===')
    for k in sorted(mapping):
        print(f'  {k} -> {mapping[k]}   [{details.get(k, "?")}]')

    print(f'\n=== UNRESOLVED: {len(unresolved)} ===')
    for o, why, cands in unresolved:
        print(f'  {o}: {why}')
        print(f'      {cands}')

    print('\n=== CHECK vs known pairs ===')
    ok = 0
    for old, new in KNOWN.items():
        got = mapping.get(old)
        m = got == new
        ok += 1 if m else 0
        print(f'  {old}: expected {new}, got {got}  [{"MATCH" if m else "MISMATCH"}]')
    print(f'  -> {ok}/{len(KNOWN)} verified')

    OUT.write_text(json.dumps(mapping, indent=2, ensure_ascii=False),
                   encoding='utf-8')
    print(f'\nSaved {len(mapping)} -> {OUT}')


if __name__ == '__main__':
    main()
