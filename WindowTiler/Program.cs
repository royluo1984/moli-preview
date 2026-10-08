using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MoliWindowTiler
{
    internal static class Program
    {
        internal static readonly string GameExecutableName = "Reincarnation.exe";
        internal const string SingleInstanceMutexName = "MoliWindowTiler.SingleInstance.v1";
        internal const string ActivateMessageName = "MoliWindowTiler.Activate.v1";
        internal const string MainWindowTitle = "魔力宝贝窗口排列器";
        internal static readonly uint ActivateMessage = Native.RegisterWindowMessage(ActivateMessageName);
        internal static string GamePath;
        private static int errorDialogShown;
        private static Mutex singleInstanceMutex;

        [STAThread]
        private static void Main()
        {
            DpiSupport.Enable();
            bool createdNew;
            singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            if (!createdNew)
            {
                ActivateExistingInstance();
                singleInstanceMutex.Dispose();
                singleInstanceMutex = null;
                return;
            }

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
            try
            {
                Application.Run(new MainForm());
            }
            finally
            {
                if (singleInstanceMutex != null)
                {
                    try { singleInstanceMutex.ReleaseMutex(); }
                    catch (ApplicationException) { }
                    singleInstanceMutex.Dispose();
                    singleInstanceMutex = null;
                }
            }
        }

        private static void ActivateExistingInstance()
        {
            IntPtr existing = Native.FindWindow(null, MainWindowTitle);
            if (existing == IntPtr.Zero) return;

            if (ActivateMessage != 0)
                Native.PostMessage(existing, ActivateMessage, IntPtr.Zero, IntPtr.Zero);
            Native.ShowWindowAsync(existing, 9); // SW_RESTORE
            Native.BringWindowToTop(existing);
            Native.SetForegroundWindow(existing);
        }
    }
}
