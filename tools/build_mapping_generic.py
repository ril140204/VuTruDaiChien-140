#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Mapping obfuscated type: ver132 -> ban 140, bo sung LAN TRUYEN qua generic argument.

Ly do: khi container da duoc map (vd BLCDHGMIHPL -> LIJGOHIGIJH), thi
generic argument cua no cung da bi xac dinh:
    ver132: BLCDHGMIHPL : NLIBNLIJNDA<BPOKMCKCJKJ>
    140   : LIJGOHIGIJH : KDOKFBNKELH<FAEBCOMAKAN>
=> BPOKMCKCJKJ -> FAEBCOMAKAN

Vong lap: sau moi vong mapping, dung mapping vua co de suy ra mapping moi
cho cac generic argument chua biet.
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


def generic_args(base_raw):
    """Trich danh sach generic argument tu base type raw (vd 'A<B, C>')."""
    if not base_raw or '<' not in base_raw:
        return []
    start = base_raw.index('<')
    depth = 0
    args = []
    cur = ''
    for ch in base_raw[start + 1:]:
        if ch == '<':
            depth += 1
            cur += ch
        elif ch == '>':
            if depth == 0:
                break
            depth -= 1
            cur += ch
        elif ch == ',' and depth == 0:
            args.append(cur.strip())
            cur = ''
        else:
            cur += ch
    if cur.strip():
        args.append(cur.strip())
    return args


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
                base_raw = (m.group('base') or '').strip()
                classes[cur] = {
                    'base_raw': base_raw,
                    'base': norm_type(base_raw),
                    'base_name': base_raw.split('<')[0].strip(),
                    'gargs': generic_args(base_raw),
                    'fields': [],
                    'consts': [],
                    'methods': [],
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

    obf_targets = [t for t in typerefs if OBF_RE.match(t)]
    n_obf = {name: c for name, c in n.items() if OBF_RE.match(name)}
    print(f'ver132={len(v)}, 140={len(n)}, obf-targets={len(obf_targets)}, '
          f'140-obf-types={len(n_obf)}\n')

    # ---- indices ----
    idx_const = defaultdict(set)
    idx_field_base = defaultdict(set)
    idx_field = defaultdict(set)
    idx_shape = defaultdict(set)
    idx_counts = defaultdict(set)
    idx_base_name = defaultdict(set)

    for name, c in n_obf.items():
        if c['consts']:
            idx_const[tuple(sorted(c['consts']))].add(name)
        idx_field_base[(norm_type(c['base']), field_key(c))].add(name)
        idx_field[field_key(c)].add(name)
        idx_shape[method_shape(c)].add(name)
        idx_counts[(norm_type(c['base']), len(c['fields']), len(c['methods']))].add(name)
        idx_base_name[c['base_name']].add(name)

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

    def compute_mapping():
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

            sets = [
                ('const', idx_const.get(tuple(sorted(src['consts'])), set())
                 if src['consts'] else None),
                ('field+base', idx_field_base.get((base, fk), set())),
                ('field', idx_field.get(fk, set())),
                ('method-shape', idx_shape.get(mk, set())),
                ('counts', idx_counts.get((base, len(src['fields']),
                                          len(src['methods'])), set())),
                ('member-req',
                 {x for x in n_obf if has_members(x, reqs)} if reqs else None),
            ]

            cur = set(n_obf)
            used = []
            for label, s in sets:
                if s is None:
                    continue
                inter = cur & s
                if not inter:
                    continue
                if len(inter) < len(cur):
                    cur = inter
                    used.append(label)
                if len(cur) == 1:
                    break

            if len(cur) == 1:
                mapping[obf] = next(iter(cur))
                details[obf] = '+'.join(used) or 'unique'
                continue

            if len(cur) > 1:
                sub = [x for x in cur if len(n[x]['fields']) == len(src['fields'])]
                if len(sub) == 1:
                    mapping[obf] = sub[0]
                    details[obf] = '+'.join(used) + '+nfield'
                    continue
                if src['consts']:
                    s2 = [x for x in sub if tuple(sorted(n[x]['consts'])) ==
                          tuple(sorted(src['consts']))]
                    if len(s2) == 1:
                        mapping[obf] = s2[0]
                        details[obf] = '+'.join(used) + '+nfield+const'
                        continue
                s3 = [x for x in sub if norm_type(n[x]['base']) == base]
                if len(s3) == 1:
                    mapping[obf] = s3[0]
                    details[obf] = '+'.join(used) + '+nfield+base'
                    continue
                cur = set(s3 or sub)

            unresolved.append((obf, '+'.join(used) or 'none', sorted(cur)[:10]))
        return mapping, details, unresolved

    mapping, details, unresolved = compute_mapping()
    print(f'=== VONG 1: {len(mapping)} / {len(obf_targets)} ===')

    # ---- lan truyen qua generic argument ----
    for round_no in range(1, 6):
        added = 0
        # map base type name -> type 140 tuong ung
        base_map = {}
        for obf132, obf140 in mapping.items():
            src = v.get(obf132)
            tgt = n.get(obf140)
            if not src or not tgt:
                continue
            if src['base_name'] and tgt['base_name']:
                base_map.setdefault(src['base_name'], set()).add(tgt['base_name'])

        newmap = {}
        for obf132, obf140 in mapping.items():
            src = v.get(obf132)
            tgt = n.get(obf140)
            if not src or not tgt:
                continue
            g1 = src['gargs']
            g2 = tgt['gargs']
            if len(g1) != len(g2):
                continue
            for a1, a2 in zip(g1, g2):
                a1 = a1.split('<')[0].strip()
                a2 = a2.split('<')[0].strip()
                if OBF_RE.match(a1) and OBF_RE.match(a2) and a1 != a2:
                    if a1 not in mapping and a1 in obf_targets:
                        newmap[a1] = a2
                        added += 1

        # them: neu container co base generic bi doi ten, suy ra tu mapping base
        if not added:
            # thu suy ra qua "generic base class"
            for obf132, obf140 in mapping.items():
                src = v.get(obf132)
                tgt = n.get(obf140)
                if not src or not tgt:
                    continue
                b1 = src['base_name']
                b2 = tgt['base_name']
                if not b1 or not b2 or b1 == b2:
                    continue
                cands = base_map.get(b1)
                if not cands:
                    continue
                # class 132 extends generic base G<A>; class 140 extends G'<B>
                for g1 in src['gargs']:
                    for g2 in tgt['gargs']:
                        a1 = g1.split('<')[0].strip()
                        a2 = g2.split('<')[0].strip()
                        if OBF_RE.match(a1) and OBF_RE.match(a2) and a1 != a2:
                            if a1 not in mapping and a1 in obf_targets:
                                newmap[a1] = a2
                                added += 1

        if not added:
            print(f'  vong {round_no}: khong co mapping moi')
            break
        mapping.update(newmap)
        print(f'  vong {round_no}: +{added} mapping  (tong {len(mapping)})')
        # recompute unresolved
        _, details2, unresolved = compute_mapping()
        details.update(details2)
        # giu mapping da biet
        for obf, hit in mapping.items():
            if obf not in details:
                details[obf] = 'generic-propagation'

    print(f'\n=== FINAL: {len(mapping)} / {len(obf_targets)} ===')
    for k in sorted(mapping):
        print(f'  {k} -> {mapping[k]}   [{details.get(k, "?")}]')

    _, _, unresolved = compute_mapping()
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
