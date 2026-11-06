#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
QA TestBot Dashboard - Server
API HTTP + static file server cho trang quan ly tai khoan QA.

Chay duy nhat:
    python QA_TestBot/dashboard/dashboard.py

Mo trinh duyet tai: http://127.0.0.1:8766/
"""

import csv
import json
import os
import re
import shutil
import subprocess
import sys
import threading
import time
import webbrowser
from collections import Counter, defaultdict
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse, parse_qs

BASE_DIR = Path(__file__).resolve().parent.parent.parent
QA_DIR = BASE_DIR / "QA_TestBot"
CONFIG_DIR = QA_DIR / "config"
OUTPUT_DIR = QA_DIR / "output"
DASHBOARD_DIR = Path(__file__).resolve().parent
STATIC_DIR = DASHBOARD_DIR / "static"
STOP_FLAG = CONFIG_DIR / "stop.flag"
GAME_EXE = BASE_DIR / "Vũ Trụ Đại Chiến.exe"

HOST = "127.0.0.1"
PORT = 8766

# Windows console mac dinh la cp1258 -> khong in duoc mot so chu Viet Nam.
# Chuyen stdout sang UTF-8 va bo qua loi encode thay vi crash.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

# Trang thai launcher da chay
# RLock (re-entrant) bat buoc: start_launcher() giu khoa roi goi launcher_running()
# cung giu khoa -> dung Lock se tao deadlock va treo toan bo server.
_launcher_process = None
_launcher_lock = threading.RLock()

# Cache ket qua quet launcher de khong spawn PowerShell o moi request polling.
_launcher_scan_cache = {"pids": [], "at": 0.0}


# ----------------------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------------------
def read_json(path: Path, default):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return default


def write_json(path: Path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
    tmp.replace(path)


def load_bot_config():
    return read_json(CONFIG_DIR / "bot_config.json", {})


def save_bot_config(cfg):
    write_json(CONFIG_DIR / "bot_config.json", cfg)


def load_accounts():
    accounts = read_json(CONFIG_DIR / "accounts.json", [])
    return accounts if isinstance(accounts, list) else []


def save_accounts(accounts):
    write_json(CONFIG_DIR / "accounts.json", accounts)


def parse_events(file_path: Path):
    """Phan tich events.csv cua mot tai khoan."""
    info = {
        "exists": file_path.exists(),
        "success": False,
        "duration": None,
        "last_state": "NotRun",
        "events_count": 0,
        "errors": [],
        "last_activity": None,
    }
    if not file_path.exists():
        return info

    last_state = "Unknown"
    try:
        with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
            for row in csv.DictReader(f):
                info["events_count"] += 1
                state = row.get("state", last_state)
                last_state = state
                event_type = row.get("event_type", "")
                msg = row.get("message", "")
                info["last_activity"] = row.get("timestamp", "")

                if state == "InGame" or "LOGIN_SUCCESS" in msg:
                    info["success"] = True

                if event_type == "METRIC" and "LOGIN_SUCCESS" in msg:
                    m = re.search(r"([\d.]+)\s*seconds", msg)
                    if m:
                        try:
                            info["duration"] = float(m.group(1))
                        except ValueError:
                            pass

                if event_type in ("ERROR", "TIMEOUT"):
                    info["errors"].append(f"[{state}] {msg}")

                if event_type == "SNAPSHOT" and "objects recorded" in msg:
                    m = re.search(r"(\d+)\s+objects recorded", msg)
                    if m:
                        info["snapshot_objects"] = int(m.group(1))
    except Exception:
        pass

    info["last_state"] = last_state
    return info


def parse_snapshots(file_path: Path):
    """Thong ke snapshots.csv: tong so ban ghi + phan bo loai doi tuong."""
    result = {"total_records": 0, "ticks": 0, "by_type": {}}
    if not file_path.exists():
        return result

    times = set()
    counter = Counter()
    try:
        with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
            for row in csv.DictReader(f):
                result["total_records"] += 1
                t = row.get("time", "")
                obj_type = row.get("type", "Unknown")
                if t:
                    times.add(t)
                counter[obj_type] += 1
    except Exception:
        pass

    result["ticks"] = len(times)
    result["by_type"] = dict(counter)
    return result


def account_status():
    """Tong hop trang thai tat ca tai khoan da biet."""
    accounts = load_accounts()
    known = {a.get("username", "") for a in accounts if isinstance(a, dict)}

    # Account da chay nhung da bi xoa khoi config -> van hien thi de khong mat du lieu.
    if OUTPUT_DIR.exists():
        for d in OUTPUT_DIR.iterdir():
            if d.is_dir() and d.name not in known:
                known.add(d.name)

    result = []
    for name in sorted(known):
        if not name:
            continue
        acc_dir = OUTPUT_DIR / name
        events = parse_events(acc_dir / "events.csv")
        snaps = parse_snapshots(acc_dir / "snapshots.csv")
        in_config = any(
            isinstance(a, dict) and a.get("username") == name for a in accounts
        )
        result.append(
            {
                "username": name,
                "in_config": in_config,
                "events": events,
                "snapshots": snaps,
            }
        )
    return result


def tail_events(account: str, limit: int = 200):
    path = OUTPUT_DIR / account / "events.csv"
    if not path.exists():
        return []
    rows = []
    try:
        with open(path, "r", encoding="utf-8", errors="ignore") as f:
            for row in csv.DictReader(f):
                rows.append(row)
    except Exception:
        return []
    return rows[-limit:]


# ----------------------------------------------------------------------------
# Launcher control
# ----------------------------------------------------------------------------
def _launcher_pids():
    """Tim PID cua tat ca launcher.py dang chay (ke ca ban khoi dong tu terminal).

    Dung Get-CimInstance de lay CommandLine cua moi process python.exe.
    """
    try:
        proc = subprocess.run(
            [
                "powershell",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | "
                "ForEach-Object { \"$($_.ProcessId)`t$($_.CommandLine)\" }",
            ],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=25,
        )
    except Exception:
        return []

    pids = []
    for line in (proc.stdout or "").splitlines():
        if "launcher.py" not in line:
            continue
        head = line.split("\t", 1)[0].strip()
        try:
            pids.append(int(head))
        except ValueError:
            continue
    return pids


def launcher_running():
    with _launcher_lock:
        if _launcher_process is not None and _launcher_process.poll() is None:
            return True
        # Launcher co the da duoc chay truc tiep tu terminal -> quet tien trinh.
        return bool(_scan_launcher_pids())


def _scan_launcher_pids():
    """Quet PID launcher.py, cache 3 giay de giam tai khoa len server."""
    now = time.time()
    with _launcher_lock:
        if now - _launcher_scan_cache["at"] < 3.0:
            return list(_launcher_scan_cache["pids"])
    pids = _launcher_pids()
    with _launcher_lock:
        _launcher_scan_cache["pids"] = pids
        _launcher_scan_cache["at"] = now
    return pids


def start_launcher():
    global _launcher_process
    with _launcher_lock:
        if launcher_running():
            return False, "Launcher dang chay roi."
        cfg = load_bot_config()
        if not cfg.get("test_env_confirmed", False):
            return False, "test_env_confirmed dang la false - he thong se tu choi chay."
        if not GAME_EXE.exists():
            return False, f"Khong tim thay game executable: {GAME_EXE}"
        if not load_accounts():
            return False, "Danh sach tai khoan rong."

        # Xoa stop.flag cu (neu co) de bat dau phien moi.
        if STOP_FLAG.exists():
            STOP_FLAG.unlink()

        script = QA_DIR / "launcher" / "launcher.py"
        log_path = OUTPUT_DIR / "launcher.log"
        log_path.parent.mkdir(parents=True, exist_ok=True)
        log_file = open(log_path, "a", encoding="utf-8", buffering=1)
        log_file.write(f"\n===== [{time.strftime('%Y-%m-%d %H:%M:%S')}] launcher started by dashboard =====\n")

        _launcher_process = subprocess.Popen(
            [sys.executable, str(script)],
            cwd=str(BASE_DIR),
            stdout=log_file,
            stderr=subprocess.STDOUT,
            encoding="utf-8",
            errors="replace",
            creationflags=getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0),
        )
        return True, f"Launcher da khoi dong (PID: {_launcher_process.pid})."


def stop_launcher():
    """Dung khan cap: tao stop.flag -> launcher tu dong terminate moi client."""
    CONFIG_DIR.mkdir(parents=True, exist_ok=True)
    STOP_FLAG.touch()
    return True, "Da tao stop.flag. Launcher se dung tat ca client trong < 10 giay."


def kill_launcher():
    """Buoc dung launcher (fallback khi stop.flag khong duoc xu ly)."""
    global _launcher_process
    with _launcher_lock:
        proc = _launcher_process
        if proc is None or proc.poll() is not None:
            return False, "Launcher khong chay."
        try:
            proc.terminate()
            _launcher_process = None
            return True, f"Da terminate launcher (PID: {proc.pid})."
        except Exception as e:
            return False, f"Loi khi terminate launcher: {e}"


# ----------------------------------------------------------------------------
# HTTP Handler
# ----------------------------------------------------------------------------
class DashboardHandler(BaseHTTPRequestHandler):
    server_version = "QATestBotDashboard/1.0"

    def log_message(self, fmt, *args):
        # Giu console terminal sach.
        pass

    # -- helpers ------------------------------------------------------------
    def _send_json(self, payload, status=200):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _send_file(self, path: Path):
        if not path.exists() or not path.is_file():
            self.send_error(404, "Not Found")
            return
        ctype = {
            ".html": "text/html; charset=utf-8",
            ".css": "text/css; charset=utf-8",
            ".js": "application/javascript; charset=utf-8",
            ".json": "application/json; charset=utf-8",
            ".png": "image/png",
            ".jpg": "image/jpeg",
            ".svg": "image/svg+xml",
            ".ico": "image/x-icon",
        }.get(path.suffix.lower(), "application/octet-stream")
        body = path.read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _read_body(self):
        length = int(self.headers.get("Content-Length", 0) or 0)
        if length <= 0:
            return {}
        try:
            return json.loads(self.rfile.read(length).decode("utf-8"))
        except Exception:
            return {}

    # -- routing ------------------------------------------------------------
    def do_GET(self):
        parsed = urlparse(self.path)
        route = parsed.path
        query = parse_qs(parsed.query)

        if route == "/":
            self._send_file(STATIC_DIR / "index.html")
            return

        if route.startswith("/static/"):
            rel = route[len("/static/"):]
            target = (STATIC_DIR / rel).resolve()
            try:
                target.relative_to(STATIC_DIR.resolve())
            except ValueError:
                self.send_error(403, "Forbidden")
                return
            self._send_file(target)
            return

        if route == "/api/status":
            cfg = load_bot_config()
            self._send_json(
                {
                    "launcher_running": launcher_running(),
                    "stop_flag": STOP_FLAG.exists(),
                    "game_exe": GAME_EXE.exists(),
                    "config": cfg,
                    "accounts": account_status(),
                    "server_name": cfg.get("server_name", ""),
                    "allowed_server_names": cfg.get("allowed_server_names", []),
                }
            )
            return

        if route == "/api/accounts":
            self._send_json({"accounts": load_accounts()})
            return

        if route == "/api/events":
            account = (query.get("account") or [""])[0]
            limit = int((query.get("limit") or ["200"])[0])
            self._send_json({"events": tail_events(account, limit)})
            return

        if route == "/api/report":
            report = build_report()
            self._send_json(report)
            return

        self.send_error(404, "Not Found")

    def do_POST(self):
        parsed = urlparse(self.path)
        route = parsed.path
        body = self._read_body()

        if route == "/api/accounts/save":
            accounts = body.get("accounts")
            if not isinstance(accounts, list):
                self._send_json({"ok": False, "error": "Du lieu tai khoan khong hop le."}, 400)
                return
            clean = []
            seen = set()
            for a in accounts:
                if not isinstance(a, dict):
                    continue
                username = str(a.get("username", "")).strip()
                if not username:
                    continue
                if username in seen:
                    continue
                seen.add(username)
                clean.append(
                    {
                        "username": username,
                        "password": str(a.get("password", "")),
                        "server": str(a.get("server", "")).strip(),
                        "note": str(a.get("note", "")).strip(),
                    }
                )
            save_accounts(clean)
            self._send_json({"ok": True, "count": len(clean), "accounts": clean})
            return

        if route == "/api/accounts/delete":
            username = str(body.get("username", "")).strip()
            if not username:
                self._send_json({"ok": False, "error": "Thieu ten tai khoan."}, 400)
                return
            accounts = [
                a for a in load_accounts()
                if not (isinstance(a, dict) and a.get("username") == username)
            ]
            save_accounts(accounts)
            self._send_json({"ok": True, "accounts": accounts})
            return

        if route == "/api/config/save":
            cfg = load_bot_config()
            for key in (
                "server_name",
                "max_concurrent",
                "stagger_seconds",
                "step_timeout",
                "snapshot_interval",
                "suppress_unity_null_reference",
            ):
                if key in body:
                    cfg[key] = body[key]
            if "allowed_server_names" in body:
                names = body["allowed_server_names"]
                if isinstance(names, list):
                    cfg["allowed_server_names"] = [
                        str(n).strip() for n in names if str(n).strip()
                    ]
            if "suppressed_log_patterns" in body:
                pats = body["suppressed_log_patterns"]
                if isinstance(pats, list):
                    cfg["suppressed_log_patterns"] = [
                        str(p).strip() for p in pats if str(p).strip()
                    ]
            save_bot_config(cfg)
            self._send_json({"ok": True, "config": cfg})
            return

        if route == "/api/launcher/start":
            ok, msg = start_launcher()
            self._send_json({"ok": ok, "message": msg}, 200 if ok else 400)
            return

        if route == "/api/launcher/stop":
            ok, msg = stop_launcher()
            self._send_json({"ok": ok, "message": msg}, 200 if ok else 400)
            return

        if route == "/api/launcher/kill":
            ok, msg = kill_launcher()
            self._send_json({"ok": ok, "message": msg}, 200 if ok else 400)
            return

        if route == "/api/report/generate":
            script = QA_DIR / "launcher" / "report.py"
            try:
                proc = subprocess.run(
                    [sys.executable, str(script)],
                    cwd=str(BASE_DIR),
                    capture_output=True,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    timeout=60,
                )
                self._send_json(
                    {
                        "ok": proc.returncode == 0,
                        "stdout": proc.stdout[-4000:],
                        "stderr": proc.stderr[-4000:],
                    }
                )
            except Exception as e:
                self._send_json({"ok": False, "error": str(e)}, 500)
            return

        if route == "/api/output/clear":
            account = str(body.get("account", "")).strip()
            if not account:
                self._send_json({"ok": False, "error": "Thieu tai khoan."}, 400)
                return
            target = (OUTPUT_DIR / account).resolve()
            try:
                target.relative_to(OUTPUT_DIR.resolve())
            except ValueError:
                self._send_json({"ok": False, "error": "Duong dan khong hop le."}, 400)
                return
            if target.exists():
                shutil.rmtree(target, ignore_errors=True)
            self._send_json({"ok": True})
            return

        self.send_error(404, "Not Found")


def build_report():
    """Tong hop bao cao nhanh (khong can chay report.py)."""
    accounts = account_status()
    durations = [
        a["events"]["duration"]
        for a in accounts
        if a["events"].get("duration") is not None
    ]
    total = len(accounts)
    success = sum(1 for a in accounts if a["events"].get("success"))
    type_counter = Counter()
    for a in accounts:
        for t, c in a["snapshots"].get("by_type", {}).items():
            type_counter[t] += c

    return {
        "total_accounts": total,
        "success_accounts": success,
        "failed_accounts": total - success,
        "success_rate": round(success / total * 100, 1) if total else 0.0,
        "duration_min": min(durations) if durations else None,
        "duration_max": max(durations) if durations else None,
        "duration_avg": round(sum(durations) / len(durations), 2) if durations else None,
        "object_types": dict(type_counter),
    }


def main():
    if not STATIC_DIR.exists():
        print(f"[ERROR] Khong tim thay thu muc static: {STATIC_DIR}")
        sys.exit(1)

    server = ThreadingHTTPServer((HOST, PORT), DashboardHandler)
    url = f"http://{HOST}:{PORT}/"
    print("=" * 60)
    print("       QA TESTBOT DASHBOARD - VU TRU DAI CHIEN")
    print("=" * 60)
    print(f"  Dashboard : {url}")
    print(f"  Config    : {CONFIG_DIR}")
    print(f"  Output    : {OUTPUT_DIR}")
    print("  Nhan Ctrl+C de dung.")
    print("=" * 60)

    try:
        webbrowser.open(url)
    except Exception:
        pass

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[DASHBOARD] Dang dung...")
    finally:
        server.shutdown()


if __name__ == "__main__":
    main()
