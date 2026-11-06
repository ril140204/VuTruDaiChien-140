# QA TESTBOT - VŨ TRỤ ĐẠI CHIẾN (IL2CPP)

Hệ thống công cụ QA / TestBot tự động đăng nhập, chọn khu/server, ghi log sự kiện và snapshot đối tượng (Mob, NPC, OtherPlayer) cho game Unity IL2CPP x64.

---

## 1. Yêu cầu Môi trường
- **HĐH:** Windows 10/11 x64.
- **Python:** 3.10+ (Đã có sẵn `python`).
- **.NET SDK:** .NET SDK 8.0. Nếu chưa cài, đặt tại `C:\Program Files\dotnet\dotnet.exe`
  hoặc cài bản cục bộ (không cần quyền Admin) bằng script `dotnet-install.ps1`.
- **Game Engine:** Unity 2022.3.62f2 IL2CPP.

---

## 2. Cài đặt BepInEx 6 & Plugin QA.TestBot

### Bước 1: Cài BepInEx 6 IL2CPP & UnityExplorer
Toàn bộ file đã được cài đặt vào thư mục game:
- `winhttp.dll`, `doorstop_config.ini`, thư mục `BepInEx/` nằm ở thư mục gốc game.
- Plugin UnityExplorer đã có tại `BepInEx/plugins/sinai-dev-UnityExplorer`.

### Bước 2: Chạy game lần đầu (Sinh Interop Assemblies)
- Chạy `Vũ Trụ Đại Chiến.exe`.
- BepInEx sẽ tự động chạy `Cpp2IL` và `Il2CppInterop` để sinh các file interop tại `BepInEx/interop/` (chỉ mất ~1-2 phút ở lần đầu tiên).
- Sau khi game vào màn hình đăng nhập, đóng game.

### Bước 3: Biên dịch Plugin C# (QA.TestBot.dll)
Chạy lệnh PowerShell sau để build và copy plugin:
```powershell
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
& $dotnet build "D:\game\VuTruDaiChien_140\QA_TestBot\plugin\TestBot.csproj"
Copy-Item "D:\game\VuTruDaiChien_140\QA_TestBot\plugin\bin\Debug\net6.0\QA.TestBot.dll" "D:\game\VuTruDaiChien_140\BepInEx\plugins\" -Force
```

> **Lưu ý:** Phải tắt game trước khi copy DLL. Nếu file đang bị game chiếm sẽ báo
> `The process cannot access the file ... because it is being used by another process`.

---

## 3. Cấu hình Kiểm thử

### File 1: `QA_TestBot/config/bot_config.json`
```json
{
  "test_env_confirmed": true,
  "server_name": "Khu 1",
  "allowed_server_names": [
    "Khu 1",
    "Khu 2",
    "Server 1",
    "Server Test",
    "Thử Nghiệm"
  ],
  "max_concurrent": 1,
  "stagger_seconds": 6.0,
  "step_timeout": 30.0,
  "snapshot_interval": 5.0,
  "log_dir": "QA_TestBot/output",
  "suppress_unity_null_reference": true,
  "suppressed_log_patterns": [
    "FileNotFoundException",
    "Could not load the file"
  ],
  "account_input_name": "Account",
  "password_input_name": "Password",
  "login_button_name": "LoginButton",
  "server_expand_button_name": "ExpandButton"
}
```

> **LƯU Ý GIỚI HẠN AN TOÀN:**
> - `test_env_confirmed`: Bắt buộc phải là `true`. Nếu `false`, launcher và plugin sẽ từ chối chạy.
> - `server_name`: Tên server muốn vào. Bắt buộc phải nằm trong mảng `allowed_server_names`. Nếu ngoài whitelist, hệ thống từ chối chạy.
> - `max_concurrent`: Số client chạy đồng thời (khuyến nghị chạy `1` để kiểm tra trước khi tăng lên).
> - `stagger_seconds`: Thời gian giãn cách giữa các lần mở game (mặc định 6.0s).
> - `suppress_unity_null_reference`: Ẩn lỗi `NullReferenceException` spam từ Unity (do thiếu `Facebook.Unity.Windows` trên bản PC).
> - `suppressed_log_patterns`: Danh sách chuỗi log cần ẩn (mỗi dòng 1 chuỗi).
>
> **Cơ chế lọc log:** Plugin thay thế `ConsoleLogListener` của BepInEx bằng một
> `FilteringLogListener` bao bọc listener gốc. Chỉ log có **nguồn = `Unity`** và
> chứa chuỗi trong danh sách trên mới bị chặn — log của BepInEx và của chính
> QA TestBot luôn được giữ nguyên. Không cần sửa bất kỳ file engine nào.

### File 2: `QA_TestBot/config/accounts.json`
Danh sách tài khoản test (username / password):
```json
[
  {
    "username": "tryni1",
    "password": "tryni1"
  },
  {
    "username": "tryni2",
    "password": "tryni1"
  }
]
```

---

## 4. Vận hành & Chạy thử nghiệm

### 4.1. Khởi chạy Launcher
Mở PowerShell tại thư mục `D:\game\VuTruDaiChien_140`:
```powershell
python .\QA_TestBot\launcher\launcher.py
```
- Launcher sẽ tự động phân bổ tài khoản, tạo biến môi trường `TESTBOT_ACCOUNT_FILE` cho từng client.
- Khi người dùng chạy game trực tiếp bằng tay (không thông qua launcher), plugin TestBot sẽ **hoàn toàn đứng yên** và không can thiệp.

### 4.2. Dừng khẩn cấp
Có 2 cách dừng khẩn cấp:
1. Nhấn tổ hợp phím **`Ctrl + C`** trên cửa sổ terminal đang chạy `launcher.py`.
2. Tạo file `stop.flag` tại `QA_TestBot/config/stop.flag`:
   ```powershell
   New-Item -ItemType File -Path ".\QA_TestBot\config\stop.flag"
   ```
Hệ thống sẽ lập tức dừng việc spawn tài khoản mới và đóng toàn bộ các game client đang chạy trong vòng **< 10 giây**.

---

## 5. Dashboard Quản Lý Tài Khoản (Web UI)

Giao diện quản lý tài khoản chạy độc lập, không cần thư viện ngoài (Python stdlib thuần).

### 5.1. Khởi chạy Dashboard
```powershell
python .\QA_TestBot\dashboard\dashboard.py
```
- Mở tự động trình duyệt tại `http://127.0.0.1:8766/`
- Nhấn `Ctrl + C` để dừng.

### 5.2. Chức năng chính

| Khu vực | Chức năng |
| :--- | :--- |
| Thanh trên | `▶ Bắt đầu` (chạy launcher), `⏹ Dừng khẩn cấp` (dừng toàn bộ client), `↻` (refresh), `+ Thêm` / `💾 Lưu` (thêm & lưu tài khoản) |
| Danh sách tài khoản | Lọc theo tên / trạng thái (Đã vào map / Chưa vào map / Đang chạy), click để xem chi tiết |
| Tab **Cấu hình** | Server đích, whitelist server, `max_concurrent`, `stagger_seconds`, `step_timeout`, `snapshot_interval`, bật/tắt lọc log Unity, danh sách chuỗi log cần ẩn |
| Tab **Tài khoản** | Sửa username / password / server / ghi chú trực tiếp trên bảng |
| Tab **Nhật ký** | Xem log realtime của từng tài khoản, xoá log theo tài khoản |
| Tab **Báo cáo** | Thống kê tổng hợp + nút `📊 Xuất báo cáo` (sinh `summary.md` / `summary.csv`) |

### 5.3. REST API

| Method | Endpoint | Mô tả |
| :--- | :--- | :--- |
| GET | `/api/status` | Trạng thái launcher, config, danh sách tài khoản kèm trạng thái |
| GET | `/api/accounts` | Danh sách tài khoản |
| POST | `/api/accounts/save` | Lưu danh sách tài khoản |
| POST | `/api/accounts/delete` | Xoá một tài khoản (`{ "username": "..." }`) |
| POST | `/api/config/save` | Lưu cấu hình bot |
| POST | `/api/launcher/start` | Khởi chạy launcher |
| POST | `/api/launcher/stop` | Dừng launcher + toàn bộ client |
| POST | `/api/launcher/kill` | Ép dừng launcher |
| GET | `/api/events?account=<user>&limit=300` | Log sự kiện của một tài khoản |
| GET | `/api/report` | Báo cáo tổng hợp nhanh (không cần chạy `report.py`) |
| POST | `/api/report/generate` | Chạy `report.py` để xuất `summary.md` / `summary.csv` |
| POST | `/api/output/clear` | Xoá thư mục log của một tài khoản |

> **Lưu ý:** Chỉ mở **một** instance dashboard. Nếu chạy nhiều lần, cần tắt các
> instance cũ trước, nếu không sẽ tranh đấu port `8766`.

---

## 5A. Bản Desktop (WinForms) — Quản Lý Tài Khoản Trực Tiếp Trên Máy

Bản desktop native (C# / WinForms / .NET 8) thay thế hoàn toàn web dashboard.
Chạy trực tiếp trên máy, không cần mở trình duyệt, không cần server HTTP.

### 5A.1. Chạy nhanh (Development)

Double-click `QA_TestBot\desktop\run.bat`, hoặc:

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet-sdk\dotnet.exe"
& $dotnet build ".\QA_TestBot\desktop\QA.TestBot.Desktop.csproj" -c Release
& ".\QA_TestBot\desktop\bin\Release\net8.0-windows\QA.TestBot.Desktop.exe"
```

### 5A.2. Đóng gói bản chạy độc lập (Self-contained)

```powershell
.\QA_TestBot\desktop\publish.ps1
```
- Xuất ra `QA_TestBot\desktop\publish\QA.TestBot.Desktop.exe` (không cần cài .NET).
- Script tự ghi file `qatestbot.path` trỏ về thư mục gốc project, để app tìm đúng
  `QA_TestBot/config`, `QA_TestBot/output` và file game.

### 5A.3. Giao diện

```
┌──────────────────────────────────────────────────────────────────────┐
│ QA TestBot   [Bắt đầu] [Dừng] [Ép dừng] │ [Thêm] [Lưu] [Xóa] [↻]  │
├───────────────────┬──────────────────────────────────────────────────┤
│ DANH SÁCH TÀI KHOẢN │  [Tài khoản] [Cấu hình] [Nhật ký] [Báo cáo]    │
│ [tìm kiếm][lọc]   │ ┌──────────────────────────────────────────────┐ │
│ ┌────────────────┐ │ │ ✓ │ Tài khoản │ Mật khẩu │ Server │ NV │ Ghi │ │
│ │ tryni3      🟢 │ │ ├───┼──────────┼──────────┼────────┼────┼─────┤ │
│ │ Vào map  4.61s │ │ │ ✓ │ tryni3   │ ••••     │        │ 1  │     │ │
│ ├────────────────┤ │ │ ✓ │ tryni2   │ ••••     │        │ 1  │     │ │
│ │ tryni2      🟢 │ │ └──────────────────────────────────────────────┘ │
│ └────────────────┘ │                                                  │
│ 3 tài khoản • 3 vào map│                                            │
└───────────────────┴──────────────────────────────────────────────────┘
```

| Khu vực | Chức năng |
| :--- | :--- |
| Thanh trên | `Bắt đầu` (chạy launcher), `Dừng` (dừng mềm — tự escalate sang ép dừng sau 3s nếu launcher không phản hồi), `Ép dừng` (kill launcher + toàn bộ client), `Thêm` / `Lưu` / `Xóa`, `↻` (refresh) |
| Cột trái | Danh sách tài khoản realtime kèm trạng thái (Vào map / Chưa vào map / Chưa chạy / Tắt) và thời gian vào map; tìm kiếm + lọc theo trạng thái |
| Tab **Tài khoản** | Sửa trực tiếp trên bảng: bật/tắt, username, password, server, nhân vật, ghi chú |
| Tab **Cấu hình** | Server đích, whitelist server, `max_concurrent`, `stagger_seconds`, `step_timeout`, `snapshot_interval`, bật/tắt lọc log Unity, danh sách chuỗi log cần ẩn |
| Tab **Nhật ký** | Log realtime từng tài khoản (500 dòng gần nhất), xoá log theo tài khoản |
| Tab **Báo cáo** | Bảng tổng hợp trạng thái + nút `Xuất báo cáo` (sinh `summary.md` / `summary.csv`) |

### 5A.4. Định vị thư mục gốc

App tự dò thư mục gốc project bằng cách leo lên từ vị trí file `.exe` cho tới khi
thấy **cả** thư mục `QA_TestBot/config` **và** file game `.exe`. Nếu đóng gói chạy
ở nơi khác, đặt biến môi trường:

```powershell
$env:QATESTBOT_ROOT = "D:\game\VuTruDaiChien_140"
```

hoặc tạo file `qatestbot.path` cạnh file `.exe` chứa đường dẫn tuyệt đối tới
thư mục gốc.

### 5A.5. Ghi log lỗi

Nếu app gặp lỗi ngoài dự kiến, chi tiết stack trace được ghi vào:
```
%LOCALAPPDATA%\QA_TestBot\desktop_error.log
```

---

## 6. Xuất Báo cáo Kết quả
Sau khi các tài khoản kiểm thử hoàn tất hoặc dừng:
```powershell
python .\QA_TestBot\launcher\report.py
```
Kết quả được xuất ra:
- `QA_TestBot/output/summary.csv`: Thống kê dạng bảng từng tài khoản, trạng thái, thời gian đăng nhập.
- `QA_TestBot/output/summary.md`: Báo cáo chi tiết số lượng tài khoản thành công/thất bại, thời gian (min/avg/max), danh sách lỗi và thống kê snapshot đối tượng trong khu.

> **Lưu ý encoding:** Các script Python đã tự chuyển stdout sang UTF-8, nên có thể
> in tiếng Việt có dấu đúng cách trên Windows console (`cp1258`) mà không bị crash.

---

## 7. Ghi chú kỹ thuật

### 7.1. Chống spam log `SERVER_LIST`
Khi chờ danh sách server, plugin chỉ ghi 1 dòng `SERVER_LIST` cho mỗi **nội dung danh
sách khác nhau** (dùng chữ ký nối các tên server). Trước đây logic ghi mỗi 2 giây
kể cả khi danh sách không đổi, gây ra hàng chục nghìn dòng rác (~2 MB CSV/account).

### 7.2. Lọc log Unity
Xem mục `suppress_unity_null_reference` trong mục **3. Cấu hình Kiểm thử**.
