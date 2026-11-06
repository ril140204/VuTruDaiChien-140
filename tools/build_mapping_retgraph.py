#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Mapping obfuscated type: ver132 -> ban 140, dung QUAN HE KIEM TRAU VE (return-type graph).

Y tuong: Client.dll goi cac member co kieu tra ve la TYPE cu the. Vi du:
    GameManager.get_BAHMMHAIGKE() : NOACOHGAEHH
Neu biet GameManager -> <type 140>, va biet type 140 co method tra ve
<gi do>, thi NOACOHGAEHH phai la type 140 do.

Tin hieu moi: "co method tra ve T" (chi can kieu, khong can ten).
Ket hop voi cac tin hieu hinh hoc (field layout, const, method shape) va
phép GIAO de giai quyet.

Ngoai ra: lan truyen qua generic argument + qua field kieu generic.
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
    if not base_raw or '<' not in base_raw:
        return []
    start = base_raw.index('<')
    depth = 0
    args, cur = [], ''
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
                    'rets': set(),
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
                ret = norm_type(mm.group('ret'))
                classes[cur]['methods'].append({
                    'ret': ret, 'nargs': nargs,
                    'static': 'static' in mods,
                    'virt': 'virtual' in mods or 'override' in mods,
                })
                # ghi lai kieu tra ve GOC (chua normalize) de dung cho return-graph
                raw_ret = mm.group('ret').strip()
                raw_ret = ' '.join(x for x in raw_ret.split() if x not in MODS)
                classes[cur]['rets'].add(raw_ret)
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
    return (kind, ret.strip(), nargs)


def main():
    v = parse(V132)
    n = parse(N140)
    refs = json.loads(REFS.read_text(encoding='utf-8'))
    typerefs = refs.get('typeRefs', [])
    members = refs.get('members', {})

    obf_targets = [t for t in typerefs if OBF_RE.match(t)]
    n_obf = {name: c for name, c in n.items() if OBF_RE.match(name)}
    print(f'ver132={len(v)}, 140={len(n)}, obf-targets={len(obf_targets)}, '
          f'140-obf={len(n_obf)}\n')

    # ---- indices ----
    idx_const = defaultdict(set)
    idx_field_base = defaultdict(set)
    idx_field = defaultdict(set)
    idx_shape = defaultdict(set)
    idx_counts = defaultdict(set)
    # index theo kieu tra ve:  idx_ret[retType] = set(type co method tra ve retType)
    idx_ret = defaultdict(set)
    for name, c in n_obf.items():
        if c['consts']:
            idx_const[tuple(sorted(c['consts']))].add(name)
        idx_field_base[(norm_type(c['base']), field_key(c))].add(name)
        idx_field[field_key(c)].add(name)
        idx_shape[method_shape(c)].add(name)
        idx_counts[(norm_type(c['base']), len(c['fields']), len(c['methods']))].add(name)
        for r in c['rets']:
            idx_ret[r].add(name)

    # Client.dll goi member nao cua type nao -> return-type requirements
    # members[type] = ['M <ret> name(args)']
    req_ret = {}
    for t, sigs in members.items():
        rs = []
        for s in sigs:
            r = parse_req(s)
            if r and r[0] == 'M':
                rs.append(r)
        req_ret[t] = rs

    def has_members(name, reqs):
        """type 140 'name' co cac member voi (kieu tra ve, so tham so) tuong ung?"""
        c = n[name]
        for kind, ret, nargs in reqs:
            if kind == 'M':
                if not any(m['ret'] == norm_type(ret) and m['nargs'] == nargs
                           for m in c['methods']):
                    return False
            elif kind == 'F':
                if not any(f['type'] == norm_type(ret) for f in c['fields']):
                    return False
        return True

    def compute(mapping):
        """mapping: dict 132->140 da biet. Tra ve mapping moi + unresolved."""
        result = {}
        details = {}
        unresolved = []
        for obf in sorted(obf_targets):
            src = v.get(obf)
            if src is None:
                unresolved.append((obf, 'not-in-132-dump', []))
                continue
            reqs = req_ret.get(obf, [])
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

            # ---- return-type graph: type 140 phai co method tra ve DUNG type
            #      ma Client.dll goi (do da biet mapping cho type do)
            if len(cur) > 1:
                known140 = {mapping[o] for o in mapping if o in obf_targets}
                ret_cands = set()
                for kind, ret, nargs in reqs:
                    if kind != 'M':
                        continue
                    # ret co the la type 132 da biet -> doi sang 140
                    targets = {ret}
                    if ret in mapping:
                        targets.add(mapping[ret])
                    for tg in targets:
                        ret_cands |= idx_ret.get(tg, set())
                    # neu khong doi, ret co the la type 140 luon
                    if ret in n_obf:
                        ret_cands |= idx_ret.get(ret, set())
                inter = cur & ret_cands
                if inter:
                    cur = inter
                    used.append('ret-graph')

            if len(cur) == 1:
                result[obf] = next(iter(cur))
                details[obf] = '+'.join(used) or 'unique'
                continue

            if len(cur) > 1:
                sub = [x for x in cur if len(n[x]['fields']) == len(src['fields'])]
                if len(sub) == 1:
                    result[obf] = sub[0]
                    details[obf] = '+'.join(used) + '+nfield'
                    continue
                if src['consts']:
                    s2 = [x for x in sub if tuple(sorted(n[x]['consts'])) ==
                          tuple(sorted(src['consts']))]
                    if len(s2) == 1:
                        result[obf] = s2[0]
                        details[obf] = '+'.join(used) + '+nfield+const'
                        continue
                s3 = [x for x in sub if norm_type(n[x]['base']) == base]
                if len(s3) == 1:
                    result[obf] = s3[0]
                    details[obf] = '+'.join(used) + '+nfield+base'
                    continue
                cur = set(s3 or sub)

            unresolved.append((obf, '+'.join(used) or 'none', sorted(cur)[:12]))
        return result, details, unresolved

    mapping, details, unresolved = compute({})
    print(f'=== VONG 1: {len(mapping)} / {len(obf_targets)} ===')

    # ---- vong lap: lan truyen generic + ret-graph ----
    for rnd in range(1, 8):
        added = 0
        newmap = {}
        for obf132, obf140 in list(mapping.items()):
            src = v.get(obf132)
            tgt = n.get(obf140)
            if not src or not tgt:
                continue
            if len(src['gargs']) == len(tgt['gargs']):
                for a1, a2 in zip(src['gargs'], tgt['gargs']):
                    a1 = a1.split('<')[0].strip()
                    a2 = a2.split('<')[0].strip()
                    if OBF_RE.match(a1) and OBF_RE.match(a2) and a1 != a2:
                        if a1 not in mapping and a1 in obf_targets:
                            newmap[a1] = a2
                            added += 1
        if added:
            mapping.update(newmap)
            print(f'  vong {rnd} (generic): +{added}  -> tong {len(mapping)}')
            continue

        # khong co generic moi -> dung ret-graph voi mapping hien tai
        newres, newdet, unresolved = compute(mapping)
        added = 0
        for k, val in newres.items():
            if k not in mapping:
                mapping[k] = val
                details[k] = newdet.get(k, 'ret-graph')
                added += 1
        if not added:
            print(f'  vong {rnd}: khong co mapping moi')
            break
        print(f'  vong {rnd} (ret-graph): +{added} -> tong {len(mapping)}')

    # recompute final
    _r, _d, unresolved = compute(mapping)
    for k, val in _r.items():
        if k not in mapping:
            mapping[k] = val
            details[k] = _d.get(k, '?')
    for k in mapping:
        details.setdefault(k, 'generic-propagation')

    print(f'\n=== FINAL: {len(mapping)} / {len(obf_targets)} ===')
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
