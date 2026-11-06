#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Patch metadata string trong Client.dll: doi ten type obfuscate cua ban ver132
sang ten tuong duong cua ban game 140.

Ly do: obfuscator sinh ten co dinh do dai (VD: 11 ky tu), nen ta co the
thay the CHUOI trong #Strings heap truc tiep ma van giu nguyen metadata
va khong can build lai assembly.

Chi ghi de khi chuoi moi CUNG do dai voi chuoi cu (de khong lam lech
offset trong metadata stream).
"""
import io
import json
import shutil
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

CLIENT_DLL = Path(r"D:\game\VuTruDaiChien_140\BepInEx\plugins\Client.dll")
MAPPING_FILE = Path(r"D:\game\VuTruDaiChien_140\tools\type_mapping.json")


def patch(data: bytes, mapping: dict, dry_run=True):
    """Thay the chuoi trong data. Tra ve (data_moi, danh_sach_thay_the)."""
    changes = []
    out = bytearray(data)

    for old, new in mapping.items():
        if old == new:
            continue
        if len(old.encode()) != len(new.encode()):
            changes.append((old, new, 'SKIP-length-mismatch', 0))
            continue

        old_b = old.encode('ascii')
        new_b = new.encode('ascii')

        count = 0
        pos = 0
        while True:
            idx = data.find(old_b, pos)
            if idx < 0:
                break
            # chi thay trong #Strings heap: sau byte name nen den \x00
            if idx + len(old_b) < len(data) and data[idx + len(old_b)] == 0:
                out[idx:idx + len(old_b)] = new_b
                count += 1
            pos = idx + 1

        changes.append((old, new, 'OK' if count else 'NOT-FOUND', count))

    return bytes(out), changes


def main():
    mapping = json.loads(MAPPING_FILE.read_text(encoding='utf-8'))
    # chi giu mapping co gia tri la ten hop le
    clean = {}
    for k, v in mapping.items():
        v = v.split('  ')[0].strip()
        if v and v != k:
            clean[k] = v

    print(f"mapping entries: {len(clean)}")
    for k, v in sorted(clean.items()):
        print(f"  {k} -> {v}")

    data = CLIENT_DLL.read_bytes()
    print(f"\nClient.dll size: {len(data)}")

    new_data, changes = patch(data, clean, dry_run=True)

    print(f"\n=== PATCH PREVIEW ===")
    for old, new, status, count in changes:
        print(f"  {old} -> {new}: {status} ({count} occurrence(s))")

    applied = [c for c in changes if c[2] == 'OK']
    if not applied:
        print("\nNothing to patch.")
        return

    if '--apply' in sys.argv:
        backup = CLIENT_DLL.with_suffix('.dll.bak')
        if not backup.exists():
            shutil.copy2(CLIENT_DLL, backup)
            print(f"backup: {backup}")

        CLIENT_DLL.write_bytes(new_data)
        print(f"\nAPPLIED. {len(applied)} type(s) patched -> {CLIENT_DLL}")
        print(f"new size: {len(new_data)} (delta {len(new_data) - len(data)})")
    else:
        print("\n(dry-run. add --apply to write)")


if __name__ == '__main__':
    main()
