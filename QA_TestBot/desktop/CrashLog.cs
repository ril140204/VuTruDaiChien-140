using System;
using System.IO;
using System.Text;

namespace QATestBotDesktop
{
    /// <summary>
    /// Ghi lỗi ra file để chẩn đoán khi app crash.
    /// </summary>
    public static class CrashLog
    {
        public static string LastPath { get; private set; } = "";

        public static void Write(string context, Exception ex)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QA_TestBot");
                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, "desktop_error.log");
                LastPath = path;

                var sb = new StringBuilder();
                sb.AppendLine("========================================");
                sb.AppendLine("Time   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.AppendLine("Context: " + context);
                sb.AppendLine("Root   : " + AppPaths.Root);
                sb.AppendLine("--------------------------------------");
                sb.AppendLine(ex?.ToString() ?? "(null exception)");
                sb.AppendLine();

                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
