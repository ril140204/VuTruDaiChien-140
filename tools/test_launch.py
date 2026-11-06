#!/usr/bin/env python3
"""
Test launcher: mở game 140 với đúng command line mà QLTK.exe dùng,
để kiểm tra plugin QLTK.Client140.dll:
  - plugin load thành công (không TypeLoadException)
  - parse đúng -autologin
  - kết nối được 127.0.0.1:28082
"""
import base64
import glob
import os
import subprocess
import sys

ROOT = os.environ.get("QLTK_ROOT", r"D:\game\VuTruDaiChien_140")

# !!! KHONG hard-code mat khau trong source.
# Dat qua bien moi truong truoc khi chay:
#   $env:QLTK_USER="<username>"
#   $env:QLTK_PASS="<password>"
#   $env:QLTK_SERVER="Vu Tru 1"
USER = os.environ.get("QLTK_USER", "")
PASS = os.environ.get("QLTK_PASS", "")
SERVER = os.environ.get("QLTK_SERVER", "Vũ Trụ 1")
CHAR = os.environ.get("QLTK_CHAR", "1")
STT = os.environ.get("QLTK_STT", "1")
W = os.environ.get("QLTK_W", "800")
H = os.environ.get("QLTK_H", "600")


def find_game_exe():
    # game exe: tên chứa "Chiến" và .exe, loại trừ QLTK.exe
    cands = []
    for p in glob.glob(os.path.join(ROOT, "*.exe")):
        base = os.path.basename(p)
        if base.upper() == "QLTK.EXE":
            continue
        if base.lower().endswith(".exe"):
            cands.append(p)
    # ưu tiên file có "Đại Chiến" / "Chiến"
    for p in cands:
        if "Chi" in os.path.basename(p):
            return p
    return cands[0] if cands else None


def main():
    if not USER or not PASS:
        print("Thieu QLTK_USER / QLTK_PASS trong environment.")
        print('Vi du: $env:QLTK_USER="abc"; $env:QLTK_PASS="xxx"; python tools\\test_launch.py')
        return 2

    exe = find_game_exe()
    if not exe:
        print("KHONG tim thay game exe")
        return 1

    b64u = base64.b64encode(USER.encode("utf-8")).decode("ascii")
    b64p = base64.b64encode(PASS.encode("utf-8")).decode("ascii")

    args = [
        exe,
        "-autologin", b64u, b64p, CHAR, W, H, "0", SERVER, STT,
        "-proxy", "", "", "", "",
    ]

    print("EXE :", exe)
    print("ARGS:", " ".join(repr(a) for a in args[1:]))
    print("-" * 60)

    p = subprocess.Popen(args, cwd=ROOT)
    print("PID =", p.pid)
    try:
        p.wait()
    except KeyboardInterrupt:
        p.kill()
    return 0


if __name__ == "__main__":
    sys.exit(main())
