# Vũ Trụ Đại Chiến — Client 140 Automation Toolkit

Bộ công cụ tự động hoá cho client game **Vũ Trụ Đại Chiến bản 140** (BepInEx 6 IL2CPP),
gồm plugin C#, launcher Python và công cụ phân tích reverse-engineering.

> ⚠️ Repo này **chỉ chứa source code**. Binary game, `GameAssembly.dll`, `interop/`,
> `dump/`, `Il2CppDumper/` và toàn bộ credential đều bị `.gitignore` loại bỏ.

---

## 1. Cấu trúc

```
tools/plugin140/        Plugin BepInEx chính (QLTK.Client140.dll)
  Main.cs               Entry point: cài các module con
  LoginMod.cs           Tự động đăng nhập (parse -autologin base64)
  GameApi.cs            Wrapper API game: moveTo/setPosition, mobs, items, gates, level
  ApiDumper.cs          Công cụ chẩn đoán runtime (pos/mobs/gates/automob/level/...)
  RibbonBot.cs          Bot tự động đi Ruy Băng Đỏ (state machine)
  CombatProbe.cs        Probe chẩn đoán cơ chế đánh (chạy trong Update, không block)
  LiveWatcher.cs        Ghi log mọi thao tác game realtime + nhận lệnh từ file
  GameExplorer.cs       Khám phá scene/UI, ghi GameExplorer.log
  Message2.cs / SocketClient.cs   Giao tiếp với QLTK server (127.0.0.1:28082)

tools/                  Script Python/C# phụ trợ (build, map type, IL dump, launcher)
QA_TestBot/             Hệ thống test bot: desktop GUI (WinForms), launcher, dashboard
```

## 2. Yêu cầu

- Windows x64
- .NET 8 SDK (build plugin) — `tools/dotnet-install.ps1` nếu chưa có
- Game client 140 (không nằm trong repo)
- BepInEx 6 (IL2CPP) đã cài vào thư mục game

## 3. Build plugin

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\deploy_plugin.ps1
```

Output: `BepInEx\plugins\QLTK.Client140.dll`

## 4. Chạy thử (auto login)

```powershell
$env:QLTK_USER="<username>"
$env:QLTK_PASS="<password>"
$env:QLTK_SERVER="Vũ Trụ 1"
python tools\test_launch.py
```

> Không hard-code mật khẩu trong source. Mọi credential nạp qua biến môi trường
> hoặc file `accounts.json` (file này đã `.gitignore`).

## 5. Điều khiển plugin từ console

Plugin đọc lệnh từ file `GameCmd.txt` (thư mục game) và ghi log ra `BepInEx/LogOutput.log`.

```powershell
# gửi lệnh
powershell -NoProfile -ExecutionPolicy Bypass -File tools\game_cmd.ps1 -L "pos"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\game_cmd.ps1 -L "mobs"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\game_cmd.ps1 -L "automob"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\game_cmd.ps1 -L "probe start"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\game_cmd.ps1 -L "ribbon start"
```

| Lệnh | Tác dụng |
|------|----------|
| `pos` | Map id, tọa độ, tên map, level nhân vật |
| `mobs` | Danh sách quái còn sống + vị trí |
| `gates` | Cổng chuyển map |
| `items` | Đồ rơi trên mặt đất |
| `npcs` | NPC trong map |
| `ibtn` / `abtn` | Nút tương tác / nút auto-đánh |
| `level` | Level nhân vật |
| `auto` / `autoon` / `autooff` | Trạng thái & bật/tắt auto-đánh |
| `automob` | Dump `MainGame.autoAttackMobData` |
| `automobadd <id>` | Thêm mobInfoId vào danh sách auto |
| `probe start\|stop` | Chạy CombatProbe chẩn đoán cơ chế đánh |
| `ribbon start\|stop\|status` | Bật/dừng bot Ruy Băng Đỏ |

## 6. Ghi chú kỹ thuật quan trọng

### 6.1 Di chuyển / dịch chuyển
- `MainPlayer.moveTo(x, y)` → nhân vật **bay từ từ** (chậm).
- `MainPlayer.setPosition(Vector2)` → dịch chuyển **tức thời**, server chấp nhận, không bị kéo về.

### 6.2 Xác định level nhân vật
Level thật nằm ở text bên trong GameObject cha tên `"Level"`.
`AppearanceInfo/LevelText` trả về `99` (level trang bị/cosmetic) — **không dùng** để chọn mốc Ruy Băng.

### 6.3 Auto-đánh
`AutoAttackButton` chỉ là nút mở menu ("Tab"), **không** phải công tắc bật auto.
Cờ thật nằm ở `MainGame.setPlayerOnAutoMode(bool)` và các property
`isAutoAttackOn` / `isOnAutoMode` / `BLJEMOOGPGD` / `NCAOKLGBPOI` / `BKJINDIJANL`.

### 6.4 Lọc UI button
Button trong `DontDestroyObject/ObjectPoolHandler/InteractPanelButton` là **template** của
object pool — bấm vào đó sẽ kích hoạt button tái sử dụng/stale.
Luôn lọc các button có `"ObjectPoolHandler"` trong transform path.

### 6.5 IL2CPP & reflection
- Custom MonoBehaviour phải `ClassInjector.RegisterTypeInIl2Cpp<T>()`.
- `FindObjectOfType<T>()` **không** tìm thấy custom MonoBehaviour → giữ static instance.
- Nhiều getter không được Il2CppInterop sinh ra ở compile-time → phải đọc qua reflection.

### 6.6 Threading
`Thread.Sleep` trong command handler **block main thread Unity** → skill không execute được.
Mọi thao tác cần chờ (đo HP, đợi skill land) phải làm bằng state machine chạy trong `Update()`.

## 7. QA_TestBot

- `QA_TestBot/desktop/` — GUI WinForms quản lý tài khoản, chạy song song nhiều client.
- `QA_TestBot/launcher/` — script Python điều phối, sinh báo cáo.
- `QA_TestBot/dashboard/` — web dashboard xem tiến độ.
- `QA_TestBot/plugin/` — plugin BepInEx phục vụ test (snapshot trạng thái).

## 8. License

Source code của bộ công cụ này. Game client và file nhị phân của game **không**
được phân phối trong repo này — tuân thủ điều khoản bản quyền của nhà phát hành.
