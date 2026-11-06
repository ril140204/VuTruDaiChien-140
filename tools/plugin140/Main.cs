using System;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace QLTK.Client140
{
    /// <summary>
    /// Plugin BepInEx cho bản game 140, nói chuyện với QLTK.exe.
    ///
    /// Trách nhiệm:
    ///   1. Đọc tham số dòng lệnh (-autologin ...) do QLTK truyền vào.
    ///   2. Kết nối TCP tới 127.0.0.1:28082 (QLTK socket server).
    ///   3. Chạy luồng tự động đăng nhập (LoginMod).
    ///   4. Nhận lệnh điều khiển từ QLTK và thực thi.
    ///
    /// Giao thức:
    ///   Client -> QLTK : TEXT UTF-8, ví dụ "STT:5", "lv: 12"
    ///   QLTK  -> Client: BINARY [1 byte cmd][payload BinaryWriter]
    /// </summary>
    [BepInPlugin("qltk.client140", "QLTK Client 140", "1.0.0")]
    public sealed class Main : BasePlugin
    {
        internal static Main Instance;

        private GameArgs _args;
        private SocketClient _socket;
        private LoginMod _loginMod;
        private GameExplorer _explorer;
        private bool _sttSent;

        public override void Load()
        {
            Instance = this;

            Log140.Init(Log);

            // Bắt buộc: cài dispatcher trước khi bất kỳ thread nào gọi Unity API.
            // Dispatcher được gắn lên chính GameObject của plugin (main thread).
            // Nếu lỗi ở đây thì thoát ngay, không chạy tiếp để tránh crash khó hiểu.
            try
            {
                MainThreadDispatcher.Install(this);
            }
            catch (Exception ex)
            {
                Log140.Error("Khong cai duoc MainThreadDispatcher: " + ex);
                return;
            }

            try
            {
                _args = GameArgs.Parse();
            }
            catch (Exception ex)
            {
                Log140.Error("Parse tham so loi: " + ex);
                return;
            }

            Log140.Info($"Plugin loaded. autologin={_args.HasAutoLogin} " +
                        $"user='{_args.Username}' server='{_args.Server}' char={_args.CharIndex} " +
                        $"stt={_args.Stt} res={_args.Width}x{_args.Height}");

            // Kết nối QLTK server
            try
            {
                _socket = new SocketClient();
                _socket.OnMessageReceived += OnMessageReceived;
                _socket.OnBinaryReceived += OnBinaryReceived;
                _socket.OnConnected += OnConnected;
                _socket.OnDisconnected += OnDisconnected;
                _ = _socket.ConnectAsync("127.0.0.1", 28082);
            }
            catch (Exception ex)
            {
                Log140.Error("Khoi tao socket loi: " + ex);
            }

            // Tự động đăng nhập (nếu có tham số)
            if (_args.HasAutoLogin)
            {
                try
                {
                    _loginMod = LoginMod.Install(this, _args, _socket);
                }
                catch (Exception ex)
                {
                    Log140.Error("Cai LoginMod loi: " + ex);
                }
            }

            // GameExplorer: tự động khám phá game khi vào MainGameScene
            // và ghi ra file GameExplorer.log để gửi cho Claude
            try
            {
                _explorer = GameExplorer.Install(this);
            }
            catch (Exception ex)
            {
                Log140.Error("Cai GameExplorer loi: " + ex);
            }

            // LiveWatcher: ghi lại MỌI thao tác trong game theo thời gian thực
            // ra BepInEx/GamePlay.log để developer đọc và làm tool theo yêu cầu.
            try
            {
                LiveWatcher.Install(this);
            }
            catch (Exception ex)
            {
                Log140.Error("Cai LiveWatcher loi: " + ex);
            }

            // RibbonBot: tool tự động đi Ruy Băng (F10 bật / F11 dừng,
            // hoặc lệnh "ribbon start" / "ribbon stop" qua GameCmd.txt).
            try
            {
                RibbonBot.Install(this);
            }
            catch (Exception ex)
            {
                Log140.Error("Cai RibbonBot loi: " + ex);
            }

            // CombatProbe: chẩn đoán cơ chế đánh (probe start|stop|status)
            try
            {
                CombatProbe.Install(this);
            }
            catch (Exception ex)
            {
                Log140.Error("Cai CombatProbe loi: " + ex);
            }
        }

        private void OnConnected()
        {
            // OnConnected gọi từ background thread -> marshal về main thread
            MainThreadDispatcher.Enqueue(() =>
            {
                Log140.Info("Da ket noi QLTK server (127.0.0.1:28082)");

                // Đăng ký STT ngay khi kết nối (nếu biết).
                // LoginMod cũng gửi khi vào map; _sttSent tránh gửi trùng.
                if (_args.Stt >= 0 && !_sttSent)
                {
                    _socket.Send($"STT:{_args.Stt}");
                    _sttSent = true;
                }
            });
        }

        private void OnDisconnected()
        {
            MainThreadDispatcher.Enqueue(() => Log140.Warn("Mat ket noi QLTK server"));
        }

        private void OnMessageReceived(string message)
        {
            try
            {
                if (message.StartsWith("__BIN__", StringComparison.Ordinal)) return;

                if (message.StartsWith("lv: ", StringComparison.Ordinal))
                    Log140.Info("Server bao level: " + message.Substring(4));
                else if (message.StartsWith("STT:", StringComparison.Ordinal))
                    Log140.Info("Server xac nhan STT: " + message.Substring(4));
                else
                    Log140.Info("Server gui len: " + message);
            }
            catch (Exception ex)
            {
                Log140.Error("Xu ly tin nhan loi: " + ex);
            }
        }

        /// <summary>
        /// Xử lý gói nhị phân từ QLTK server.
        /// Payload đọc theo đúng thứ tự QLTK ghi (xem Message2.cs).
        /// </summary>
        private void OnBinaryReceived(sbyte cmd, byte[] packet)
        {
            try
            {
                var payload = new PacketReader(packet);

                switch (cmd)
                {
                    case Cmd.SendFps:
                        ApplyFps(payload.ReadInt());
                        break;

                    case Cmd.CongChiSo:
                        Log140.Info("CongChiSo type=" + payload.ReadInt());
                        break;

                    case Cmd.Xmap:
                        Log140.Info("Xmap mapId=" + payload.ReadInt());
                        break;

                    case Cmd.ChangeZone:
                        Log140.Info("changeZone zoneId=" + payload.ReadInt());
                        break;

                    case Cmd.NhatZGem:
                        Log140.Info("nhat_zGem type=" + payload.ReadInt());
                        break;

                    case Cmd.SendTanCong:
                        Log140.Info("sendTanCong typeId=" + payload.ReadInt());
                        break;

                    case Cmd.KepFullKhu:
                        {
                            int map = payload.ReadInt();
                            int zone = payload.ReadInt();
                            Log140.Info($"kepfullkhu map={map} zone={zone}");
                            break;
                        }

                    case Cmd.SendHp:
                        Log140.Info("sendHp '" + payload.ReadString() + "'");
                        break;

                    case Cmd.TurnOffWinRuyBang:
                        Log140.Info("sendTurnOffWinRuyBang");
                        break;

                    case Cmd.Resize:
                        {
                            int w = payload.ReadInt();
                            int h = payload.ReadInt();
                            ApplyResize(w, h);
                            break;
                        }

                    case Cmd.dung5BinhItem:
                        Log140.Info("dung5BinhItem");
                        break;

                    case Cmd.mua5BinhItem:
                        Log140.Info("mua5BinhItem");
                        break;

                    case Cmd.mua100Dau2:
                        Log140.Info("mua100Dau2");
                        break;

                    case Cmd.UpdateInfo:
                        Log140.Info("updateInfo");
                        SendLevelToServer();
                        break;

                    case Cmd.muaDoLv3x:
                        Log140.Info("muaDoLv3x");
                        break;

                    default:
                        Log140.Info("Lenh chua xu ly: " + cmd);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log140.Error("Xu ly lenh nhi phan loi: " + ex);
            }
        }

        private static void ApplyFps(int fps)
        {
            try
            {
                if (fps > 0) UnityEngine.Application.targetFrameRate = fps;
                Log140.Info("Dat FPS = " + fps);
            }
            catch (Exception ex) { Log140.Error("Dat FPS loi: " + ex.Message); }
        }

        private static void ApplyResize(int w, int h)
        {
            try
            {
                if (w <= 0 || h <= 0) return;
                UnityEngine.Screen.SetResolution(w, h, UnityEngine.FullScreenMode.Windowed);
                Log140.Info($"Resize = {w}x{h}");
            }
            catch (Exception ex) { Log140.Error("Resize loi: " + ex.Message); }
        }

        /// <summary>Gửi level hiện tại lên QLTK (format "lv: &lt;level&gt;").</summary>
        private void SendLevelToServer()
        {
            try
            {
                int level = PlayerInfo.TryGetLevel();
                if (level < 0)
                {
                    Log140.Warn("Khong doc duoc level nhan vat");
                    return;
                }
                _socket?.Send($"lv: {level}");
                Log140.Info($"Da gui level={level} len QLTK");
            }
            catch (Exception ex)
            {
                Log140.Warn("Gui level loi: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Đọc thông tin nhân vật đang điều khiển.
    /// Level lấy từ UI (LevelText / parent AppearanceInfo) vì đây là dữ liệu
    /// game đã render đúng, không phụ thuộc tên field bị obfuscate.
    /// </summary>
    internal static class PlayerInfo
    {
        public static int TryGetLevel()
        {
            // 1. UI: TextLevelDisplay / "Level" panel / AppearanceInfo/LevelText
            int fromUi = TryGetLevelFromUi();
            if (fromUi >= 0) return fromUi;

            // 2. Fallback: reflection trên MainPlayer
            try
            {
                var player = UnityEngine.Object.FindObjectOfType<MainPlayer>(true);
                if (player == null) return -1;

                foreach (var comp in player.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    int v = ScanForLevel(comp);
                    if (v >= 0) return v;
                }
            }
            catch { }

            return -1;
        }

        private static int TryGetLevelFromUi()
        {
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TMPro.TextMeshProUGUI>(true);
                foreach (var t in texts)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    if (!string.IsNullOrEmpty(t.gameObject.name) &&
                        string.Equals(t.gameObject.name, "LevelText", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse((t.text ?? "").Trim(), out int lv) && lv >= 0) return lv;
                    }
                }
            }
            catch { }

            return -1;
        }

        private static int ScanForLevel(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            Type t;
            try { t = obj.GetType(); }
            catch { return -1; }

            foreach (var p in t.GetProperties(flags))
            {
                if (p.PropertyType != typeof(int) || p.GetIndexParameters().Length > 0) continue;
                if (!LooksLikeLevel(p.Name)) continue;

                try
                {
                    object val = p.GetValue(obj);
                    if (val is int i) return i;
                }
                catch { }
            }

            foreach (var f in t.GetFields(flags))
            {
                if (f.FieldType != typeof(int)) continue;
                if (!LooksLikeLevel(f.Name)) continue;

                try
                {
                    object val = f.GetValue(obj);
                    if (val is int i) return i;
                }
                catch { }
            }

            return -1;
        }

        private static bool LooksLikeLevel(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n == "level" || n == "lv" || n.EndsWith("level") || n.EndsWith("_lv");
        }
    }

    /// <summary>
    /// Đọc payload của gói nhị phân theo định dạng BinaryWriter .NET.
    /// </summary>
    internal sealed class PacketReader
    {
        private readonly byte[] _data;
        private int _pos;

        public PacketReader(byte[] data)
        {
            _data = data ?? Array.Empty<byte>();
            _pos = 1;   // bỏ byte command
        }

        public int ReadInt()
        {
            if (_pos + 4 > _data.Length) { _pos = _data.Length; return 0; }
            int v = BitConverter.ToInt32(_data, _pos);
            _pos += 4;
            return v;
        }

        public short ReadShort()
        {
            if (_pos + 2 > _data.Length) { _pos = _data.Length; return 0; }
            short v = BitConverter.ToInt16(_data, _pos);
            _pos += 2;
            return v;
        }

        public sbyte ReadByte()
        {
            if (_pos + 1 > _data.Length) { _pos = _data.Length; return 0; }
            return unchecked((sbyte)_data[_pos++]);
        }

        public string ReadString()
        {
            // 7-bit encoded length
            int length = 0, shift = 0;

            while (true)
            {
                if (_pos >= _data.Length) return string.Empty;
                byte b = _data[_pos++];
                length |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 28) return string.Empty;
            }

            if (length <= 0) return string.Empty;
            if (_pos + length > _data.Length) length = _data.Length - _pos;

            string s = System.Text.Encoding.UTF8.GetString(_data, _pos, length);
            _pos += length;
            return s;
        }
    }
}
