using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
        public string TargetProfileId;
        public string TargetProfileName;
        public List<string> IdentityCandidates;
        public string Key { get { return Pid + ":" + Started + ":" + Handle.ToInt64(); } }
    }

    internal sealed class WindowSample
    {
        public IntPtr Handle;
        public int Pid;
        public uint ThreadId;
        public string ExecutableName;
        public string ExecutablePath;
        public string WindowClass;
        public string Title;
        public string ThreadDescription;

        public override string ToString()
        {
            string title = string.IsNullOrWhiteSpace(Title) ? "（无标题）" : Title.Trim();
            string executable = string.IsNullOrWhiteSpace(ExecutableName) ? "未知程序" : ExecutableName;
            return title + "  [" + executable + "]  PID " + Pid;
        }
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

        internal static List<WindowSample> FindWindowSamples()
        {
            List<WindowSample> samples = new List<WindowSample>();
            EnumProc callback = delegate(IntPtr hwnd, IntPtr data)
            {
                if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero) return true;
                StringBuilder title = new StringBuilder(1024);
                GetWindowText(hwnd, title, title.Capacity);
                StringBuilder className = new StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);
                uint pid;
                uint threadId = GetWindowThreadProcessId(hwnd, out pid);
                IntPtr process = OpenProcess(0x1000, false, pid);
                if (process == IntPtr.Zero) return true;
                string executablePath = "";
                try
                {
                    StringBuilder path = new StringBuilder(32768);
                    int length = path.Capacity;
                    if (!QueryFullProcessImageName(process, 0, path, ref length)) return true;
                    executablePath = path.ToString();
                }
                finally { CloseHandle(process); }
                string executableName = Path.GetFileName(executablePath);
                if (string.Equals(executableName, "MoliWindowTiler.exe", StringComparison.OrdinalIgnoreCase)) return true;
                samples.Add(new WindowSample
                {
                    Handle = hwnd,
                    Pid = (int)pid,
                    ThreadId = threadId,
                    ExecutableName = executableName,
                    ExecutablePath = executablePath,
                    WindowClass = className.ToString(),
                    Title = title.ToString(),
                    ThreadDescription = ReadThreadDescription(threadId)
                });
                return true;
            };
            if (!EnumWindows(callback, IntPtr.Zero)) throw new InvalidOperationException("读取窗口列表失败，请刷新重试。");
            return samples.OrderBy(sample => sample.ExecutableName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(sample => sample.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static GameWindow ReadWindow(IntPtr hwnd)
        {
            return ReadWindow(hwnd, TargetProfile.CreateMoliDefault());
        }

        internal static GameWindow ReadWindow(IntPtr hwnd, TargetProfile profile)
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero) return null;
            profile = profile ?? TargetProfile.CreateMoliDefault();
            StringBuilder className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
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
                if (!profile.MatchesExecutableAndClass(executableName, className.ToString())) return null;
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
            if (!profile.MatchesTitle(title.ToString())) return null;
            string threadDescription = ReadThreadDescription(threadId);
            List<string> identityCandidates = profile.ExtractIdentityCandidates(
                title.ToString(), threadDescription, threadId);
            string characterName = FirstIdentity(identityCandidates, threadId);
            Size clientSize = client.Rectangle.Size;
            string resolution = clientSize.Width + "×" + clientSize.Height;
            if (minimized) resolution = "最小化 · 按还原尺寸预览";
            return new GameWindow
            {
                Handle = hwnd, Pid = (int)pid, Started = started, ThreadId = threadId,
                ThreadDescription = threadDescription, CharacterName = characterName,
                Title = title.ToString(),
                Bounds = outer, ClientSize = clientSize, Resolution = resolution,
                Minimized = minimized, Maximized = maximized, Placement = placement,
                TargetProfileId = profile.Id ?? "",
                TargetProfileName = profile.Name ?? "",
                IdentityCandidates = identityCandidates
            };
        }

        private static string FirstIdentity(IList<string> candidates, uint threadId)
        {
            if (candidates != null)
            {
                foreach (string candidate in candidates)
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && !LooksGeneric(candidate))
                        return CleanCharacter(candidate);
                }
            }
            return "未命名-线程" + threadId;
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
            foreach (string value in CharacterCandidates(title, threadDescription, threadId))
            {
                if (!string.IsNullOrWhiteSpace(value) && !LooksGeneric(value))
                    return CleanCharacter(value);
            }
            return "未命名-线程" + threadId;
        }

        internal static List<string> CharacterCandidates(GameWindow game)
        {
            if (game == null) return new List<string>();
            if (game.IdentityCandidates != null && game.IdentityCandidates.Count > 0)
                return new List<string>(game.IdentityCandidates);
            List<string> result = CharacterCandidates(game.Title, game.ThreadDescription, game.ThreadId);
            AddCandidate(result, game.CharacterName);
            return result;
        }

        internal static List<string> CharacterCandidates(string title, string threadDescription, uint threadId)
        {
            List<string> result = new List<string>();
            AddSourceCandidates(result, threadDescription);
            AddSourceCandidates(result, title);
            AddCandidate(result, "未命名-线程" + threadId);
            return result;
        }

        private static void AddSourceCandidates(List<string> result, string source)
        {
            string value = CleanCharacter(source);
            if (string.IsNullOrWhiteSpace(value)) return;

            int separator = value.LastIndexOf("--", StringComparison.Ordinal);
            if (separator >= 0 && separator + 2 < value.Length)
                AddCandidate(result, value.Substring(separator + 2));

            int closingBracket = value.LastIndexOf(']');
            if (separator < 0 && closingBracket >= 0 && closingBracket + 1 < value.Length)
                AddCandidate(result, value.Substring(closingBracket + 1));

            int closingParenthesis = value.LastIndexOf(')');
            int openingParenthesis = value.LastIndexOf('(');
            if (openingParenthesis >= 0 && closingParenthesis > openingParenthesis + 1)
                AddCandidate(result, value.Substring(openingParenthesis + 1,
                    closingParenthesis - openingParenthesis - 1));

            int dash = value.LastIndexOf('-');
            if (dash >= 0 && dash + 1 < value.Length)
                AddCandidate(result, value.Substring(dash + 1));

            int reincarnation = value.IndexOf("Reincarnation", StringComparison.OrdinalIgnoreCase);
            if (separator < 0 && reincarnation >= 0 && reincarnation + "Reincarnation".Length < value.Length)
                AddCandidate(result, value.Substring(reincarnation + "Reincarnation".Length));

            AddCandidate(result, value);
        }

        private static void AddCandidate(List<string> result, string value)
        {
            value = CleanCharacter(value);
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!result.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))) result.Add(value);
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
            return FindGames(TargetProfile.CreateMoliDefault());
        }

        internal static List<GameWindow> FindGames(TargetProfile profile)
        {
            List<GameWindow> games = new List<GameWindow>();
            EnumProc callback = delegate(IntPtr hwnd, IntPtr data)
            {
                GameWindow game = ReadWindow(hwnd, profile);
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
