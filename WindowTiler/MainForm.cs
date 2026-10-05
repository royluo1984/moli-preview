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
        private readonly ComboBox targetProfileBox = new ComboBox();
        private readonly TextBox customRowsBox = new TextBox();
        private readonly CheckBox switcherBox = new CheckBox();
        private readonly CheckBox trayBox = new CheckBox();
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
        private readonly Button hotkeyButton = new Button();
        private readonly Button minimizeHotkeyButton = new Button();
        private readonly Button manageProfilesButton = new Button();
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
        private readonly List<string> preferredOrder = new List<string>();
        private readonly List<ClientHotkeyBinding> clientHotkeys = new List<ClientHotkeyBinding>();
        private readonly List<TargetProfile> targetProfiles = new List<TargetProfile>();
        private string activeTargetProfileId = TargetProfile.MoliDefaultId;
        private readonly Dictionary<int, ClientHotkeyBinding> registeredHotkeys =
            new Dictionary<int, ClientHotkeyBinding>();
        private readonly List<string> hotkeyFailures = new List<string>();
        private const int FirstHotkeyId = 0x5200;
        private const int MinimizeAllHotkeyId = 0x51FF;
        private uint minimizeAllModifiers = HotkeyDefaults.MinimizeAllModifiers;
        private uint minimizeAllKey = HotkeyDefaults.MinimizeAllKey;
        private bool minimizeAllHotkeyRegistered;
        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private ContextMenuStrip trayMenu;
        private bool exiting;
        private bool minimizingToTray;

        public MainForm()
        {
            positionStore = new PositionStore();
            settingsStore = new SettingsStore();
            switcher = new SwitcherOverlay(ActivateGame);
            switcher.UserClosed += delegate { if (!IsDisposed && !Disposing) switcherBox.Checked = false; };
            switcher.OrderChanged += HandleSwitcherOrderChanged;
            Text = "魔力宝贝窗口排列器";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(940, 620);
            ClientSize = new Size(1180, 760);
            Icon = SystemIcons.Application;
            BuildControls();
            InitializeTray();
            ApplySettings(settingsStore.Load());
            controlsReady = true;
            Resize += MainFormResize;
            FormClosing += delegate
            {
                exiting = true;
                CaptureCurrentPositions();
                SaveSettings();
                UnregisterClientHotkeys();
                if (switcher != null && !switcher.IsDisposed) switcher.Close();
                trayIcon.Visible = false;
                trayIcon.Dispose();
                if (trayMenu != null) trayMenu.Dispose();
            };
            RefreshMonitors();
            RefreshWindows();
            if (IsHandleCreated) RegisterClientHotkeys();
        }

        private void BuildControls()
        {
            BackColor = Color.FromArgb(238, 242, 247);
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10),
                BackColor = BackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 156));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(root);

            GroupBox options = new GroupBox
            {
                Text = "排列设置",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 22, 10, 8),
                BackColor = Color.White
            };
            root.Controls.Add(options, 0, 0);

            TableLayoutPanel settings = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 3,
                BackColor = Color.White,
                Padding = new Padding(0)
            };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            options.Controls.Add(settings);

            ConfigureCombo(monitorBox, 190);
            monitorBox.SelectedIndexChanged += delegate { SaveSettings(); UpdatePlan(); };
            AddSettingLabel(settings, "目标屏幕", 0, 0);
            settings.Controls.Add(monitorBox, 1, 0);

            ConfigureCombo(columnsBox, 132);
            columnsBox.Items.AddRange(new object[] { "智能排列", "一行横排", "一列竖排", "两行均匀", "三行均匀", "自定义组合" });
            columnsBox.SelectedIndex = 0;
            columnsBox.SelectedIndexChanged += delegate
            {
                customRowsBox.Enabled = columnsBox.SelectedIndex == 5;
                SaveSettings();
                UpdatePlan();
            };
            AddSettingLabel(settings, "排列方式", 2, 0);
            settings.Controls.Add(columnsBox, 3, 0);

            customRowsBox.Text = "3,3";
            customRowsBox.Dock = DockStyle.Fill;
            customRowsBox.Enabled = false;
            customRowsBox.Margin = new Padding(3, 5, 3, 5);
            customRowsBox.TextChanged += delegate
            {
                SaveSettings();
                if (columnsBox.SelectedIndex == 5) UpdatePlan();
            };
            AddSettingLabel(settings, "行组合", 4, 0);
            settings.Controls.Add(customRowsBox, 5, 0);
            Label rowsHint = new Label { Text = "例：3,3 / 2,2,2", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
            settings.Controls.Add(rowsHint, 6, 0);
            settings.SetColumnSpan(rowsHint, 2);

            ConfigureCombo(alignmentBox, 96);
            alignmentBox.Items.AddRange(new object[] { "左上", "上中", "右上", "左中", "居中", "右中", "左下", "下中", "右下" });
            alignmentBox.SelectedIndex = (int)LayoutAlignment.Center;
            alignmentBox.SelectedIndexChanged += delegate { SaveSettings(); UpdatePlan(); };
            AddSettingLabel(settings, "对齐方式", 0, 1);
            settings.Controls.Add(alignmentBox, 1, 1);

            ConfigureNumber(gapBox, 300, 0, 54);
            gapBox.ValueChanged += delegate { SaveSettings(); UpdatePlan(); };
            AddSettingLabel(settings, "窗口间距", 2, 1);
            settings.Controls.Add(gapBox, 3, 1);

            ConfigureNumber(marginBox, 80, 8, 54);
            marginBox.ValueChanged += delegate { SaveSettings(); UpdatePlan(); };
            AddSettingLabel(settings, "屏幕边距", 4, 1);
            settings.Controls.Add(marginBox, 5, 1);
            Label preserveLabel = new Label { Text = "只移动位置，不改分辨率", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(48, 110, 72) };
            settings.Controls.Add(preserveLabel, 6, 1);
            settings.SetColumnSpan(preserveLabel, 2);

            FlowLayoutPanel features = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 2, 0, 0)
            };
            settings.Controls.Add(features, 0, 2);
            settings.SetColumnSpan(features, 8);
            Label targetProfileLabel = new Label
            {
                Text = "目标程序",
                AutoSize = true,
                Margin = new Padding(2, 7, 3, 0),
                ForeColor = Color.FromArgb(70, 78, 90)
            };
            features.Controls.Add(targetProfileLabel);
            targetProfileBox.DropDownStyle = ComboBoxStyle.DropDownList;
            targetProfileBox.Width = 150;
            targetProfileBox.Height = 27;
            targetProfileBox.Margin = new Padding(2, 2, 4, 0);
            targetProfileBox.SelectedIndexChanged += delegate { TargetProfileSelectionChanged(); };
            features.Controls.Add(targetProfileBox);
            manageProfilesButton.Text = "管理目标程序";
            manageProfilesButton.Width = 100;
            manageProfilesButton.Height = 27;
            manageProfilesButton.Margin = new Padding(2, 2, 12, 0);
            manageProfilesButton.Click += delegate { ManageTargetProfiles(); };
            features.Controls.Add(manageProfilesButton);
            switcherBox.Text = "显示点击切换浮层";
            switcherBox.Checked = true;
            switcherBox.AutoSize = true;
            switcherBox.Margin = new Padding(2, 5, 18, 0);
            switcherBox.CheckedChanged += delegate { SaveSettings(); UpdateSwitcher(); };
            features.Controls.Add(switcherBox);
            trayBox.Text = "最小化到托盘";
            trayBox.Checked = true;
            trayBox.AutoSize = true;
            trayBox.Margin = new Padding(2, 5, 18, 0);
            trayBox.CheckedChanged += delegate { SaveSettings(); };
            features.Controls.Add(trayBox);
            hotkeyButton.Text = "设置客户端快捷键";
            hotkeyButton.Width = 138;
            hotkeyButton.Height = 27;
            hotkeyButton.Margin = new Padding(2, 2, 3, 0);
            hotkeyButton.Click += delegate { ConfigureClientHotkeys(); };
            features.Controls.Add(hotkeyButton);
            minimizeHotkeyButton.Width = 190;
            minimizeHotkeyButton.Height = 27;
            minimizeHotkeyButton.Margin = new Padding(2, 2, 3, 0);
            minimizeHotkeyButton.Click += delegate { ConfigureMinimizeAllHotkey(); };
            features.Controls.Add(minimizeHotkeyButton);
            UpdateMinimizeHotkeyButtonText();

            TableLayoutPanel body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = BackColor,
                Padding = new Padding(0, 8, 0, 8)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            root.Controls.Add(body, 0, 1);

            GroupBox windowsGroup = new GroupBox
            {
                Text = "客户端列表（勾选要排列的窗口）",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 22, 8, 8),
                BackColor = Color.White,
                Margin = new Padding(0, 0, 6, 0)
            };
            body.Controls.Add(windowsGroup, 0, 0);
            windowList.Dock = DockStyle.Fill;
            windowList.CheckBoxes = true;
            windowList.FullRowSelect = true;
            windowList.GridLines = false;
            windowList.MultiSelect = false;
            windowList.View = View.Details;
            windowList.HideSelection = false;
            windowList.BackColor = Color.White;
            windowList.Columns.Add("PID", 52);
            windowList.Columns.Add("人物", 108);
            windowList.Columns.Add("快捷键", 86);
            windowList.Columns.Add("线程", 56);
            windowList.Columns.Add("客户端标题", 142);
            windowList.Columns.Add("分辨率", 78);
            windowList.Columns.Add("状态", 58);
            windowList.Columns.Add("位置", 104);
            windowList.ItemChecked += delegate
            {
                if (refreshingWindows) return;
                UpdatePlan();
            };
            windowsGroup.Controls.Add(windowList);

            GroupBox previewGroup = new GroupBox
            {
                Text = "排列预览",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 22, 8, 8),
                BackColor = Color.White,
                Margin = new Padding(6, 0, 0, 0)
            };
            body.Controls.Add(previewGroup, 1, 0);
            planLabel.AutoSize = false;
            planLabel.Dock = DockStyle.Bottom;
            planLabel.Height = 44;
            planLabel.TextAlign = ContentAlignment.MiddleLeft;
            planLabel.Padding = new Padding(7, 2, 7, 2);
            planLabel.ForeColor = Color.FromArgb(70, 78, 90);
            planLabel.BackColor = Color.FromArgb(247, 249, 252);
            previewGroup.Controls.Add(planLabel);
            preview.Dock = DockStyle.Fill;
            previewGroup.Controls.Add(preview);

            TableLayoutPanel actionBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.White,
                Padding = new Padding(8, 5, 8, 5)
            };
            actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
            actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
            root.Controls.Add(actionBar, 0, 2);
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0)
            };
            actionBar.Controls.Add(actions, 0, 0);
            ConfigureButton(refreshButton, "刷新窗口", 84, false);
            refreshButton.Click += delegate { RefreshWindows(); };
            actions.Controls.Add(refreshButton);
            ConfigureButton(selectAllButton, "全选", 58, false);
            selectAllButton.Click += delegate { SetAllChecked(true); };
            actions.Controls.Add(selectAllButton);
            ConfigureButton(clearButton, "清空选择", 78, false);
            clearButton.Click += delegate { SetAllChecked(false); };
            actions.Controls.Add(clearButton);
            ConfigureButton(arrangeButton, "一键排列", 100, true);
            arrangeButton.Click += delegate { ArrangeWindows(); };
            actions.Controls.Add(arrangeButton);
            ConfigureButton(restoreButton, "恢复原位置", 92, false);
            restoreButton.Click += delegate { RestoreWindows(); };
            actions.Controls.Add(restoreButton);
            ConfigureButton(closeAllButton, "关闭所有客户端", 122, false);
            closeAllButton.ForeColor = Color.DarkRed;
            closeAllButton.Enabled = false;
            closeAllButton.Click += delegate { CloseAllClients(); };
            actions.Controls.Add(closeAllButton);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.AutoEllipsis = true;
            statusLabel.TextAlign = ContentAlignment.MiddleRight;
            statusLabel.Padding = new Padding(8, 0, 4, 0);
            statusLabel.ForeColor = Color.FromArgb(74, 85, 104);
            actionBar.Controls.Add(statusLabel, 1, 0);
        }

        private static void AddSettingLabel(TableLayoutPanel table, string text, int column, int row)
        {
            table.Controls.Add(new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(70, 78, 90),
                Padding = new Padding(2, 0, 2, 0)
            }, column, row);
        }

        private static void ConfigureCombo(ComboBox box, int width)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Width = width;
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(3, 5, 3, 5);
        }

        private static void ConfigureNumber(NumericUpDown box, int maximum, int value, int width)
        {
            box.Minimum = 0;
            box.Maximum = maximum;
            box.Value = value;
            box.Width = width;
            box.Dock = DockStyle.Left;
            box.Margin = new Padding(3, 5, 3, 5);
        }

        private static void ConfigureButton(Button button, string text, int width, bool primary)
        {
            button.Text = text;
            button.Width = width;
            button.Height = 30;
            button.Margin = new Padding(3, 0, 3, 0);
            button.FlatStyle = FlatStyle.Standard;
            if (primary)
            {
                button.Font = new Font(button.Font, FontStyle.Bold);
                button.BackColor = Color.FromArgb(44, 115, 200);
                button.ForeColor = Color.White;
            }
        }

        private void InitializeTray()
        {
            trayMenu = new ContextMenuStrip();

            ToolStripMenuItem showItem = new ToolStripMenuItem("显示主窗口");
            showItem.Click += delegate { RestoreFromTray(); };
            trayMenu.Items.Add(showItem);

            ToolStripMenuItem refreshItem = new ToolStripMenuItem("刷新客户端列表");
            refreshItem.Click += delegate
            {
                RestoreFromTray();
                RefreshWindows();
            };
            trayMenu.Items.Add(refreshItem);

            ToolStripMenuItem arrangeItem = new ToolStripMenuItem("一键排列");
            arrangeItem.Click += delegate
            {
                RestoreFromTray();
                ArrangeWindows();
            };
            trayMenu.Items.Add(arrangeItem);
            trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate
            {
                exiting = true;
                Close();
            };
            trayMenu.Items.Add(exitItem);

            trayIcon.Icon = Icon ?? SystemIcons.Application;
            trayIcon.Text = "魔力宝贝窗口排列器";
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = false;
            trayIcon.DoubleClick += delegate { RestoreFromTray(); };
        }

        private void MainFormResize(object sender, EventArgs e)
        {
            if (exiting || minimizingToTray || !trayBox.Checked || WindowState != FormWindowState.Minimized)
                return;

            minimizingToTray = true;
            try
            {
                trayIcon.Visible = true;
                ShowInTaskbar = false;
                Hide();
            }
            finally
            {
                minimizingToTray = false;
            }
        }

        private void RestoreFromTray()
        {
            if (exiting || IsDisposed || Disposing) return;
            minimizingToTray = false;
            ShowInTaskbar = true;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            BringToFront();
            trayIcon.Visible = false;
        }

        private TargetProfile CurrentTargetProfile()
        {
            TargetProfile selected = targetProfileBox.SelectedItem as TargetProfile;
            if (selected != null) return selected;
            selected = targetProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, activeTargetProfileId, StringComparison.OrdinalIgnoreCase));
            return selected ?? TargetProfile.CreateMoliDefault();
        }

        private void LoadTargetProfiles(IList<TargetProfile> source, string activeId)
        {
            AppSettings migration = new AppSettings
            {
                TargetProfiles = source == null
                    ? null
                    : source.Where(profile => profile != null).Select(profile => profile.Clone()).ToList(),
                ActiveTargetProfileId = activeId
            };
            SettingsStore.EnsureTargetProfiles(migration);
            targetProfiles.Clear();
            targetProfiles.AddRange(migration.TargetProfiles.Select(profile => profile.Clone()));
            activeTargetProfileId = migration.ActiveTargetProfileId ?? TargetProfile.MoliDefaultId;
            BindTargetProfileBox();
        }

        private void BindTargetProfileBox()
        {
            targetProfileBox.BeginUpdate();
            try
            {
                targetProfileBox.Items.Clear();
                foreach (TargetProfile profile in targetProfiles)
                    if (profile != null && profile.Enabled) targetProfileBox.Items.Add(profile);
                int selected = -1;
                for (int i = 0; i < targetProfileBox.Items.Count; i++)
                {
                    TargetProfile profile = targetProfileBox.Items[i] as TargetProfile;
                    if (profile != null && string.Equals(profile.Id, activeTargetProfileId, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = i;
                        break;
                    }
                }
                if (selected < 0 && targetProfileBox.Items.Count > 0) selected = 0;
                targetProfileBox.SelectedIndex = selected;
                TargetProfile current = targetProfileBox.SelectedItem as TargetProfile;
                if (current != null) activeTargetProfileId = current.Id;
            }
            finally { targetProfileBox.EndUpdate(); }
        }

        private void TargetProfileSelectionChanged()
        {
            if (!controlsReady || applyingSettings) return;
            TargetProfile selected = targetProfileBox.SelectedItem as TargetProfile;
            if (selected == null || string.Equals(selected.Id, activeTargetProfileId, StringComparison.OrdinalIgnoreCase)) return;
            CaptureCurrentPositions();
            activeTargetProfileId = selected.Id;
            SaveSettings();
            RegisterClientHotkeys();
            RefreshWindows();
        }

        private void ManageTargetProfiles()
        {
            using (TargetProfileManagerDialog dialog = new TargetProfileManagerDialog(targetProfiles))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string previous = activeTargetProfileId;
                targetProfiles.Clear();
                foreach (TargetProfile profile in dialog.Profiles)
                    if (profile != null) targetProfiles.Add(profile.Clone());
                SettingsStore.EnsureTargetProfiles(new AppSettings { TargetProfiles = targetProfiles, ActiveTargetProfileId = previous });
                activeTargetProfileId = targetProfiles.Any(profile =>
                    string.Equals(profile.Id, previous, StringComparison.OrdinalIgnoreCase))
                    ? previous : TargetProfile.MoliDefaultId;
                BindTargetProfileBox();
                SaveSettings();
                RegisterClientHotkeys();
                RefreshWindows();
            }
        }

        private void ApplySettings(AppSettings settings)
        {
            if (settings == null) return;
            applyingSettings = true;
            try
            {
                SettingsStore.EnsureTargetProfiles(settings);
                LoadTargetProfiles(settings.TargetProfiles, settings.ActiveTargetProfileId);
                columnsBox.SelectedIndex = settings.LayoutMode >= 0 && settings.LayoutMode < columnsBox.Items.Count
                    ? settings.LayoutMode : 0;
                customRowsBox.Text = string.IsNullOrWhiteSpace(settings.CustomRows) ? "3,3" : settings.CustomRows;
                alignmentBox.SelectedIndex = settings.Alignment >= 0 && settings.Alignment < alignmentBox.Items.Count
                    ? settings.Alignment : (int)LayoutAlignment.Center;
                gapBox.Value = Math.Max(gapBox.Minimum, Math.Min(gapBox.Maximum, settings.Gap));
                marginBox.Value = Math.Max(marginBox.Minimum, Math.Min(marginBox.Maximum, settings.Margin));
                switcherBox.Checked = settings.ShowSwitcher;
                trayBox.Checked = settings.MinimizeToTray;
                ApplyMinimizeAllHotkey(settings.MinimizeAllModifiers, settings.MinimizeAllKey);
                preferredMonitor = settings.Monitor ?? "";
                hasSavedSelection = settings.HasSelection;
                preferredCharacters.Clear();
                if (settings.SelectedCharacters != null)
                {
                    foreach (string character in settings.SelectedCharacters)
                        if (!string.IsNullOrWhiteSpace(character)) preferredCharacters.Add(character.Trim());
                }
                preferredOrder.Clear();
                if (settings.ClientOrder != null)
                {
                    foreach (string identity in settings.ClientOrder)
                        if (!string.IsNullOrWhiteSpace(identity) &&
                            !preferredOrder.Any(existing => string.Equals(existing, identity.Trim(), StringComparison.OrdinalIgnoreCase)))
                            preferredOrder.Add(identity.Trim());
                }
                clientHotkeys.Clear();
                if (settings.ClientHotkeys != null)
                {
                    foreach (ClientHotkeyBinding binding in settings.ClientHotkeys)
                    {
                        if (binding == null || !binding.IsValid) continue;
                        if (!clientHotkeys.Any(existing =>
                            string.Equals(existing.Identity, binding.Identity.Trim(), StringComparison.OrdinalIgnoreCase)))
                        {
                            ClientHotkeyBinding copy = binding.Clone();
                            copy.Identity = copy.Identity.Trim();
                            clientHotkeys.Add(copy);
                        }
                    }
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
                            preferredCharacters.Add(PositionStore.IdentityFor(game));
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
                    MinimizeToTray = trayBox.Checked,
                    MinimizeAllModifiers = minimizeAllModifiers,
                    MinimizeAllKey = minimizeAllKey,
                    HasSelection = hasSavedSelection,
                    SelectedCharacters = preferredCharacters.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
                    ClientOrder = preferredOrder.ToList(),
                    ClientHotkeys = clientHotkeys.Select(binding => binding.Clone()).ToList(),
                    TargetProfiles = targetProfiles.Select(profile => profile.Clone()).ToList(),
                    ActiveTargetProfileId = activeTargetProfileId
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
                List<GameWindow> found = Native.FindGames(CurrentTargetProfile());
                ApplySavedPositions(found);
                games = ApplyPreferredOrder(found);
                closeAllButton.Enabled = games.Count > 0 && CurrentTargetProfile().AllowClose;
                windowList.BeginUpdate();
                windowList.Items.Clear();
                foreach (GameWindow game in games)
                {
                    ListViewItem item = new ListViewItem(game.Pid.ToString());
                    item.SubItems.Add(game.CharacterName);
                    item.SubItems.Add(HotkeyTextFor(game));
                    item.SubItems.Add(game.ThreadId.ToString());
                    item.SubItems.Add(string.IsNullOrWhiteSpace(game.Title) ? "（无标题）" : game.Title.Trim());
                    item.SubItems.Add(game.Resolution);
                    item.SubItems.Add(game.Minimized ? "最小化" : game.Maximized ? "最大化" : "正常");
                    item.SubItems.Add(game.Bounds.Left + "," + game.Bounds.Top + "  " + game.Bounds.Width + "×" + game.Bounds.Height);
                    item.Tag = game;
                    if (checkedState.ContainsKey(game.Key))
                        item.Checked = checkedState[game.Key];
                    else if (hasSavedSelection)
                        item.Checked = preferredCharacters.Any(identity => PositionStore.MatchesIdentity(identity, game));
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

        private List<GameWindow> ApplyPreferredOrder(List<GameWindow> found)
        {
            if (found == null || found.Count <= 1 || preferredOrder.Count == 0)
                return found ?? new List<GameWindow>();
            List<GameWindow> remaining = new List<GameWindow>(found);
            List<GameWindow> ordered = new List<GameWindow>();
            foreach (string identity in preferredOrder)
            {
                GameWindow match = remaining.FirstOrDefault(game => PositionStore.MatchesIdentity(identity, game));
                if (match == null) continue;
                ordered.Add(match);
                remaining.Remove(match);
            }
            ordered.AddRange(remaining);
            return ordered;
        }

        private void RememberClientOrder(IList<GameWindow> ordered)
        {
            if (ordered == null) return;
            List<string> next = new List<string>();
            foreach (GameWindow game in ordered)
            {
                string identity = PositionStore.IdentityFor(game);
                if (!string.IsNullOrWhiteSpace(identity) &&
                    !next.Any(existing => string.Equals(existing, identity, StringComparison.OrdinalIgnoreCase)))
                    next.Add(identity);
            }
            foreach (string identity in preferredOrder)
            {
                if (!string.IsNullOrWhiteSpace(identity) &&
                    !next.Any(existing => string.Equals(existing, identity, StringComparison.OrdinalIgnoreCase)))
                    next.Add(identity);
            }
            preferredOrder.Clear();
            preferredOrder.AddRange(next);
        }

        private void HandleSwitcherOrderChanged(IList<IntPtr> handles)
        {
            if (!controlsReady || handles == null || handles.Count < 2 || games == null) return;
            Dictionary<IntPtr, GameWindow> byHandle = new Dictionary<IntPtr, GameWindow>();
            foreach (GameWindow game in games)
            {
                if (game != null && !byHandle.ContainsKey(game.Handle)) byHandle.Add(game.Handle, game);
            }
            List<GameWindow> ordered = new List<GameWindow>();
            HashSet<IntPtr> added = new HashSet<IntPtr>();
            foreach (IntPtr handle in handles)
            {
                GameWindow game;
                if (byHandle.TryGetValue(handle, out game) && added.Add(handle)) ordered.Add(game);
            }
            foreach (GameWindow game in games)
                if (game != null && added.Add(game.Handle)) ordered.Add(game);
            if (ordered.Count < 2) return;
            games = ordered;
            RememberClientOrder(games);
            SaveSettings();
            statusLabel.Text = "已交换客户端顺序，正在保存并重新排列窗口。";
            ArrangeWindows();
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
                SavedWindowPosition saved = positionStore.Get(game);
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
            List<GameWindow> checkedGames = new List<GameWindow>();
            foreach (ListViewItem item in windowList.Items)
            {
                if (item == null || !item.Checked) continue;
                GameWindow game = item.Tag as GameWindow;
                if (game != null && Native.IsWindow(game.Handle)) checkedGames.Add(game);
            }
            if (checkedGames.Count <= 1 || games == null || games.Count == 0) return checkedGames;
            HashSet<string> selected = new HashSet<string>(checkedGames.Select(game => game.Key));
            List<GameWindow> result = games.Where(game => game != null && selected.Contains(game.Key)).ToList();
            HashSet<string> added = new HashSet<string>(result.Select(game => game.Key));
            result.AddRange(checkedGames.Where(game => !added.Contains(game.Key)));
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
                switcher.SetWindows(games, HotkeyTextFor);
                Screen screen = games.Count == 0 ? Screen.PrimaryScreen : Screen.FromHandle(games[0].Handle);
                switcher.ShowOnScreen(screen);
            }
            catch { switcher.Hide(); }
        }

        private void ApplyMinimizeAllHotkey(uint modifiers, uint key)
        {
            if (key == 0)
            {
                minimizeAllModifiers = 0;
                minimizeAllKey = 0;
            }
            else if (HotkeyFormatter.IsValid(modifiers, key))
            {
                minimizeAllModifiers = modifiers;
                minimizeAllKey = key;
            }
            else
            {
                minimizeAllModifiers = HotkeyDefaults.MinimizeAllModifiers;
                minimizeAllKey = HotkeyDefaults.MinimizeAllKey;
            }
            UpdateMinimizeHotkeyButtonText();
        }

        private void UpdateMinimizeHotkeyButtonText()
        {
            if (minimizeHotkeyButton == null || minimizeHotkeyButton.IsDisposed) return;
            minimizeHotkeyButton.Text = "全部最小化：" +
                HotkeyFormatter.Format(minimizeAllModifiers, minimizeAllKey);
        }

        private void ConfigureMinimizeAllHotkey()
        {
            using (GlobalHotkeySettingsDialog dialog =
                new GlobalHotkeySettingsDialog(minimizeAllModifiers, minimizeAllKey))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ApplyMinimizeAllHotkey(dialog.Modifiers, dialog.Key);
                int failed = RegisterClientHotkeys();
                SaveSettings();
                statusLabel.Text = failed == 0
                    ? "全部最小化快捷键已保存：" + HotkeyFormatter.Format(minimizeAllModifiers, minimizeAllKey)
                    : "快捷键已保存，但有 " + failed + " 个组合键注册失败。";
                if (failed > 0)
                {
                    MessageBox.Show(this,
                        "以下快捷键已被其他程序占用或被系统保留，请换用其他组合键：\n\n" +
                        string.Join("\n", hotkeyFailures),
                        "快捷键设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void MinimizeAllGames()
        {
            try
            {
                CaptureCurrentPositions();
                List<GameWindow> found = Native.FindGames(CurrentTargetProfile());
                HashSet<IntPtr> handles = new HashSet<IntPtr>();
                foreach (GameWindow game in found)
                {
                    if (game == null || !handles.Add(game.Handle) || !Native.IsWindow(game.Handle)) continue;
                    Native.ShowWindowAsync(game.Handle, 6); // SW_MINIMIZE
                }
                if (switcher != null && !switcher.IsDisposed) switcher.Hide();
                statusLabel.Text = found.Count == 0
                    ? "当前没有发现游戏客户端。"
                    : "已最小化 " + handles.Count + " 个游戏客户端。";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "最小化游戏窗口失败：" + ex.Message;
            }
        }

        private void ConfigureClientHotkeys()
        {
            List<GameWindow> liveGames;
            try { liveGames = ApplyPreferredOrder(Native.FindGames(CurrentTargetProfile())); }
            catch (Exception ex)
            {
                statusLabel.Text = "读取客户端失败：" + ex.Message;
                return;
            }
            if (liveGames.Count == 0 && clientHotkeys.Count == 0)
            {
                MessageBox.Show(this, "请先刷新并发现客户端，再设置快捷键。",
                    "客户端快捷键", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (HotkeySettingsDialog dialog = new HotkeySettingsDialog(liveGames, clientHotkeys))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                clientHotkeys.Clear();
                foreach (ClientHotkeyBinding binding in dialog.Bindings)
                    if (binding != null) clientHotkeys.Add(binding.Clone());
                int failed = RegisterClientHotkeys();
                SaveSettings();
                RefreshWindows();
                statusLabel.Text = failed == 0
                    ? "客户端快捷键已保存。"
                    : "快捷键已保存，但有 " + failed + " 个组合键注册失败，可能已被其他程序占用。";
                if (failed > 0)
                {
                    MessageBox.Show(this,
                        "以下快捷键已被其他程序占用或被系统保留，请换用其他组合键：\n\n" +
                        string.Join("\n", hotkeyFailures),
                        "客户端快捷键", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private int RegisterClientHotkeys()
        {
            UnregisterClientHotkeys();
            hotkeyFailures.Clear();
            if (!controlsReady || !IsHandleCreated || IsDisposed || Disposing) return 0;
            int failed = 0;
            if (HotkeyFormatter.IsValid(minimizeAllModifiers, minimizeAllKey))
            {
                const uint MOD_NOREPEAT = 0x4000;
                if (Native.RegisterHotKey(Handle, MinimizeAllHotkeyId,
                    minimizeAllModifiers | MOD_NOREPEAT, minimizeAllKey))
                    minimizeAllHotkeyRegistered = true;
                else
                {
                    failed++;
                    hotkeyFailures.Add("全部最小化：" +
                        HotkeyFormatter.Format(minimizeAllModifiers, minimizeAllKey));
                }
            }
            int id = FirstHotkeyId;
            foreach (ClientHotkeyBinding binding in clientHotkeys)
            {
                if (binding == null || !binding.IsValid) continue;
                const uint MOD_NOREPEAT = 0x4000;
                if (Native.RegisterHotKey(Handle, id, binding.Modifiers | MOD_NOREPEAT, binding.Key))
                    registeredHotkeys.Add(id, binding.Clone());
                else
                {
                    failed++;
                    hotkeyFailures.Add(binding.Identity + "：" + binding.ShortcutText);
                }
                id++;
            }
            return failed;
        }

        private void UnregisterClientHotkeys()
        {
            if (registeredHotkeys == null) return;
            if (IsHandleCreated)
            {
                if (minimizeAllHotkeyRegistered)
                {
                    try { Native.UnregisterHotKey(Handle, MinimizeAllHotkeyId); }
                    catch { }
                    minimizeAllHotkeyRegistered = false;
                }
                foreach (int id in registeredHotkeys.Keys.ToList())
                {
                    try { Native.UnregisterHotKey(Handle, id); }
                    catch { }
                }
            }
            registeredHotkeys.Clear();
        }

        private void ActivateClientByIdentity(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) return;
            try
            {
                // Read live identities: login names and window handles may have changed.
                // Discovery only keeps a hotkey press from rebuilding the layout.
                List<GameWindow> matches = Native.FindGames(CurrentTargetProfile()).Where(candidate =>
                    PositionStore.MatchesIdentity(identity, candidate)).ToList();
                if (matches.Count == 1)
                {
                    ActivateGame(matches[0].Handle);
                    return;
                }
                statusLabel.Text = matches.Count == 0
                    ? "快捷键对应的角色尚未登录：" + identity
                    : "多个客户端使用相同角色名，请通过悬浮窗选择：" + identity;
            }
            catch (Exception ex)
            {
                statusLabel.Text = "快捷键切换失败：" + ex.Message;
            }
        }

        private string HotkeyTextFor(GameWindow game)
        {
            ClientHotkeyBinding binding = clientHotkeys.FirstOrDefault(candidate =>
                PositionStore.MatchesIdentity(candidate.Identity, game));
            return binding == null ? "" : binding.ShortcutText;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (controlsReady) RegisterClientHotkeys();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (hotkeyFailures.Count > 0)
                statusLabel.Text = "快捷键注册失败，请在设置中更换组合键：" + string.Join("；", hotkeyFailures);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterClientHotkeys();
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message message)
        {
            const int WM_HOTKEY = 0x0312;
            if (message.Msg == WM_HOTKEY && controlsReady && !IsDisposed && !Disposing)
            {
                int id = message.WParam.ToInt32();
                if (id == MinimizeAllHotkeyId && minimizeAllHotkeyRegistered)
                {
                    MinimizeAllGames();
                    return;
                }
                ClientHotkeyBinding binding;
                if (registeredHotkeys.TryGetValue(id, out binding))
                {
                    ActivateClientByIdentity(binding.Identity);
                    return;
                }
            }
            base.WndProc(ref message);
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
                if (Native.IsIconic(handle)) Native.ShowWindowAsync(handle, 9); // SW_RESTORE
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
            if (!CurrentTargetProfile().AllowClose)
            {
                statusLabel.Text = "当前目标程序配置已禁止关闭客户端。";
                return;
            }
            List<GameWindow> found;
            try
            {
                found = Native.FindGames(CurrentTargetProfile());
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
