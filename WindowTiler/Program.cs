using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MoliWindowTiler
{
    internal static class Program
    {
        internal static readonly string GameExecutableName = "Reincarnation.exe";
        internal static string GamePath;
        private static int errorDialogShown;

        [STAThread]
        private static void Main()
        {
            GamePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, GameExecutableName);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WindowTiler-error.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + e.Exception + Environment.NewLine + Environment.NewLine);
                }
                catch { }
                if (Interlocked.Exchange(ref errorDialogShown, 1) == 0)
                    MessageBox.Show("窗口排列器遇到界面错误，已记录到 WindowTiler-error.log。请重新刷新窗口列表。\n\n" + e.Exception.Message,
                        "魔力宝贝窗口排列器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm());
        }
    }
}
