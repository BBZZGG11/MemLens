using System;
using System.IO;
using System.Windows.Forms;

namespace MemLens
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0].StartsWith("--"))
            {
                Environment.ExitCode = Cli.Run(args);
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                LogError(e.ExceptionObject as Exception);
            };

            if (Environment.OSVersion.Version.Major < 10)
            {
                MessageBox.Show("MemLens 需要 Windows 10 或更高版本。\r\n\r\n当前系统版本：" + Environment.OSVersion.VersionString, "系统版本不受支持", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                LogError(e.Exception);
                MessageBox.Show(e.Exception.Message, "MemLens 发生错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                LogError(ex);
                MessageBox.Show(ex.ToString(), "MemLens 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void LogError(Exception ex)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "MemLens_error.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + (ex == null ? "unknown" : ex.ToString()) + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}
