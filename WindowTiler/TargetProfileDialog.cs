using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MoliWindowTiler
{
    internal sealed class ProfileIdentityChoice
    {
        internal readonly string Value;
        internal readonly string Text;

        internal ProfileIdentityChoice(string value, string text)
        {
            Value = value;
            Text = text;
        }

        public override string ToString() { return Text; }
    }

    internal sealed class TargetProfileEditorDialog : AdaptiveForm
    {
        private readonly TargetProfile profile;
        private readonly IList<WindowSample> samples;
        private readonly ComboBox sampleBox = new ComboBox();
        private readonly TextBox nameBox = new TextBox();
        private readonly TextBox executableBox = new TextBox();
        private readonly TextBox classBox = new TextBox();
        private readonly TextBox titleBox = new TextBox();
        private readonly ComboBox identityBox = new ComboBox();
        private readonly TextBox patternBox = new TextBox();
        private readonly CheckBox allowCloseBox = new CheckBox();
        private readonly CheckBox enabledBox = new CheckBox();
        private readonly Label patternHint = new Label();
        private readonly TextBox testResultBox = new TextBox();

        internal TargetProfile Profile { get { return profile; } }

        internal TargetProfileEditorDialog(TargetProfile source, IList<WindowSample> samples)
        {
            profile = source == null ? new TargetProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "新目标程序",
                IdentitySource = TargetProfile.IdentityTitleSuffix,
                AllowClose = true,
                Enabled = true
            } : source.Clone();
            this.samples = samples ?? new List<WindowSample>();
            Text = source == null ? "新增目标程序" : "编辑目标程序";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            SetInitialSize(new Size(680, 560));
            BuildControls();
            LoadProfile();
        }

        private void BuildControls()
        {
            TableLayoutPanel frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(frame);
            Panel viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            frame.Controls.Add(viewport, 0, 0);
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 10,
                Padding = new Padding(4)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 9; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            viewport.Controls.Add(root);

            AddLabel(root, "窗口样本", 0);
            sampleBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sampleBox.Dock = DockStyle.Fill;
            sampleBox.Margin = new Padding(3, 4, 3, 4);
            foreach (WindowSample sample in samples) sampleBox.Items.Add(sample);
            root.Controls.Add(sampleBox, 1, 0);

            AddLabel(root, "配置名称", 1);
            ConfigureText(nameBox);
            root.Controls.Add(nameBox, 1, 1);

            AddLabel(root, "进程文件名", 2);
            ConfigureText(executableBox);
            root.Controls.Add(executableBox, 1, 2);

            AddLabel(root, "窗口类", 3);
            ConfigureText(classBox);
            root.Controls.Add(classBox, 1, 3);

            AddLabel(root, "标题筛选", 4);
            ConfigureText(titleBox);
            root.Controls.Add(titleBox, 1, 4);

            AddLabel(root, "身份来源", 5);
            identityBox.DropDownStyle = ComboBoxStyle.DropDownList;
            identityBox.Dock = DockStyle.Fill;
            identityBox.Margin = new Padding(3, 4, 3, 4);
            foreach (ProfileIdentityChoice choice in IdentityChoices()) identityBox.Items.Add(choice);
            identityBox.SelectedIndexChanged += delegate { UpdatePatternState(); };
            root.Controls.Add(identityBox, 1, 5);

            AddLabel(root, "正则表达式", 6);
            ConfigureText(patternBox);
            root.Controls.Add(patternBox, 1, 6);

            patternHint.Text = "正则可使用命名捕获组 (?<identity>...)；标题后缀适合“程序名--角色名”。";
            patternHint.Dock = DockStyle.Fill;
            patternHint.ForeColor = Color.DimGray;
            patternHint.AutoSize = true;
            root.Controls.Add(patternHint, 1, 7);

            WrappingPanel flags = new WrappingPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            allowCloseBox.Text = "允许关闭全部客户端";
            allowCloseBox.AutoSize = true;
            enabledBox.Text = "启用此配置";
            enabledBox.AutoSize = true;
            flags.Controls.Add(allowCloseBox);
            flags.Controls.Add(enabledBox);
            root.Controls.Add(new Label { Text = "操作权限", AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 8);
            root.Controls.Add(flags, 1, 8);

            AddLabel(root, "识别测试", 9);
            testResultBox.ReadOnly = true;
            testResultBox.Multiline = true;
            testResultBox.ScrollBars = ScrollBars.Vertical;
            testResultBox.Dock = DockStyle.Fill;
            testResultBox.Margin = new Padding(3, 3, 3, 3);
            testResultBox.BackColor = Color.FromArgb(248, 250, 252);
            testResultBox.Text = "点击“测试识别”查看当前规则能匹配到的窗口。";
            root.Controls.Add(testResultBox, 1, 9);

            WrappingPanel actions = new WrappingPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = true,
                Padding = new Padding(0, 4, 0, 0)
            };
            Button cancel = UiSizing.Button("取消"); cancel.DialogResult = DialogResult.Cancel;
            Button save = UiSizing.Button("保存");
            Button read = UiSizing.Button("读取选中样本");
            Button test = UiSizing.Button("测试识别");
            read.Click += delegate { ReadSelectedSample(); };
            test.Click += delegate { TestRecognition(); };
            save.Click += delegate { SaveAndClose(); };
            actions.Controls.Add(cancel);
            actions.Controls.Add(save);
            actions.Controls.Add(test);
            actions.Controls.Add(read);
            frame.Controls.Add(actions, 0, 1);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static void AddLabel(TableLayoutPanel root, string text, int row)
        {
            root.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(70, 78, 90)
            }, 0, row);
        }

        private static void ConfigureText(TextBox box)
        {
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(3, 4, 3, 4);
        }

        private void LoadProfile()
        {
            nameBox.Text = profile.Name ?? "";
            executableBox.Text = profile.ExecutableName ?? "";
            classBox.Text = profile.WindowClass ?? "";
            titleBox.Text = profile.TitleContains ?? "";
            patternBox.Text = profile.IdentityPattern ?? "";
            allowCloseBox.Checked = profile.AllowClose;
            enabledBox.Checked = profile.Enabled;
            SelectIdentity(profile.IdentitySource);
            UpdatePatternState();
        }

        private void SelectIdentity(string value)
        {
            for (int i = 0; i < identityBox.Items.Count; i++)
            {
                ProfileIdentityChoice choice = identityBox.Items[i] as ProfileIdentityChoice;
                if (choice != null && string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    identityBox.SelectedIndex = i;
                    return;
                }
            }
            identityBox.SelectedIndex = 0;
        }

        private void UpdatePatternState()
        {
            ProfileIdentityChoice choice = identityBox.SelectedItem as ProfileIdentityChoice;
            bool regex = choice != null &&
                (string.Equals(choice.Value, TargetProfile.IdentityTitleRegex, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(choice.Value, TargetProfile.IdentityThreadRegex, StringComparison.OrdinalIgnoreCase));
            patternBox.Enabled = regex;
            patternHint.Text = regex
                ? "正则可使用命名捕获组 (?<identity>...)；没有捕获组时使用第一个分组。"
                : "正则仅在选择标题正则或线程正则时使用。标题后缀适合“程序名--角色名”。";
        }

        private void ReadSelectedSample()
        {
            WindowSample sample = sampleBox.SelectedItem as WindowSample;
            if (sample == null) return;
            if (string.IsNullOrWhiteSpace(nameBox.Text) || string.Equals(nameBox.Text, "新目标程序", StringComparison.OrdinalIgnoreCase))
                nameBox.Text = sample.ExecutableName;
            executableBox.Text = sample.ExecutableName ?? "";
            classBox.Text = sample.WindowClass ?? "";
            titleBox.Text = "";
            if (!string.IsNullOrWhiteSpace(sample.ThreadDescription))
                SelectIdentity(TargetProfile.IdentityThread);
            else
                SelectIdentity(TargetProfile.IdentityTitleSuffix);
            UpdatePatternState();
        }

        private bool TryBuildProfile(out TargetProfile result, out string error)
        {
            result = profile.Clone();
            error = "";
            string name = (nameBox.Text ?? "").Trim();
            string executable = (executableBox.Text ?? "").Trim();
            string windowClass = (classBox.Text ?? "").Trim();
            if (name.Length == 0)
            {
                error = "请填写配置名称。";
                return false;
            }
            if (executable.Length == 0 && windowClass.Length == 0)
            {
                error = "进程文件名和窗口类至少填写一项。";
                return false;
            }
            ProfileIdentityChoice choice = identityBox.SelectedItem as ProfileIdentityChoice;
            if (choice == null)
            {
                error = "请选择身份来源。";
                return false;
            }
            if (patternBox.Enabled && patternBox.Text.Trim().Length > 0)
            {
                try { new Regex(patternBox.Text.Trim()); }
                catch (ArgumentException ex)
                {
                    error = "正则表达式格式有误：" + ex.Message;
                    return false;
                }
            }
            result.Name = name;
            result.ExecutableName = executable;
            result.WindowClass = windowClass;
            result.TitleContains = (titleBox.Text ?? "").Trim();
            result.IdentitySource = choice.Value;
            result.IdentityPattern = (patternBox.Text ?? "").Trim();
            result.AllowClose = allowCloseBox.Checked;
            result.Enabled = enabledBox.Checked;
            return true;
        }

        private void SaveAndClose()
        {
            TargetProfile next;
            string error;
            if (!TryBuildProfile(out next, out error))
            {
                MessageBox.Show(this, error, "目标程序", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            profile.Name = next.Name;
            profile.ExecutableName = next.ExecutableName;
            profile.WindowClass = next.WindowClass;
            profile.TitleContains = next.TitleContains;
            profile.IdentitySource = next.IdentitySource;
            profile.IdentityPattern = next.IdentityPattern;
            profile.AllowClose = next.AllowClose;
            profile.Enabled = next.Enabled;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void TestRecognition()
        {
            TargetProfile testProfile;
            string error;
            if (!TryBuildProfile(out testProfile, out error))
            {
                testResultBox.Text = error;
                return;
            }
            testProfile.Enabled = true;
            try
            {
                List<GameWindow> found = Native.FindGames(testProfile);
                StringBuilder output = new StringBuilder();
                output.AppendLine("匹配窗口：" + found.Count + " 个");
                Dictionary<string, int> identities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (GameWindow game in found)
                {
                    string identity = string.IsNullOrWhiteSpace(game.CharacterName) ? "未命名" : game.CharacterName;
                    int count;
                    identities.TryGetValue(identity, out count);
                    identities[identity] = count + 1;
                    output.AppendLine("· " + identity + "  |  " + game.Title);
                }
                List<string> duplicates = identities.Where(pair => pair.Value > 1)
                    .Select(pair => pair.Key + "（" + pair.Value + " 个）").ToList();
                if (duplicates.Count > 0)
                    output.AppendLine("重复身份：" + string.Join("、", duplicates));
                if (found.Count == 0)
                    output.AppendLine("请检查进程名、窗口类和标题筛选条件。");
                testResultBox.Text = output.ToString();
            }
            catch (Exception ex)
            {
                testResultBox.Text = "识别测试失败：" + ex.Message;
            }
        }

        internal static IEnumerable<ProfileIdentityChoice> IdentityChoices()
        {
            return new[]
            {
                new ProfileIdentityChoice(TargetProfile.IdentityTitleSuffix, "标题后缀"),
                new ProfileIdentityChoice(TargetProfile.IdentityTitle, "完整窗口标题"),
                new ProfileIdentityChoice(TargetProfile.IdentityThread, "线程描述"),
                new ProfileIdentityChoice(TargetProfile.IdentityTitleRegex, "标题正则"),
                new ProfileIdentityChoice(TargetProfile.IdentityThreadRegex, "线程正则"),
                new ProfileIdentityChoice(TargetProfile.IdentityMoli, "魔力宝贝兼容规则")
            };
        }
    }

    internal sealed class TargetProfileManagerDialog : AdaptiveForm
    {
        private readonly ListBox profileList = new ListBox();
        private readonly List<TargetProfile> profiles = new List<TargetProfile>();

        internal IList<TargetProfile> Profiles
        {
            get { return profiles.Select(profile => profile.Clone()).ToList(); }
        }

        internal TargetProfileManagerDialog(IList<TargetProfile> source)
        {
            if (source != null)
                foreach (TargetProfile profile in source)
                    if (profile != null) profiles.Add(profile.Clone());
            SettingsStore.EnsureTargetProfiles(new AppSettings { TargetProfiles = profiles });
            Text = "目标程序管理";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            SetInitialSize(new Size(600, 380));
            BuildControls();
            RefreshList();
        }

        private void BuildControls()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(10)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            profileList.Dock = DockStyle.Fill;
            profileList.IntegralHeight = false;
            profileList.SelectedIndexChanged += delegate { UpdateButtons(); };
            root.Controls.Add(profileList, 0, 0);

            FlowLayoutPanel editActions = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(4, 0, 0, 0)
            };
            Button add = MakeButton("新增", delegate { AddProfile(); });
            Button edit = MakeButton("编辑", delegate { EditProfile(); });
            Button delete = MakeButton("删除", delegate { DeleteProfile(); });
            editActions.Controls.Add(add);
            editActions.Controls.Add(edit);
            editActions.Controls.Add(delete);
            root.Controls.Add(editActions, 1, 0);

            WrappingPanel bottom = new WrappingPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = true
            };
            Button cancel = UiSizing.Button("取消"); cancel.DialogResult = DialogResult.Cancel;
            Button save = UiSizing.Button("保存");
            save.Click += delegate { SaveAndClose(); };
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(save);
            root.Controls.Add(bottom, 0, 1);
            root.SetColumnSpan(bottom, 2);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static Button MakeButton(string text, EventHandler handler)
        {
            Button button = UiSizing.Button(text);
            button.Click += handler;
            return button;
        }

        private void RefreshList(int selected = -1)
        {
            profileList.BeginUpdate();
            profileList.Items.Clear();
            foreach (TargetProfile profile in profiles) profileList.Items.Add(profile);
            if (profileList.Items.Count > 0)
                profileList.SelectedIndex = selected >= 0 && selected < profileList.Items.Count ? selected : 0;
            profileList.EndUpdate();
        }

        private void UpdateButtons() { }

        private IList<WindowSample> ReadSamples()
        {
            try { return Native.FindWindowSamples(); }
            catch { return new List<WindowSample>(); }
        }

        private void AddProfile()
        {
            using (TargetProfileEditorDialog editor = new TargetProfileEditorDialog(null, ReadSamples()))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                profiles.Add(editor.Profile.Clone());
                RefreshList(profiles.Count - 1);
            }
        }

        private void EditProfile()
        {
            int index = profileList.SelectedIndex;
            if (index < 0 || index >= profiles.Count) return;
            using (TargetProfileEditorDialog editor = new TargetProfileEditorDialog(profiles[index], ReadSamples()))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                profiles[index] = editor.Profile.Clone();
                RefreshList(index);
            }
        }

        private void DeleteProfile()
        {
            int index = profileList.SelectedIndex;
            if (index < 0 || index >= profiles.Count) return;
            if (string.Equals(profiles[index].Id, TargetProfile.MoliDefaultId, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "内置魔力宝贝配置会一直保留。", "目标程序", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "删除选中的目标程序配置？", "目标程序", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            profiles.RemoveAt(index);
            RefreshList(Math.Max(0, index - 1));
        }

        private void SaveAndClose()
        {
            SettingsStore.EnsureTargetProfiles(new AppSettings { TargetProfiles = profiles });
            List<string> conflicts = new List<string>();
            for (int i = 0; i < profiles.Count; i++)
            {
                for (int j = i + 1; j < profiles.Count; j++)
                {
                    TargetProfile first = profiles[i];
                    TargetProfile second = profiles[j];
                    if (first == null || second == null || !first.MayOverlap(second)) continue;
                    conflicts.Add("“" + first.Name + "” 与 “" + second.Name + "”\n  " +
                        first.MatchDescription() + "\n  " + second.MatchDescription());
                }
            }
            if (conflicts.Count > 0)
            {
                DialogResult choice = MessageBox.Show(this,
                    "发现可能匹配同一窗口的目标程序配置：\n\n" + string.Join("\n\n", conflicts) +
                    "\n\n仍然保存这些配置吗？",
                    "目标程序配置提示", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (choice != DialogResult.Yes) return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
