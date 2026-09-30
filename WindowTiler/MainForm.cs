using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MoliWindowTiler
{
    internal sealed class MonitorOption
    {
        public readonly Screen Screen;
        private readonly string text;

        public MonitorOption(string text, Screen screen)
        {
            this.text = text;
            Screen = screen;
        }

        public override string ToString() { return text; }
    }

    internal sealed class WindowSnapshot
    {
        public IntPtr Handle;
        public WindowPlacement Placement;
        public Rectangle Bounds;
    }

    internal sealed class LayoutChoice
    {
        public LayoutPlan Plan;
        public Size ClientSize;
        public bool Fits;
    }

    internal sealed class PreviewPanel : Panel
    {
        private LayoutPlan plan;
        private List<GameWindow> windows;
        private Rectangle area;
        private Size clientSize;

        public PreviewPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(245, 247, 250);
            BorderStyle = BorderStyle.FixedSingle;
        }

        public void SetPlan(LayoutPlan plan, IList<GameWindow> windows, Rectangle area, Size clientSize)
        {
            this.plan = plan;
            this.windows = windows == null ? null : new List<GameWindow>(windows);
            this.area = area;
            this.clientSize = clientSize;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            using (Font titleFont = new Font(Font, FontStyle.Bold))
            using (Brush textBrush = new SolidBrush(Color.FromArgb(70, 78, 90)))
            {
                if (plan == null || windows == null || windows.Count == 0)
                {
                    string empty = "刷新后选择游戏窗口，右侧会显示排列预览";
                    SizeF measured = g.MeasureString(empty, Font);
                    g.DrawString(empty, Font, textBrush,
                        Math.Max(8, (ClientSize.Width - measured.Width) / 2),
                        Math.Max(8, (ClientSize.Height - measured.Height) / 2));
                    return;
                }

                int margin = 18;
                float scale = Math.Min(
                    (ClientSize.Width - margin * 2f) / Math.Max(1, area.Width),
                    (ClientSize.Height - margin * 2f - 26) / Math.Max(1, area.Height));
                scale = Math.Max(0.03f, scale);
                float left = (ClientSize.Width - area.Width * scale) / 2f;
                float top = 26 + (ClientSize.Height - 26 - area.Height * scale) / 2f;
                g.DrawString("工作区 " + area.Width + "×" + area.Height + "　当前客户端 " + clientSize.Width + "×" + clientSize.Height,
                    titleFont, textBrush, 10, 7);
                using (Pen areaPen = new Pen(Color.FromArgb(160, 170, 184)))
                using (Brush areaBrush = new SolidBrush(Color.White))
                {
                    RectangleF frame = new RectangleF(left, top, area.Width * scale, area.Height * scale);
                    g.FillRectangle(areaBrush, frame);
                    g.DrawRectangle(areaPen, frame.X, frame.Y, frame.Width, frame.Height);
                }

                Color[] colors = { Color.FromArgb(74, 137, 220), Color.FromArgb(67, 160, 71),
                    Color.FromArgb(239, 126, 55), Color.FromArgb(142, 91, 190),
                    Color.FromArgb(0, 150, 136), Color.FromArgb(220, 75, 75) };
                for (int i = 0; i < plan.Windows.Length; i++)
                {
                    Rectangle r = plan.Windows[i];
                    RectangleF draw = new RectangleF(
                        left + (r.Left - area.Left) * scale,
                        top + (r.Top - area.Top) * scale,
                        Math.Max(2, r.Width * scale), Math.Max(2, r.Height * scale));
                    using (Brush fill = new SolidBrush(Color.FromArgb(185, colors[i % colors.Length])))
                    using (Pen border = new Pen(colors[i % colors.Length], 1.5f))
                    {
                        g.FillRectangle(fill, draw);
                        g.DrawRectangle(border, draw.X, draw.Y, draw.Width, draw.Height);
                    }
                    string label = (i + 1) + "  " + (i < windows.Count ? ShortTitle(windows[i].Title) : "游戏窗口");
                    using (Brush labelBrush = new SolidBrush(Color.White))
                    {
                        RectangleF labelArea = new RectangleF(draw.X + 4, draw.Y + 4,
                            Math.Max(2, draw.Width - 8), Math.Max(2, draw.Height - 8));
                        g.DrawString(label, Font, labelBrush, labelArea);
                    }
                }
            }
        }

        private static string ShortTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "未命名客户端";
            string value = title.Trim();
            return value.Length > 18 ? value.Substring(0, 18) + "…" : value;
        }
    }

    public sealed class MainForm : Form
    {
        private readonly ListView windowList = new ListView();
        private readonly ComboBox monitorBox = new ComboBox();
        private readonly ComboBox columnsBox = new ComboBox();
        private readonly ComboBox alignmentBox = new ComboBox();
        private readonly TextBox customRowsBox = new TextBox();
        private readonly CheckBox switcherBox = new CheckBox();
        private readonly NumericUpDown marginBox = new NumericUpDown();
        private readonly NumericUpDown gapBox = new NumericUpDown();
        private readonly Label statusLabel = new Label();
        private readonly Label planLabel = new Label();
        private readonly Button refreshButton = new Button();
        private readonly Button selectAllButton = new Button();
        private readonly Button clearButton = new Button();
        private readonly Button arrangeButton = new Button();
        private readonly Button restoreButton = new Button();
        private readonly Button closeAllButton = new Button();
        private readonly PreviewPanel preview = new PreviewPanel();
        private readonly Dictionary<string, WindowSnapshot> snapshots = new Dictionary<string, WindowSnapshot>();
        private readonly PositionStore positionStore;
        private readonly SettingsStore settingsStore;
        private readonly SwitcherOverlay switcher;
        private List<GameWindow> games = new List<GameWindow>();
        private LayoutPlan currentPlan;
        private Size currentClientSize;
        private Rectangle currentArea;
        private bool controlsReady;
        private bool applyingSettings;
        private bool updatingMonitors;
        private bool refreshingWindows;
        private bool hasSavedSelection;
        private string preferredMonitor;
        private readonly HashSet<string> preferredCharacters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public MainForm()
        {
            positionStore = new PositionStore();
            settingsStore = new SettingsStore();
            switcher = new SwitcherOverlay(ActivateGame);
            switcher.UserClosed += delegate { if (!IsDisposed && !Disposing) switcherBox.Checked = false; };
            Text = "魔力宝贝窗口排列器";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(860, 560);
            ClientSize = new Size(1080, 680);
            Icon = SystemIcons.Application;
            BuildControls();
            ApplySettings(settingsStore.Load());
            controlsReady = true;
            FormClosing += delegate
            {
                CaptureCurrentPositions();
                SaveSettings();
                if (switcher != null && !switcher.IsDisposed) switcher.Close();
            };
            RefreshMonitors();
            RefreshWindows();
        }

        private void BuildControls()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 182));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            Controls.Add(root);

            GroupBox options = new GroupBox { Text = "排列设置", Dock = DockStyle.Fill, Padding = new Padding(8, 20, 8, 4) };
            root.Controls.Add(options, 0, 0);
            FlowLayoutPanel optionFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0)
            };
            options.Controls.Add(optionFlow);

            optionFlow.Controls.Add(new Label { Text = "目标屏幕", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
            monitorBox.DropDownStyle = ComboBoxStyle.DropDownList;
            monitorBox.Width = 190;
            monitorBox.SelectedIndexChanged += delegate { SaveSettings(); UpdatePlan(); };
            optionFlow.Controls.Add(monitorBox);

            optionFlow.Controls.Add(new Label { Text = "排列方式", AutoSize = true, Margin = new Padding(10, 7, 3, 0) });
            columnsBox.DropDownStyle = ComboBoxStyle.DropDownList;
            columnsBox.Width = 118;
            columnsBox.Items.AddRange(new object[] { "智能排列", "一行横排", "一列竖排", "两行均匀", "三行均匀", "自定义组合" });
            columnsBox.SelectedIndex = 0;
            columnsBox.SelectedIndexChanged += delegate
            {
                customRowsBox.Enabled = columnsBox.SelectedIndex == 5;
                SaveSettings();
                UpdatePlan();
            };
            optionFlow.Controls.Add(columnsBox);

            optionFlow.Controls.Add(new Label { Text = "行组合", AutoSize = true, Margin = new Padding(10, 7, 3, 0) });
            customRowsBox.Text = "3,3";
            customRowsBox.Width = 62;
            customRowsBox.Enabled = false;
            customRowsBox.Margin = new Padding(0, 3, 3, 0);
            customRowsBox.TextChanged += delegate
            {
                SaveSettings();
                if (columnsBox.SelectedIndex == 5) UpdatePlan();
            };
            optionFlow.Controls.Add(customRowsBox);
            optionFlow.Controls.Add(new Label { Text = "例：3,3 / 2,2,2", AutoSize = true, Margin = new Padding(1, 7, 3, 0) });

            optionFlow.Controls.Add(new Label { Text = "对齐方式", AutoSize = true, Margin = new Padding(10, 7, 3, 0) });
            alignmentBox.DropDownStyle = ComboBoxStyle.DropDownList;
            alignmentBox.Width = 92;
            alignmentBox.Items.AddRange(new object[]
            {
                "左上", "上中", "右上",
                "左中", "居中", "右中",
                "左下", "下中", "右下"
            });
            alignmentBox.SelectedIndex = (int)LayoutAlignment.Center;
            alignmentBox.SelectedIndexChanged += delegate { SaveSettings(); UpdatePlan(); };
            optionFlow.Controls.Add(alignmentBox);

            optionFlow.Controls.Add(new Label { Text = "窗口间距", AutoSize = true, Margin = new Padding(10, 7, 3, 0) });
            gapBox.Minimum = 0;
            gapBox.Maximum = 300;
            gapBox.Value = 0;
            gapBox.Width = 54;
            gapBox.Margin = new Padding(0, 3, 3, 0);
            gapBox.ValueChanged += delegate { SaveSettings(); UpdatePlan(); };
            optionFlow.Controls.Add(gapBox);
            optionFlow.Controls.Add(new Label { Text = "像素（默认 0）", AutoSize = true, Margin = new Padding(1, 7, 3, 0) });

            optionFlow.Controls.Add(new Label
            {
                Text = "排列时保持客户端当前分辨率",
                AutoSize = true,
                Margin = new Padding(12, 7, 3, 0)
            });

            switcherBox.Text = "显示点击切换浮层";
            switcherBox.Checked = true;
            switcherBox.AutoSize = true;
            switcherBox.Margin = new Padding(12, 5, 3, 0);
            switcherBox.CheckedChanged += delegate { SaveSettings(); UpdateSwitcher(); };
            optionFlow.Controls.Add(switcherBox);

            optionFlow.Controls.Add(new Label { Text = "屏幕边距", AutoSize = true, Margin = new Padding(10, 7, 3, 0) });
            marginBox.Minimum = 0;
            marginBox.Maximum = 80;
            marginBox.Value = 8;
            marginBox.Width = 54;
            marginBox.Margin = new Padding(0, 3, 3, 0);
            marginBox.ValueChanged += delegate { SaveSettings(); UpdatePlan(); };
            optionFlow.Controls.Add(marginBox);
            optionFlow.Controls.Add(new Label { Text = "像素", AutoSize = true, Margin = new Padding(1, 7, 3, 0) });

            GroupBox windowsGroup = new GroupBox { Text = "已发现的游戏客户端（默认全选）", Dock = DockStyle.Fill, Padding = new Padding(7, 20, 7, 7) };
            root.Controls.Add(windowsGroup, 0, 1);
            windowList.Dock = DockStyle.Fill;
            windowList.CheckBoxes = true;
            windowList.FullRowSelect = true;
            windowList.GridLines = true;
            windowList.MultiSelect = false;
            windowList.View = View.Details;
            windowList.HideSelection = false;
            windowList.Columns.Add("PID", 58);
            windowList.Columns.Add("人物", 150);
            windowList.Columns.Add("线程", 65);
            windowList.Columns.Add("客户端标题", 220);
            windowList.Columns.Add("分辨率", 116);
            windowList.Columns.Add("状态", 85);
            windowList.Columns.Add("位置", 120);
            windowList.ItemChecked += delegate
            {
                if (refreshingWindows) return;
                UpdatePlan();
            };
            windowsGroup.Controls.Add(windowList);

            GroupBox previewGroup = new GroupBox { Text = "排列预览", Dock = DockStyle.Fill, Padding = new Padding(7, 20, 7, 7) };
            root.Controls.Add(previewGroup, 1, 0);
            root.SetRowSpan(previewGroup, 2);
            preview.Dock = DockStyle.Fill;
            previewGroup.Controls.Add(preview);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, Padding = new Padding(0, 3, 0, 0)
            };
            root.Controls.Add(actions, 0, 2);
            refreshButton.Text = "刷新窗口";
            refreshButton.Width = 82;
            refreshButton.Click += delegate { RefreshWindows(); };
            actions.Controls.Add(refreshButton);
            selectAllButton.Text = "全选";
            selectAllButton.Width = 58;
            selectAllButton.Click += delegate { SetAllChecked(true); };
            actions.Controls.Add(selectAllButton);
            clearButton.Text = "清空选择";
            clearButton.Width = 76;
            clearButton.Click += delegate { SetAllChecked(false); };
            actions.Controls.Add(clearButton);
            arrangeButton.Text = "一键排列";
            arrangeButton.Width = 100;
            arrangeButton.Font = new Font(arrangeButton.Font, FontStyle.Bold);
            arrangeButton.Click += delegate { ArrangeWindows(); };
            actions.Controls.Add(arrangeButton);
            restoreButton.Text = "恢复原位置";
            restoreButton.Width = 92;
            restoreButton.Click += delegate { RestoreWindows(); };
            actions.Controls.Add(restoreButton);
            closeAllButton.Text = "关闭所有客户端";
            closeAllButton.Width = 120;
            closeAllButton.ForeColor = Color.DarkRed;
            closeAllButton.Enabled = false;
            closeAllButton.Click += delegate { CloseAllClients(); };
            actions.Controls.Add(closeAllButton);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.AutoEllipsis = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(8, 0, 8, 0);
            root.Controls.Add(statusLabel, 1, 2);

            planLabel.AutoSize = false;
            planLabel.Dock = DockStyle.Bottom;
            planLabel.Height = 42;
            planLabel.TextAlign = ContentAlignment.MiddleLeft;
            planLabel.Padding = new Padding(5, 2, 5, 2);
            previewGroup.Controls.Add(planLabel);
        }

        private void ApplySettings(AppSettings settings)
        {
            if (settings == null) return;
            applyingSettings = true;
            try
            {
                columnsBox.SelectedIndex = settings.LayoutMode >= 0 && settings.LayoutMode < columnsBox.Items.Count
                    ? settings.LayoutMode : 0;
                customRowsBox.Text = string.IsNullOrWhiteSpace(settings.CustomRows) ? "3,3" : settings.CustomRows;
                alignmentBox.SelectedIndex = settings.Alignment >= 0 && settings.Alignment < alignmentBox.Items.Count
                    ? settings.Alignment : (int)LayoutAlignment.Center;
                gapBox.Value = Math.Max(gapBox.Minimum, Math.Min(gapBox.Maximum, settings.Gap));
                marginBox.Value = Math.Max(marginBox.Minimum, Math.Min(marginBox.Maximum, settings.Margin));
                switcherBox.Checked = settings.ShowSwitcher;
                preferredMonitor = settings.Monitor ?? "";
                hasSavedSelection = settings.HasSelection;
                preferredCharacters.Clear();
                if (settings.SelectedCharacters != null)
                {
                    foreach (string character in settings.SelectedCharacters)
                        if (!string.IsNullOrWhiteSpace(character)) preferredCharacters.Add(character.Trim());
                }
                customRowsBox.Enabled = columnsBox.SelectedIndex == 5;
            }
            finally
            {
                applyingSettings = false;
            }
        }

        private string CurrentMonitorName()
        {
            MonitorOption option = monitorBox.SelectedItem as MonitorOption;
            if (option == null) return null;
            return option.Screen == null ? "" : option.Screen.DeviceName;
        }

        private void SaveSettings()
        {
            if (!controlsReady || applyingSettings || updatingMonitors) return;
            try
            {
                string monitor = CurrentMonitorName();
                if (monitor != null) preferredMonitor = monitor;
                if (windowList.Items.Count > 0)
                {
                    preferredCharacters.Clear();
                    foreach (ListViewItem item in windowList.Items)
                    {
                        GameWindow game = item.Tag as GameWindow;
                        if (item.Checked && game != null && !string.IsNullOrWhiteSpace(game.CharacterName))
                            preferredCharacters.Add(game.CharacterName.Trim());
                    }
                    hasSavedSelection = true;
                }
                settingsStore.Save(new AppSettings
                {
                    LayoutMode = columnsBox.SelectedIndex < 0 ? 0 : columnsBox.SelectedIndex,
                    CustomRows = customRowsBox.Text,
                    Alignment = alignmentBox.SelectedIndex < 0 ? (int)LayoutAlignment.Center : alignmentBox.SelectedIndex,
                    Gap = (int)gapBox.Value,
                    Margin = (int)marginBox.Value,
                    Monitor = preferredMonitor ?? "",
                    ShowSwitcher = switcherBox.Checked,
                    HasSelection = hasSavedSelection,
                    SelectedCharacters = preferredCharacters.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList()
                });
            }
            catch
            {
                // Settings persistence must never interrupt the window manager UI.
            }
        }

        private void RefreshMonitors()
        {
            string selected = CurrentMonitorName();
            if (selected == null) selected = preferredMonitor ?? "";
            updatingMonitors = true;
            try
            {
                monitorBox.BeginUpdate();
                monitorBox.Items.Clear();
                monitorBox.Items.Add(new MonitorOption("当前客户端所在屏幕", null));
                foreach (Screen screen in Screen.AllScreens.OrderBy(s => s.DeviceName))
                    monitorBox.Items.Add(new MonitorOption(screen.DeviceName + "  " + screen.WorkingArea.Width + "×" + screen.WorkingArea.Height, screen));
                monitorBox.SelectedIndex = 0;
                for (int i = 1; i < monitorBox.Items.Count; i++)
                {
                    MonitorOption option = (MonitorOption)monitorBox.Items[i];
                    if (option.Screen.DeviceName == selected) monitorBox.SelectedIndex = i;
                }
                monitorBox.EndUpdate();
            }
            finally
            {
                updatingMonitors = false;
            }
            string current = CurrentMonitorName();
            preferredMonitor = current ?? "";
        }

        private void RefreshWindows()
        {
            CaptureCurrentPositions();
            Dictionary<string, bool> checkedState = new Dictionary<string, bool>();
            foreach (ListViewItem item in windowList.Items)
            {
                GameWindow game = item.Tag as GameWindow;
                if (game != null) checkedState[game.Key] = item.Checked;
            }
            refreshingWindows = true;
            try
            {
                games = Native.FindGames();
                closeAllButton.Enabled = games.Count > 0;
                ApplySavedPositions(games);
                windowList.BeginUpdate();
                windowList.Items.Clear();
                foreach (GameWindow game in games)
                {
                    ListViewItem item = new ListViewItem(game.Pid.ToString());
                    item.SubItems.Add(game.CharacterName);
                    item.SubItems.Add(game.ThreadId.ToString());
                    item.SubItems.Add(string.IsNullOrWhiteSpace(game.Title) ? "（无标题）" : game.Title.Trim());
                    item.SubItems.Add(game.Resolution);
                    item.SubItems.Add(game.Minimized ? "最小化" : game.Maximized ? "最大化" : "正常");
                    item.SubItems.Add(game.Bounds.Left + "," + game.Bounds.Top + "  " + game.Bounds.Width + "×" + game.Bounds.Height);
                    item.Tag = game;
                    if (checkedState.ContainsKey(game.Key))
                        item.Checked = checkedState[game.Key];
                    else if (hasSavedSelection)
                        item.Checked = preferredCharacters.Contains(game.CharacterName);
                    else
                        item.Checked = true;
                    windowList.Items.Add(item);
                }
                windowList.EndUpdate();
                RefreshMonitors();
                statusLabel.Text = games.Count == 0
                    ? "未找到 Reincarnation.exe 游戏窗口，请先启动客户端后刷新。"
                    : "已发现 " + games.Count + " 个客户端，勾选需要排列的窗口；位置按人物名称自动记忆。";
            }
            catch (Exception ex)
            {
                closeAllButton.Enabled = false;
                statusLabel.Text = "读取窗口失败：" + ex.Message;
            }
            finally
            {
                refreshingWindows = false;
            }
            UpdateSwitcher();
            UpdatePlan();
        }

        private void CaptureCurrentPositions()
        {
            if (positionStore == null || games == null || games.Count == 0) return;
            bool changed = false;
            foreach (GameWindow game in games.ToList())
            {
                try
                {
                    Rectangle bounds = Native.WindowBounds(game.Handle);
                    if (bounds.Width > 0 && bounds.Height > 0)
                    {
                        positionStore.Set(game, bounds);
                        changed = true;
                    }
                }
                catch { }
            }
            if (changed) positionStore.Save();
        }

        private void ApplySavedPositions(List<GameWindow> found)
        {
            if (found == null) return;
            foreach (GameWindow game in found)
            {
                SavedWindowPosition saved = positionStore.Get(game.CharacterName);
                if (saved == null) continue;
                try
                {
                    Screen targetScreen = Screen.AllScreens.FirstOrDefault(s =>
                        !string.IsNullOrWhiteSpace(saved.Screen) &&
                        string.Equals(s.DeviceName, saved.Screen, StringComparison.OrdinalIgnoreCase));
                    if (targetScreen == null) targetScreen = Screen.FromHandle(game.Handle) ?? Screen.PrimaryScreen;
                    // Position records from older versions also contain a size. Keep the
                    // live outer size here so restoring a record never changes the game
                    // client's rendering resolution or stretches its frame.
                    Rectangle liveBounds = Native.WindowBounds(game.Handle);
                    Size liveSize = liveBounds.Width > 0 && liveBounds.Height > 0
                        ? liveBounds.Size
                        : game.Bounds.Size;
                    Rectangle savedPosition = new Rectangle(saved.Bounds.Left, saved.Bounds.Top,
                        Math.Max(1, liveSize.Width), Math.Max(1, liveSize.Height));
                    Rectangle target = ClampToWorkArea(savedPosition, targetScreen.WorkingArea);
                    if (Native.SetWindowPos(game.Handle, IntPtr.Zero, target.Left, target.Top,
                        target.Width, target.Height, 0x0001 | 0x0004 | 0x0010 | 0x0040 | 0x0200))
                        game.Bounds = new Rectangle(target.Left, target.Top, liveSize.Width, liveSize.Height);
                }
                catch { }
            }
        }

        private static Rectangle ClampToWorkArea(Rectangle bounds, Rectangle area)
        {
            int width = Math.Min(Math.Max(1, bounds.Width), area.Width);
            int height = Math.Min(Math.Max(1, bounds.Height), area.Height);
            int left = Math.Max(area.Left, Math.Min(bounds.Left, area.Right - width));
            int top = Math.Max(area.Top, Math.Min(bounds.Top, area.Bottom - height));
            return new Rectangle(left, top, width, height);
        }

        private void SetAllChecked(bool value)
        {
            windowList.BeginUpdate();
            foreach (ListViewItem item in windowList.Items) item.Checked = value;
            windowList.EndUpdate();
            UpdatePlan();
        }

        private List<GameWindow> SelectedGames()
        {
            if (windowList.IsDisposed) return new List<GameWindow>();
            List<GameWindow> result = new List<GameWindow>();
            foreach (ListViewItem item in windowList.Items)
            {
                if (item == null || !item.Checked) continue;
                GameWindow game = item.Tag as GameWindow;
                if (game != null && Native.IsWindow(game.Handle)) result.Add(game);
            }
            return result;
        }

        private Screen TargetScreen(List<GameWindow> selected)
        {
            MonitorOption option = monitorBox.SelectedItem as MonitorOption;
            if (option != null && option.Screen != null) return option.Screen;
            try
            {
                IntPtr foreground = Native.GetForegroundWindow();
                GameWindow active = selected.FirstOrDefault(g => g.Handle == foreground);
                if (active != null)
                {
                    Screen activeScreen = Screen.FromHandle(active.Handle);
                    if (activeScreen != null) return activeScreen;
                }
                if (selected.Count > 0)
                {
                    Screen gameScreen = Screen.FromHandle(selected[0].Handle);
                    if (gameScreen != null) return gameScreen;
                }
            }
            catch { }
            return Screen.PrimaryScreen;
        }

        private int[] RequestedRows(int count)
        {
            if (count <= 0) return new int[0];
            switch (columnsBox.SelectedIndex)
            {
                case 1:
                    return new[] { count };
                case 2:
                    return Enumerable.Repeat(1, count).ToArray();
                case 3:
                    return BalancedRows(count, Math.Min(2, count));
                case 4:
                    return BalancedRows(count, Math.Min(3, count));
                case 5:
                    return ParseRows(customRowsBox.Text, count);
                default:
                    // Six clients are easiest to scan as two balanced rows. Keep this
                    // deterministic instead of letting the shape scorer choose 2 + 4.
                    return count == 6 ? new[] { 3, 3 } : null;
            }
        }

        private static int[] BalancedRows(int count, int rowCount)
        {
            rowCount = Math.Max(1, Math.Min(count, rowCount));
            int baseCount = count / rowCount;
            int remainder = count % rowCount;
            int[] rows = new int[rowCount];
            for (int i = 0; i < rows.Length; i++) rows[i] = baseCount + (i < remainder ? 1 : 0);
            return rows;
        }

        private static int[] ParseRows(string text, int count)
        {
            string value = (text ?? "").Trim();
            if (value.Length == 0) throw new ArgumentException("请输入行组合，例如 3,3 或 2,2,2。");
            string[] parts = value.Split(new[] { ',', '，', '+', ';', '；', '/', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            List<int> rows = new List<int>();
            foreach (string part in parts)
            {
                int number;
                if (!int.TryParse(part, out number) || number < 1 || number > 6)
                    throw new ArgumentException("行组合只能填写 1 至 6 的数字，例如 3,3。");
                rows.Add(number);
            }
            if (rows.Sum() != count)
                throw new ArgumentException("行组合总数必须等于当前已选窗口数（" + count + " 个）。");
            return rows.ToArray();
        }

        private LayoutAlignment SelectedAlignment()
        {
            return alignmentBox.SelectedIndex < 0
                ? LayoutAlignment.Center
                : (LayoutAlignment)alignmentBox.SelectedIndex;
        }

        private int SelectedGap()
        {
            return (int)gapBox.Value;
        }

        private static string AlignmentText(LayoutAlignment alignment)
        {
            switch (alignment)
            {
                case LayoutAlignment.TopLeft: return "左上";
                case LayoutAlignment.TopCenter: return "上中";
                case LayoutAlignment.TopRight: return "右上";
                case LayoutAlignment.MiddleLeft: return "左中";
                case LayoutAlignment.MiddleRight: return "右中";
                case LayoutAlignment.BottomLeft: return "左下";
                case LayoutAlignment.BottomCenter: return "下中";
                case LayoutAlignment.BottomRight: return "右下";
                default: return "居中";
            }
        }

        private LayoutChoice ChoosePlan(List<GameWindow> selected, Rectangle area)
        {
            int[] requestedRows = RequestedRows(selected.Count);
            LayoutAlignment alignment = SelectedAlignment();
            int gap = SelectedGap();
            // The game owns the client-area resolution. Calculate with each live outer
            // rectangle and let ArrangeWindows move the frames without resizing them.
            List<Size> outer = selected.Select(g => g.Bounds.Size).ToList();
            LayoutPlan keep = requestedRows == null
                ? global::MoliWindowTiler.LayoutEngine.Calculate(area, outer, 0, alignment, gap)
                : global::MoliWindowTiler.LayoutEngine.Calculate(area, outer, requestedRows, alignment, gap);
            return new LayoutChoice { Plan = keep, ClientSize = selected[0].ClientSize,
                Fits = keep.ClippedArea == 0 && keep.HiddenRatio < 0.00001 };
        }

        private void UpdateSwitcher()
        {
            if (!controlsReady || switcher == null || switcher.IsDisposed) return;
            if (!switcherBox.Checked || games == null || games.Count == 0)
            {
                switcher.Hide();
                return;
            }
            try
            {
                switcher.SetWindows(games);
                Screen screen = games.Count == 0 ? Screen.PrimaryScreen : Screen.FromHandle(games[0].Handle);
                switcher.ShowOnScreen(screen);
            }
            catch { switcher.Hide(); }
        }

        private void ActivateGame(IntPtr handle)
        {
            try
            {
                if (!Native.IsWindow(handle))
                {
                    RefreshWindows();
                    return;
                }
                Native.ShowWindowAsync(handle, 9); // SW_RESTORE
                Native.BringWindowToTop(handle);
                Native.SetForegroundWindow(handle);
            }
            catch { }
        }

        private void UpdatePlan()
        {
            try { UpdatePlanCore(); }
            catch (Exception ex)
            {
                currentPlan = null;
                if (!IsDisposed && !Disposing)
                {
                    planLabel.Text = "布局预览暂不可用：" + ex.Message;
                    statusLabel.Text = "窗口列表正在刷新，请稍候再试。";
                    arrangeButton.Enabled = false;
                }
            }
        }

        private void UpdatePlanCore()
        {
            if (!controlsReady || IsDisposed || Disposing) return;
            List<GameWindow> selected = SelectedGames();
            if (selected.Count == 0)
            {
                currentPlan = null;
                planLabel.Text = "没有选中的游戏窗口。";
                preview.SetPlan(null, null, Rectangle.Empty, Size.Empty);
                arrangeButton.Enabled = false;
                restoreButton.Enabled = snapshots.Count > 0;
                return;
            }
            try
            {
                Screen screen = TargetScreen(selected);
                int margin = (int)marginBox.Value;
                Rectangle area = screen.WorkingArea;
                area = new Rectangle(area.Left + margin, area.Top + margin,
                    Math.Max(1, area.Width - margin * 2), Math.Max(1, area.Height - margin * 2));
                LayoutChoice choice = ChoosePlan(selected, area);
                currentArea = area;
                currentPlan = choice.Plan;
                currentClientSize = choice.ClientSize;
                preview.SetPlan(choice.Plan, selected, area, choice.ClientSize);
                string result = "已选 " + selected.Count + " 个，" + choice.Plan.Description
                    + "，对齐 " + AlignmentText(choice.Plan.Alignment)
                    + "，间距 " + choice.Plan.Gap + "，当前客户端 "
                    + choice.ClientSize.Width + "×" + choice.ClientSize.Height;
                if (choice.Plan.ClippedArea > 0 || choice.Plan.HiddenRatio > 0.00001)
                    result += "；当前屏幕不足，预计重叠/超出 " + (choice.Plan.HiddenRatio * 100).ToString("0.#") + "%";
                else result += "；无重叠";
                planLabel.Text = result;
                arrangeButton.Enabled = true;
                restoreButton.Enabled = snapshots.Count > 0;
            }
            catch (Exception ex)
            {
                currentPlan = null;
                planLabel.Text = "布局计算失败：" + ex.Message;
                arrangeButton.Enabled = false;
            }
        }

        private void CloseAllClients()
        {
            List<GameWindow> found;
            try
            {
                found = Native.FindGames();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "读取客户端失败：" + ex.Message;
                return;
            }
            if (found.Count == 0)
            {
                RefreshWindows();
                statusLabel.Text = "当前没有可关闭的游戏客户端。";
                return;
            }

            DialogResult result = MessageBox.Show(this,
                "确定要关闭当前发现的 " + found.Count + " 个游戏客户端吗？",
                "关闭所有客户端", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes) return;

            games = found;
            CaptureCurrentPositions();
            int requested = 0;
            foreach (GameWindow game in found)
            {
                if (game != null && Native.IsWindow(game.Handle) &&
                    Native.PostMessage(game.Handle, 0x0010, IntPtr.Zero, IntPtr.Zero))
                    requested++;
            }
            closeAllButton.Enabled = false;
            statusLabel.Text = "已向 " + requested + " 个客户端发送关闭请求。";
            Timer closeTimer = new Timer { Interval = 300 };
            int checks = 0;
            closeTimer.Tick += delegate
            {
                checks++;
                bool anyRemaining = found.Any(game => game != null && Native.IsWindow(game.Handle));
                if (!anyRemaining || checks >= 10)
                {
                    closeTimer.Stop();
                    closeTimer.Dispose();
                    RefreshWindows();
                    statusLabel.Text = anyRemaining
                        ? "已发送关闭请求，仍有客户端正在退出。"
                        : "已关闭全部游戏客户端。";
                }
            };
            closeTimer.Start();
        }

        private void ArrangeWindows()
        {
            List<GameWindow> selected = SelectedGames();
            if (selected.Count == 0) return;
            UpdatePlan();
            if (currentPlan == null) return;
            int moved = 0;
            List<string> errors = new List<string>();
            // Restore first so a minimized/maximized client has a measurable normal frame.
            foreach (GameWindow game in selected)
            {
                if (game.Minimized || game.Maximized) Native.ShowWindowAsync(game.Handle, 9);
            }

            // Move the frames only. The client owns its resolution and outer size.
            for (int i = 0; i < selected.Count && i < currentPlan.Windows.Length; i++)
            {
                GameWindow game = selected[i];
                if (!snapshots.ContainsKey(game.Key))
                    snapshots.Add(game.Key, new WindowSnapshot { Handle = game.Handle, Placement = game.Placement, Bounds = game.Bounds });
                try
                {
                    Rectangle target = currentPlan.Windows[i];
                    const uint SWP_NOZORDER = 0x0004;
                    const uint SWP_NOSIZE = 0x0001;
                    const uint SWP_NOACTIVATE = 0x0010;
                    const uint SWP_SHOWWINDOW = 0x0040;
                    const uint SWP_NOOWNERZORDER = 0x0200;
                    if (!Native.SetWindowPos(game.Handle, IntPtr.Zero, target.Left, target.Top,
                        target.Width, target.Height, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER))
                        errors.Add("PID " + game.Pid);
                    else moved++;
                }
                catch (Exception ex) { errors.Add("PID " + game.Pid + "（" + ex.Message + "）"); }
            }

            // Re-read the live outer sizes so every frame stays inside the work area,
            // while clients are allowed to overlap each other.
            List<Size> actualSizes = selected.Select(g => Native.WindowBounds(g.Handle).Size).ToList();
            if (actualSizes.Any(s => s.Width <= 0 || s.Height <= 0))
                actualSizes = selected.Select(g => g.Bounds.Size).ToList();
            // Keep the row grouping shown in the preview so a visible 3+3 layout
            // remains 3+3 after the client reports its live outer size.
            LayoutPlan finalPlan = global::MoliWindowTiler.LayoutEngine.Calculate(
                currentArea, actualSizes, currentPlan.RowCounts,
                currentPlan.Alignment, currentPlan.Gap);
            for (int i = 0; i < selected.Count && i < finalPlan.Windows.Length; i++)
            {
                Rectangle target = finalPlan.Windows[i];
                if (!Native.SetWindowPos(selected[i].Handle, IntPtr.Zero, target.Left, target.Top,
                    target.Width, target.Height, 0x0001 | 0x0004 | 0x0010 | 0x0040 | 0x0200))
                    errors.Add("PID " + selected[i].Pid);
            }
            string arrangeStatus = "已排列 " + moved + " 个客户端" + (errors.Count == 0 ? "。" : "；失败：" + string.Join("、", errors));
            CaptureCurrentPositions();
            RefreshWindows();
            statusLabel.Text = arrangeStatus;
        }

        private void RestoreWindows()
        {
            int restored = 0;
            List<string> restoredKeys = new List<string>();
            foreach (KeyValuePair<string, WindowSnapshot> pair in snapshots.ToList())
            {
                WindowSnapshot snapshot = pair.Value;
                if (snapshot == null || !Native.IsWindow(snapshot.Handle)) continue;
                try
                {
                    WindowPlacement placement = snapshot.Placement;
                    if (Native.SetWindowPlacement(snapshot.Handle, ref placement)) { restored++; restoredKeys.Add(pair.Key); }
                    else if (Native.SetWindowPos(snapshot.Handle, IntPtr.Zero, snapshot.Bounds.Left, snapshot.Bounds.Top,
                        snapshot.Bounds.Width, snapshot.Bounds.Height, 0x0010 | 0x0200)) { restored++; restoredKeys.Add(pair.Key); }
                }
                catch { }
            }
            string restoreStatus = restored == 0 ? "没有可恢复的窗口。" : "已恢复 " + restored + " 个客户端的原位置。";
            foreach (string key in restoredKeys) snapshots.Remove(key);
            RefreshWindows();
            statusLabel.Text = restoreStatus;
        }
    }
}
