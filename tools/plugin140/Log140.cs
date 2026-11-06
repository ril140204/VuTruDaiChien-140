using System;
using System.IO;
using System.Text;
using BepInEx.Logging;

namespace QLTK.Client140
{
    /// <summary>
    /// Logger cho plugin.
    ///
    /// Vì sao không dùng UnityEngine.Debug.Log:
    ///   Trong BepInEx 6 IL2CPP, Debug.Log của Unity chỉ chắc chắn ra console,
    ///   còn LogOutput.log có thể không nhận (phụ thuộc listener). Để chẩn đoán
    ///   luôn có dữ liệu, mọi log đi qua BepInEx ManualLogSource VÀ ghi thêm
    ///   ra file riêng QLTK140.log.
    ///
    /// An toàn thread: ghi file có lock, gọi được từ cả background thread.
    /// KHÔNG gọi Unity API ở đây.
    /// </summary>
    internal static class Log140
    {
        private static ManualLogSource _source;
        private static readonly object _lock = new object();
        private static string _filePath;
        private static bool _fileBroken;

        public static void Init(ManualLogSource source)
        {
            _source = source;

            try
            {
                string dir = Path.Combine(
                    Path.GetDirectoryName(typeof(Log140).Assembly.Location) ?? ".",
                    "..");
                dir = Path.GetFullPath(dir);
                _filePath = Path.Combine(dir, "QLTK140.log");

                lock (_lock)
                {
                    File.WriteAllText(_filePath,
                        "=== QLTK Client 140 log " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\n",
                        new UTF8Encoding(false));
                }
            }
            catch
            {
                _fileBroken = true;
            }
        }

        public static void Info(string msg) => Write(LogLevel.Info, msg);
        public static void Warn(string msg) => Write(LogLevel.Warning, msg);
        public static void Error(string msg) => Write(LogLevel.Error, msg);

        private static void Write(LogLevel level, string msg)
        {
            string line = $"[QLTK140] {msg}";

            // BepInEx log (an toàn từ mọi thread)
            try
            {
                if (_source != null)
                {
                    switch (level)
                    {
                        case LogLevel.Error: _source.LogError(line); break;
                        case LogLevel.Warning: _source.LogWarning(line); break;
                        default: _source.LogInfo(line); break;
                    }
                }
            }
            catch { }

            // File riêng - nguồn chẩn đoán độc lập
            if (_fileBroken || string.IsNullOrEmpty(_filePath)) return;

            try
            {
                string stamp = DateTime.Now.ToString("HH:mm:ss.fff");
                lock (_lock)
                {
                    File.AppendAllText(_filePath, $"{stamp} [{level}] {msg}\n", new UTF8Encoding(false));
                }
            }
            catch { _fileBroken = true; }
        }
    }
}
