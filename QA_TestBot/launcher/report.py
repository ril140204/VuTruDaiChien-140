#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
QA TestBot Report Generator
Đọc các file events.csv và snapshots.csv từ thư mục output/
Tổng hợp và xuất summary.csv cùng summary.md.
"""

import os
import sys
import csv
import re
from pathlib import Path
from collections import defaultdict, Counter

BASE_DIR = Path(__file__).resolve().parent.parent.parent
OUTPUT_DIR = BASE_DIR / "QA_TestBot" / "output"
SUMMARY_CSV = OUTPUT_DIR / "summary.csv"
SUMMARY_MD = OUTPUT_DIR / "summary.md"

# Windows console mac dinh la cp1258 -> khong in duoc mot so chu Viet Nam.
# Chuyen stdout sang UTF-8 va bo qua loi encode thay vi crash.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

def parse_events(file_path):
    account = "unknown"
    is_success = False
    duration = None
    errors = []
    events_count = 0
    last_state = "Unknown"

    if not file_path.exists():
        return {
            "success": False,
            "duration": None,
            "errors": ["File events.csv khong ton tai"],
            "events_count": 0,
            "last_state": "NotFound"
        }

    with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
        reader = csv.DictReader(f)
        for row in reader:
            events_count += 1
            account = row.get("account", account)
            state = row.get("state", last_state)
            last_state = state
            event_type = row.get("event_type", "")
            msg = row.get("message", "")

            if state == "InGame" or "LOGIN_SUCCESS" in msg:
                is_success = True

            if event_type == "METRIC" and "LOGIN_SUCCESS" in msg:
                match = re.search(r"([\d\.]+)\s*seconds", msg)
                if match:
                    try:
                        duration = float(match.group(1))
                    except ValueError:
                        pass

            if event_type in ["ERROR", "TIMEOUT"]:
                errors.append(f"[{state}] {msg}")

    return {
        "account": account,
        "success": is_success,
        "duration": duration,
        "errors": errors,
        "events_count": events_count,
        "last_state": last_state
    }

def parse_snapshots(file_path):
    if not file_path.exists():
        return {}

    # snapshot_time -> type -> count
    time_series = defaultdict(Counter)
    total_snapshots = 0

    with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
        reader = csv.DictReader(f)
        for row in reader:
            t = row.get("time", "")
            obj_type = row.get("type", "Unknown")
            if t and obj_type:
                time_series[t][obj_type] += 1
                total_snapshots += 1

    return {
        "total_records": total_snapshots,
        "snapshots_count": len(time_series),
        "time_series": time_series
    }

def generate_report():
    if not OUTPUT_DIR.exists():
        print(f"[ERROR] Thư mục output không tồn tại: {OUTPUT_DIR}")
        return

    account_dirs = [d for d in OUTPUT_DIR.iterdir() if d.is_dir()]
    if not account_dirs:
        print(f"[WARN] Chưa có thư mục output tài khoản nào tại: {OUTPUT_DIR}")
        return

    account_summaries = []
    all_errors = []
    durations = []
    aggregated_types = Counter()

    for acc_dir in account_dirs:
        acc_name = acc_dir.name
        events_file = acc_dir / "events.csv"
        snapshots_file = acc_dir / "snapshots.csv"

        evt_data = parse_events(events_file)
        snap_data = parse_snapshots(snapshots_file)

        acc_summary = {
            "account": acc_name,
            "status": "SUCCESS" if evt_data["success"] else "FAILED",
            "duration": evt_data["duration"],
            "events_count": evt_data["events_count"],
            "errors_count": len(evt_data["errors"]),
            "snapshots_records": snap_data.get("total_records", 0),
            "last_state": evt_data["last_state"]
        }
        account_summaries.append(acc_summary)

        if evt_data["duration"] is not None:
            durations.append(evt_data["duration"])

        all_errors.extend(evt_data["errors"])

        for t, counts in snap_data.get("time_series", {}).items():
            for o_type, c in counts.items():
                aggregated_types[o_type] += c

    total_acc = len(account_summaries)
    success_acc = sum(1 for a in account_summaries if a["status"] == "SUCCESS")
    failed_acc = total_acc - success_acc
    success_rate = (success_acc / total_acc * 100) if total_acc > 0 else 0

    min_dur = min(durations) if durations else 0
    max_dur = max(durations) if durations else 0
    avg_dur = (sum(durations) / len(durations)) if durations else 0

    # 1. Xuat summary.csv
    with open(SUMMARY_CSV, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=[
            "account", "status", "duration_sec", "events_count", "errors_count", "snapshots_records", "last_state"
        ])
        writer.writeheader()
        for a in account_summaries:
            writer.writerow({
                "account": a["account"],
                "status": a["status"],
                "duration_sec": f"{a['duration']:.2f}" if a["duration"] is not None else "N/A",
                "events_count": a["events_count"],
                "errors_count": a["errors_count"],
                "snapshots_records": a["snapshots_records"],
                "last_state": a["last_state"]
            })
    print(f"[OK] Da xuat bao cao CSV: {SUMMARY_CSV}")

    # 2. Xuat summary.md
    error_counter = Counter(all_errors)
    common_errors = error_counter.most_common(5)

    md_content = []
    md_content.append("# BÁO CÁO TỔNG HỢP KIỂM THỬ TẢI & ĐĂNG NHẬP (QA TESTBOT)")
    md_content.append(f"**Thời gian xuất báo cáo:** {Path(__file__).stat().st_mtime}\n")
    md_content.append("## 1. Kết quả Đăng nhập & Vào khu")
    md_content.append(f"- **Tổng số tài khoản kiểm thử:** {total_acc}")
    md_content.append(f"- **Đăng nhập vào map thành công:** {success_acc} ({success_rate:.1f}%)")
    md_content.append(f"- **Thất bại / Timeout:** {failed_acc}\n")

    md_content.append("## 2. Thống kê Thời gian vào khu (Seconds)")
    if durations:
        md_content.append(f"- **Nhanh nhất (Min):** {min_dur:.2f}s")
        md_content.append(f"- **Chậm nhất (Max):** {max_dur:.2f}s")
        md_content.append(f"- **Trung bình (Avg):** {avg_dur:.2f}s\n")
    else:
        md_content.append("- *Chưa ghi nhận dữ liệu thời gian thành công.*\n")

    md_content.append("## 3. Chi tiết từng Tài khoản")
    md_content.append("| Tài khoản | Trạng thái | Thời gian vào (s) | Số sự kiện | Lỗi | Snapshot records | Trạng thái cuối |")
    md_content.append("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |")
    for a in account_summaries:
        dur_str = f"{a['duration']:.2f}" if a['duration'] is not None else "-"
        md_content.append(f"| {a['account']} | **{a['status']}** | {dur_str} | {a['events_count']} | {a['errors_count']} | {a['snapshots_records']} | {a['last_state']} |")
    md_content.append("")

    md_content.append("## 4. Các lỗi hay gặp nhất")
    if common_errors:
        for err, count in common_errors:
            md_content.append(f"- `{count}x`: {err}")
    else:
        md_content.append("- *Không ghi nhận lỗi nào trong quá trình kiểm thử.*")
    md_content.append("")

    md_content.append("## 5. Thống kê Đối tượng trong Khu (Snapshots)")
    if aggregated_types:
        for obj_t, c in aggregated_types.items():
            md_content.append(f"- **{obj_t}:** {c} lượt ghi nhận")
    else:
        md_content.append("- *Chưa có snapshot đối tượng nào được ghi.*")
    md_content.append("")

    with open(SUMMARY_MD, "w", encoding="utf-8") as f:
        f.write("\n".join(md_content))

    print(f"[OK] Da xuat bao cao Markdown: {SUMMARY_MD}")
    print("\n" + "=" * 50)
    print("\n".join(md_content[:15]))
    print("=" * 50)

if __name__ == "__main__":
    generate_report()
