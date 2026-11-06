#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
QA TestBot Launcher
Quản lý vòng đời khởi chạy nhiều tài khoản, kiểm soát tải, whitelist server và dừng khẩn cấp.
"""

import os
import sys
import json
import time
import signal
import subprocess
from pathlib import Path

# Thư mục gốc dự án
BASE_DIR = Path(__file__).resolve().parent.parent.parent
CONFIG_DIR = BASE_DIR / "QA_TestBot" / "config"
OUTPUT_DIR = BASE_DIR / "QA_TestBot" / "output"
GAME_EXE = BASE_DIR / "Vũ Trụ Đại Chiến.exe"
STOP_FLAG = CONFIG_DIR / "stop.flag"

# Windows console mac dinh la cp1258 -> khong in duoc mot so chu Viet Nam.
# Chuyen stdout sang UTF-8 va bo qua loi encode thay vi crash.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

active_processes = []
is_shutting_down = False

def handle_exit(signum=None, frame=None):
    global is_shutting_down
    if is_shutting_down:
        return
    is_shutting_down = True
    print("\n[LAUNCHER] Nhan tin hieu dung (Ctrl+C hoac Stop Flag). Dang dung tat ca client...")
    stop_all_clients()
    sys.exit(0)

def stop_all_clients():
    global active_processes
    start_stop_time = time.time()
    print(f"[LAUNCHER] Dang terminate {len(active_processes)} tien trinh dang chay (gioi han < 10s)...")

    for item in active_processes:
        proc = item.get("proc")
        account = item.get("account")
        if proc and proc.poll() is None:
            try:
                proc.terminate()
                print(f"  -> Da gui terminate toi client: {account} (PID: {proc.pid})")
            except Exception as e:
                print(f"  -> Loi khi terminate {account}: {e}")

    # Cho toi da 5 giay de dong em diu
    deadline = time.time() + 5.0
    while time.time() < deadline:
        alive = [item for item in active_processes if item["proc"].poll() is None]
        if not alive:
            break
        time.sleep(0.5)

    # Neu van con song thi kill thang tay
    for item in active_processes:
        proc = item.get("proc")
        if proc and proc.poll() is None:
            try:
                proc.kill()
                print(f"  -> Kill cuong buc PID: {proc.pid}")
            except Exception:
                pass

    elapsed = time.time() - start_stop_time
    print(f"[LAUNCHER] Hoan tat dung toan bo tien trinh trong {elapsed:.2f} giay.")

def load_config():
    config_file = CONFIG_DIR / "bot_config.json"
    if not config_file.exists():
        print(f"[ERROR] Khong tim thay file cau hinh: {config_file}")
        sys.exit(1)

    with open(config_file, "r", encoding="utf-8") as f:
        config = json.load(f)

    # 1. Kiem tra gioi han cung: test_env_confirmed
    if not config.get("test_env_confirmed", False):
        print("\n=======================================================")
        print("[CRITICAL] TU CHOI CHAY: 'test_env_confirmed' dang la false!")
        print("Vui long xac nhan moi truong kiem thu truoc khi bat dau.")
        print("=======================================================\n")
        sys.exit(1)

    # 2. Kiem tra whitelist server
    server_name = config.get("server_name", "").strip()
    allowed_servers = config.get("allowed_server_names", [])
    if not server_name or server_name not in allowed_servers:
        print("\n=======================================================")
        print(f"[CRITICAL] TU CHOI CHAY: Server '{server_name}' khong nam trong whitelist!")
        print(f"Danh sach allowed_server_names: {allowed_servers}")
        print("=======================================================\n")
        sys.exit(1)

    return config

def load_accounts():
    accounts_file = CONFIG_DIR / "accounts.json"
    if not accounts_file.exists():
        print(f"[ERROR] Khong tim thay file danh sach tai khoan: {accounts_file}")
        sys.exit(1)

    with open(accounts_file, "r", encoding="utf-8") as f:
        accounts = json.load(f)

    if not isinstance(accounts, list) or len(accounts) == 0:
        print("[ERROR] Danh sach accounts.json rong!")
        sys.exit(1)

    return accounts

def prepare_account_config(base_config, account_info):
    username = account_info["username"]
    account_out_dir = OUTPUT_DIR / username
    account_out_dir.mkdir(parents=True, exist_ok=True)

    acc_config = dict(base_config)
    acc_config["username"] = username
    acc_config["password"] = account_info["password"]
    acc_config["log_dir"] = str(OUTPUT_DIR.resolve())

    config_path = account_out_dir / "bot_account_config.json"
    with open(config_path, "w", encoding="utf-8") as f:
        json.dump(acc_config, f, indent=2, ensure_ascii=False)

    return config_path

def main():
    global active_processes, is_shutting_down

    signal.signal(signal.SIGINT, handle_exit)
    signal.signal(signal.SIGTERM, handle_exit)

    print("=" * 60)
    print("        QA TESTBOT LAUNCHER - VU TRU DAI CHIEN")
    print("=" * 60)

    # Kiem tra file Game.exe
    if not GAME_EXE.exists():
        print(f"[ERROR] Khong tim thay executable game tai: {GAME_EXE}")
        sys.exit(1)

    # Xoa stop.flag cu neu co de bat dau phien moi
    if STOP_FLAG.exists():
        print(f"[WARN] Phat hien {STOP_FLAG} cu. Dang xoa de bat dau phien test moi.")
        STOP_FLAG.unlink()

    config = load_config()
    accounts = load_accounts()

    max_concurrent = int(config.get("max_concurrent", 1))
    stagger_seconds = float(config.get("stagger_seconds", 6.0))
    server_name = config.get("server_name")

    print(f"[*] Muc tieu: {len(accounts)} tai khoan vao server: [{server_name}]")
    print(f"[*] Gioi han dong thoi (max_concurrent): {max_concurrent}")
    print(f"[*] Gian cach chay giua cac client (stagger_seconds): {stagger_seconds}s")
    print(f"[*] Thư mục log dau ra: {OUTPUT_DIR}")
    print("-" * 60)

    pending_accounts = list(accounts)

    while (pending_accounts or active_processes) and not is_shutting_down:
        # Kiem tra co dung khan cap stop.flag
        if STOP_FLAG.exists():
            print(f"\n[ALERT] Phat hien file dung khan cap: {STOP_FLAG}!")
            handle_exit()
            break

        # Don dep cac process da ket thuc
        still_running = []
        for item in active_processes:
            proc = item["proc"]
            account = item["account"]
            ret = proc.poll()
            if ret is None:
                still_running.append(item)
            else:
                print(f"[INFO] Client '{account}' (PID: {proc.pid}) da ket thuc (exit code: {ret}).")
        active_processes = still_running

        # Khoi chay tai khoan moi neu con slot trong
        if pending_accounts and len(active_processes) < max_concurrent:
            account_info = pending_accounts.pop(0)
            username = account_info["username"]

            acc_cfg_path = prepare_account_config(config, account_info)

            # Thiet lap bien moi truong rieng cho client
            child_env = os.environ.copy()
            child_env["TESTBOT_ACCOUNT_FILE"] = str(acc_cfg_path.resolve())

            print(f"[START] Khoi dong client cho tai khoan: [{username}]...")
            try:
                proc = subprocess.Popen(
                    [str(GAME_EXE.resolve())],
                    cwd=str(BASE_DIR.resolve()),
                    env=child_env,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL
                )
                active_processes.append({
                    "account": username,
                    "proc": proc,
                    "start_time": time.time()
                })
                print(f"  -> Client da chay, PID: {proc.pid}. Dang doi gian cach {stagger_seconds}s...")

                # Gian cach stagger de khong lam nghen socket/server
                time.sleep(stagger_seconds)
            except Exception as e:
                print(f"[ERROR] Khong the khoi dong client cho '{username}': {e}")

        time.sleep(0.5)

    print("\n[LAUNCHER] Tat ca cac tai khoan da hoan thanh hoac da dung.")

if __name__ == "__main__":
    main()
