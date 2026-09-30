using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MoliWindowTiler
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public Rectangle Rectangle { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowPlacement
    {
        public int Length, Flags, ShowCmd;
        public Point MinPosition, MaxPosition;
        public NativeRect NormalPosition;
    }

    public sealed class GameWindow
    {
        public IntPtr Handle;
        public int Pid;
        public long Started;
        public uint ThreadId;
        public string ThreadDescription;
        public string CharacterName;
        public string Title;
        public Rectangle Bounds;
        public Size ClientSize;
        public string Resolution;
        public bool Minimized;
        public bool Maximized;
        public WindowPlacement Placement;
        public string Key { get { return Pid + ":" + Started + ":" + Handle.ToInt64(); } }
    }

    internal static class Native
    {
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr value);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr value);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr hwnd, int command);
        [DllImport("user32.dll")] internal static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] internal static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr change);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] internal static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenThread(uint access, bool inherit, uint threadId);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern int GetThreadDescription(IntPtr thread, out IntPtr description);
        [DllImport("kernel32.dll")] internal static extern IntPtr LocalFree(IntPtr memory);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);

        internal static Rectangle WindowBounds(IntPtr hwnd)
        {
            NativeRect rect;
            if (!IsWindow(hwnd) || !GetWindowRect(hwnd, out rect)) return Rectangle.Empty;
            return rect.Rectangle;
        }

        internal static GameWindow ReadWindow(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero) return null;
            StringBuilder className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            if (!string.Equals(className.ToString(), "Reincar", StringComparison.OrdinalIgnoreCase)) return null;
            uint pid;
            uint threadId = GetWindowThreadProcessId(hwnd, out pid);
            IntPtr process = OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return null;
            long started, ended, kernel, user;
            try
            {
                StringBuilder path = new StringBuilder(32768);
                int length = path.Capacity;
                if (!QueryFullProcessImageName(process, 0, path, ref length)) return null;
                string executableName = Path.GetFileName(path.ToString());
                if (!string.Equals(executableName, Program.GameExecutableName, StringComparison.OrdinalIgnoreCase)) return null;
                if (!GetProcessTimes(process, out started, out ended, out kernel, out user)) return null;
            }
            finally { CloseHandle(process); }
            NativeRect bounds, client;
            WindowPlacement placement = new WindowPlacement { Length = Marshal.SizeOf(typeof(WindowPlacement)) };
            if (!GetWindowRect(hwnd, out bounds) || !GetClientRect(hwnd, out client) || !GetWindowPlacement(hwnd, ref placement)) return null;
            bool minimized = IsIconic(hwnd), maximized = IsZoomed(hwnd);
            Rectangle outer = bounds.Rectangle;
            if (minimized || maximized)
            {
                outer = placement.NormalPosition.Rectangle;
            }
            if (outer.Width <= 0 || outer.Height <= 0) return null;
            StringBuilder title = new StringBuilder(1024);
            GetWindowText(hwnd, title, title.Capacity);
            string threadDescription = ReadThreadDescription(threadId);
            string characterName = CharacterFromWindow(title.ToString(), threadDescription, threadId);
            Size clientSize = client.Rectangle.Size;
            string resolution = clientSize.Width + "×" + clientSize.Height;
            if (minimized) resolution = "最小化 · 按还原尺寸预览";
            return new GameWindow
            {
                Handle = hwnd, Pid = (int)pid, Started = started, ThreadId = threadId,
                ThreadDescription = threadDescription, CharacterName = characterName,
                Title = title.ToString(),
                Bounds = outer, ClientSize = clientSize, Resolution = resolution,
                Minimized = minimized, Maximized = maximized, Placement = placement
            };
        }

        private static string ReadThreadDescription(uint threadId)
        {
            IntPtr thread = IntPtr.Zero;
            IntPtr description = IntPtr.Zero;
            try
            {
                thread = OpenThread(0x0800, false, threadId); // THREAD_QUERY_LIMITED_INFORMATION
                if (thread == IntPtr.Zero) return "";
                if (GetThreadDescription(thread, out description) != 0 || description == IntPtr.Zero) return "";
                return Marshal.PtrToStringUni(description) ?? "";
            }
            catch (EntryPointNotFoundException) { return ""; }
            catch { return ""; }
            finally
            {
                if (description != IntPtr.Zero) LocalFree(description);
                if (thread != IntPtr.Zero) CloseHandle(thread);
            }
        }

        internal static string CharacterFromWindow(string title, string threadDescription, uint threadId)
        {
            string threadName = (threadDescription ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(threadName) && !LooksGeneric(threadName))
                return CleanCharacter(threadName);

            string value = (title ?? "").Trim();
            int separator = value.LastIndexOf("--", StringComparison.Ordinal);
            if (separator >= 0 && separator + 2 < value.Length)
                value = value.Substring(separator + 2).Trim();
            else
            {
                int reincarnation = value.IndexOf("Reincarnation", StringComparison.OrdinalIgnoreCase);
                if (reincarnation >= 0) value = value.Substring(reincarnation + "Reincarnation".Length).Trim();
                value = value.Trim('-', ' ', '\t', '[', ']');
            }
            value = CleanCharacter(value);
            return string.IsNullOrWhiteSpace(value) ? "未命名-线程" + threadId : value;
        }

        private static string CleanCharacter(string value)
        {
            value = (value ?? "").Trim();
            return value.Trim('-', ' ', '\t', '[', ']');
        }

        private static bool LooksGeneric(string value)
        {
            return string.Equals(value, "Reincarnation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "cg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "主线程", StringComparison.OrdinalIgnoreCase);
        }

        internal static List<GameWindow> FindGames()
        {
            List<GameWindow> games = new List<GameWindow>();
            EnumProc callback = delegate(IntPtr hwnd, IntPtr data)
            {
                GameWindow game = ReadWindow(hwnd);
                if (game != null) games.Add(game);
                return true;
            };
            if (!EnumWindows(callback, IntPtr.Zero)) throw new InvalidOperationException("读取窗口列表失败，请刷新重试。");
            games.Sort(delegate(GameWindow a, GameWindow b)
            {
                int order = a.Started.CompareTo(b.Started);
                return order != 0 ? order : a.Handle.ToInt64().CompareTo(b.Handle.ToInt64());
            });
            return games;
        }
    }
}
