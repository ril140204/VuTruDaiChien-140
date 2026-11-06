using System;
using System.Windows.Forms;

namespace QATestBotDesktop
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                // Windows console mac dinh la cp1258 -> khong in duoc mot so chu Viet Nam.
                // Dung wrapper de ghi UTF-8 thay vi crash khi encode that.
                Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                CrashLog.Write("Application.ThreadException", e.Exception);
                MessageBox.Show("Lỗi không mong đợi:\n\n" + e.Exception.Message +
                    "\n\nChi tiết: " + CrashLog.LastPath,
                    "QA TestBot - Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                CrashLog.Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);
                MessageBox.Show("Lỗi nghiêm trọng:\n\n" + (e.ExceptionObject?.ToString() ?? "unknown") +
                    "\n\nChi tiết: " + CrashLog.LastPath,
                    "QA TestBot - Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                CrashLog.Write("Application.Run", ex);
                MessageBox.Show("Không khởi động được ứng dụng:\n\n" + ex.Message +
                    "\n\nChi tiết: " + CrashLog.LastPath,
                    "QA TestBot - Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
