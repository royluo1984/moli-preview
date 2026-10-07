using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace MoliWindowTiler
{
    internal static class HotkeyDefaults
    {
        internal const uint ModAlt = 0x0001;
        internal const uint ModControl = 0x0002;
        internal const uint ModShift = 0x0004;
        internal const uint ModWindows = 0x0008;
        internal const uint MinimizeAllModifiers = ModControl | ModAlt;
        internal const uint MinimizeAllKey = (uint)Keys.Oem3;
        internal const uint CycleClientsModifiers = ModControl;
        internal const uint CycleClientsKey = (uint)Keys.Oem3;
    }

    internal static class HotkeyFormatter
    {
        internal static bool IsValid(uint modifiers, uint key)
        {
            return modifiers > 0 && (modifiers & ~0x000Fu) == 0 && key > 0 && key < 256 &&
                key != (uint)Keys.ShiftKey && key != (uint)Keys.ControlKey && key != (uint)Keys.Menu;
        }

        internal static string Format(uint modifiers, uint key)
        {
            if (key == 0) return "未设置";
            List<string> parts = new List<string>();
            if ((modifiers & HotkeyDefaults.ModControl) != 0) parts.Add("Ctrl");
            if ((modifiers & HotkeyDefaults.ModAlt) != 0) parts.Add("Alt");
            if ((modifiers & HotkeyDefaults.ModShift) != 0) parts.Add("Shift");
            if ((modifiers & HotkeyDefaults.ModWindows) != 0) parts.Add("Win");
            if (key == (uint)Keys.Oem3)
                parts.Add("`");
            else if (key >= (uint)Keys.D0 && key <= (uint)Keys.D9)
                parts.Add((key - (uint)Keys.D0).ToString());
            else if (key >= (uint)Keys.NumPad0 && key <= (uint)Keys.NumPad9)
                parts.Add("Num" + (key - (uint)Keys.NumPad0));
            else
                parts.Add(((Keys)key).ToString());
            return string.Join("+", parts);
        }
    }

    [DataContract]
    internal sealed class ClientHotkeyBinding
    {
        [DataMember(Name = "identity", Order = 1)] public string Identity;
        [DataMember(Name = "modifiers", Order = 2)] public uint Modifiers;
        [DataMember(Name = "key", Order = 3)] public uint Key;

        public bool IsValid
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Identity) && Modifiers > 0 &&
                    HotkeyFormatter.IsValid(Modifiers, Key);
            }
        }

        public string ShortcutText
        {
            get
            {
                return HotkeyFormatter.Format(Modifiers, Key);
            }
        }

        public ClientHotkeyBinding Clone()
        {
            return new ClientHotkeyBinding
            {
                Identity = Identity,
                Modifiers = Modifiers,
                Key = Key
            };
        }
    }

    internal sealed class HotkeyModifierChoice
    {
        public readonly string Text;
        public readonly uint Value;

        public HotkeyModifierChoice(string text, uint value)
        {
            Text = text;
            Value = value;
        }

        public override string ToString() { return Text; }
    }

    internal sealed class HotkeyKeyChoice
    {
        public readonly string Text;
        public readonly Keys Value;

        public HotkeyKeyChoice(string text, Keys value)
        {
            Text = text;
            Value = value;
        }

        public override string ToString() { return Text; }
    }

    internal sealed class HotkeySettingsDialog : Form
    {
        private sealed class HotkeyRow
        {
            public readonly string Identity;
            public readonly ComboBox ModifierBox;
            public readonly ComboBox KeyBox;
            public readonly bool Online;

            public HotkeyRow(string identity, ComboBox modifierBox, ComboBox keyBox, bool online)
            {
                Identity = identity;
                ModifierBox = modifierBox;
                KeyBox = keyBox;
                Online = online;
            }
        }

        private const uint ModAlt = HotkeyDefaults.ModAlt;
        private const uint ModControl = HotkeyDefaults.ModControl;
        private const uint ModShift = HotkeyDefaults.ModShift;
        private const uint ModWindows = HotkeyDefaults.ModWindows;
        private readonly List<HotkeyRow> rows = new List<HotkeyRow>();
        private readonly List<GameWindow> games;
        private readonly List<ClientHotkeyBinding> existing;

        public List<ClientHotkeyBinding> Bindings { get; private set; }

        public HotkeySettingsDialog(IList<GameWindow> sourceGames, IList<ClientHotkeyBinding> sourceBindings)
        {
            games = (sourceGames ?? new List<GameWindow>()).Where(g => g != null).ToList();
            existing = (sourceBindings ?? new List<ClientHotkeyBinding>())
                .Where(b => b != null).Select(b => b.Clone()).ToList();
            Bindings = new List<ClientHotkeyBinding>();

            Text = "客户端快捷键";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(720, Math.Max(260, Math.Min(600,
                150 + Math.Max(games.Count, existing.Count) * 44)));

            BuildControls();
        }

        private void BuildControls()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(root);

            Label description = new Label
            {
                Dock = DockStyle.Fill,
                Text = "登录角色后设置快捷键，重启后仍按角色匹配。离线绑定会保留；不同身份可共用组合键，按下后会在对应在线客户端间轮换。",
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(description, 0, 0);

            TableLayoutPanel table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                ColumnCount = 4,
                RowCount = 1,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                Padding = new Padding(0)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.Controls.Add(table, 0, 1);

            AddHeader(table, "客户端", 0, 0);
            AddHeader(table, "状态", 1, 0);
            AddHeader(table, "组合键", 2, 0);
            AddHeader(table, "按键", 3, 0);
            foreach (GameWindow game in games)
            {
                ClientHotkeyBinding saved = existing.FirstOrDefault(binding =>
                    PositionStore.MatchesIdentity(binding.Identity, game));
                string identity = saved == null ? PositionStore.IdentityFor(game) : saved.Identity;
                AddRow(table, identity, true, saved);
            }
            foreach (ClientHotkeyBinding saved in existing)
                if (!rows.Any(row => string.Equals(row.Identity, saved.Identity, StringComparison.OrdinalIgnoreCase)))
                    AddRow(table, saved.Identity, false, saved);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 5, 0, 0)
            };
            root.Controls.Add(actions, 0, 2);

            Button autoButton = new Button { Text = "为在线角色分配 Ctrl+Alt+数字", AutoSize = true, Height = 28 };
            autoButton.Click += delegate { AssignDefaultShortcuts(); };
            actions.Controls.Add(autoButton);

            Button clearButton = new Button { Text = "清除全部", Width = 78, Height = 28 };
            clearButton.Click += delegate { ClearShortcuts(); };
            actions.Controls.Add(clearButton);

            Button okButton = new Button { Text = "保存", Width = 76, Height = 28, DialogResult = DialogResult.None };
            okButton.Click += delegate { SaveAndClose(); };
            actions.Controls.Add(okButton);

            Button cancelButton = new Button { Text = "取消", Width = 76, Height = 28, DialogResult = DialogResult.Cancel };
            actions.Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private static void AddHeader(TableLayoutPanel table, string text, int column, int row)
        {
            Label label = new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(238, 242, 247)
            };
            table.Controls.Add(label, column, row);
        }

        private void AddRow(TableLayoutPanel table, string identity, bool online, ClientHotkeyBinding saved)
        {
            if (string.IsNullOrWhiteSpace(identity) || rows.Any(existingRow =>
                string.Equals(existingRow.Identity, identity, StringComparison.OrdinalIgnoreCase))) return;
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Label clientLabel = new Label
            {
                Text = identity,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 4, 0)
            };
            table.Controls.Add(clientLabel, 0, row);

            Label identityLabel = new Label
            {
                Text = online ? "在线" : "离线",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0),
                ForeColor = Color.DimGray
            };
            table.Controls.Add(identityLabel, 1, row);

            ComboBox modifierBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(6)
            };
            foreach (HotkeyModifierChoice choice in ModifierChoices()) modifierBox.Items.Add(choice);
            modifierBox.SelectedIndex = 0;

            ComboBox keyBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(6)
            };
            foreach (HotkeyKeyChoice choice in KeyChoices()) keyBox.Items.Add(choice);
            keyBox.SelectedIndex = 0;

            if (saved != null)
            {
                SelectModifier(modifierBox, saved.Modifiers);
                SelectKey(keyBox, (Keys)saved.Key);
            }

            table.Controls.Add(modifierBox, 2, row);
            table.Controls.Add(keyBox, 3, row);
            rows.Add(new HotkeyRow(identity, modifierBox, keyBox, online));
        }

        internal static IEnumerable<HotkeyModifierChoice> ModifierChoices()
        {
            return new[]
            {
                new HotkeyModifierChoice("Ctrl + Alt", ModControl | ModAlt),
                new HotkeyModifierChoice("Ctrl + Shift", ModControl | ModShift),
                new HotkeyModifierChoice("Alt + Shift", ModAlt | ModShift),
                new HotkeyModifierChoice("Ctrl + Alt + Shift", ModControl | ModAlt | ModShift),
                new HotkeyModifierChoice("Ctrl", ModControl),
                new HotkeyModifierChoice("Alt", ModAlt),
                new HotkeyModifierChoice("Shift", ModShift),
                new HotkeyModifierChoice("Win + Ctrl", ModWindows | ModControl),
                new HotkeyModifierChoice("Win + Alt", ModWindows | ModAlt),
                new HotkeyModifierChoice("Win", ModWindows),
                new HotkeyModifierChoice("Win + Shift", ModWindows | ModShift),
                new HotkeyModifierChoice("Win + Ctrl + Alt", ModWindows | ModControl | ModAlt),
                new HotkeyModifierChoice("Win + Ctrl + Shift", ModWindows | ModControl | ModShift),
                new HotkeyModifierChoice("Win + Alt + Shift", ModWindows | ModAlt | ModShift),
                new HotkeyModifierChoice("Win + Ctrl + Alt + Shift", ModWindows | ModControl | ModAlt | ModShift)
            };
        }

        internal static IEnumerable<HotkeyKeyChoice> KeyChoices()
        {
            List<HotkeyKeyChoice> result = new List<HotkeyKeyChoice>
            {
                new HotkeyKeyChoice("未设置", Keys.None)
            };
            for (int i = 0; i <= 9; i++)
                result.Add(new HotkeyKeyChoice(i.ToString(), (Keys)((int)Keys.D0 + i)));
            for (int i = 0; i <= 9; i++)
                result.Add(new HotkeyKeyChoice("小键盘 " + i, (Keys)((int)Keys.NumPad0 + i)));
            for (char value = 'A'; value <= 'Z'; value++)
            {
                Keys key = (Keys)Enum.Parse(typeof(Keys), value.ToString());
                result.Add(new HotkeyKeyChoice(value.ToString(), key));
            }
            for (int i = 1; i <= 12; i++)
            {
                Keys key = (Keys)((int)Keys.F1 + i - 1);
                result.Add(new HotkeyKeyChoice("F" + i, key));
            }
            result.Add(new HotkeyKeyChoice("`", Keys.Oem3));
            result.Add(new HotkeyKeyChoice("Tab", Keys.Tab));
            result.Add(new HotkeyKeyChoice("Space", Keys.Space));
            result.Add(new HotkeyKeyChoice("PageUp", Keys.PageUp));
            result.Add(new HotkeyKeyChoice("PageDown", Keys.PageDown));
            result.Add(new HotkeyKeyChoice("Left", Keys.Left));
            result.Add(new HotkeyKeyChoice("Right", Keys.Right));
            result.Add(new HotkeyKeyChoice("Up", Keys.Up));
            result.Add(new HotkeyKeyChoice("Down", Keys.Down));
            return result;
        }

        internal static void SelectModifier(ComboBox box, uint value)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                HotkeyModifierChoice choice = box.Items[i] as HotkeyModifierChoice;
                if (choice != null && choice.Value == value)
                {
                    box.SelectedIndex = i;
                    return;
                }
            }
            box.SelectedIndex = 0;
        }

        internal static void SelectKey(ComboBox box, Keys value)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                HotkeyKeyChoice choice = box.Items[i] as HotkeyKeyChoice;
                if (choice != null && choice.Value == value)
                {
                    box.SelectedIndex = i;
                    return;
                }
            }
            box.Items.Add(new HotkeyKeyChoice(value.ToString(), value));
            box.SelectedIndex = box.Items.Count - 1;
        }

        private void AssignDefaultShortcuts()
        {
            Queue<Keys> available = new Queue<Keys>(Enumerable.Range(1, 10)
                .Select(number => (Keys)((int)Keys.D0 + number % 10)));
            foreach (HotkeyRow row in rows.Where(row => row.Online))
            {
                SelectModifier(row.ModifierBox, ModControl | ModAlt);
                SelectKey(row.KeyBox, available.Count > 0 ? available.Dequeue() : Keys.None);
            }
        }

        private void ClearShortcuts()
        {
            foreach (HotkeyRow row in rows) SelectKey(row.KeyBox, Keys.None);
        }

        private void SaveAndClose()
        {
            string error;
            if (!TryCollectBindings(out error))
            {
                MessageBox.Show(this, error, "快捷键设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        internal bool TryCollectBindings(out string error)
        {
            error = "";
            List<ClientHotkeyBinding> next = new List<ClientHotkeyBinding>();
            foreach (HotkeyRow row in rows)
            {
                HotkeyModifierChoice modifier = row.ModifierBox.SelectedItem as HotkeyModifierChoice;
                HotkeyKeyChoice key = row.KeyBox.SelectedItem as HotkeyKeyChoice;
                if (modifier == null || key == null || key.Value == Keys.None) continue;
                if (string.IsNullOrWhiteSpace(row.Identity)) continue;
                next.Add(new ClientHotkeyBinding
                {
                    Identity = row.Identity,
                    Modifiers = modifier.Value,
                    Key = (uint)key.Value
                });
            }
            Bindings = next;
            return true;
        }
    }

    internal sealed class GlobalHotkeySettingsDialog : Form
    {
        private readonly ComboBox modifierBox = new ComboBox();
        private readonly ComboBox keyBox = new ComboBox();
        private readonly Label previewLabel = new Label();
        private readonly uint initialModifiers;
        private readonly uint initialKey;
        private readonly uint defaultModifiers;
        private readonly uint defaultKey;
        private readonly string descriptionText;

        public uint Modifiers { get; private set; }
        public uint Key { get; private set; }

        public GlobalHotkeySettingsDialog(string title, string descriptionText,
            uint defaultModifiers, uint defaultKey, uint modifiers, uint key)
        {
            Modifiers = modifiers;
            Key = key;
            initialModifiers = modifiers;
            initialKey = key;
            this.defaultModifiers = defaultModifiers;
            this.defaultKey = defaultKey;
            this.descriptionText = descriptionText;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(430, 190);
            BuildControls();
        }

        private void BuildControls()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            Controls.Add(root);

            Label description = new Label
            {
                Text = descriptionText,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(description, 0, 0);
            root.SetColumnSpan(description, 2);

            root.Controls.Add(new Label
            {
                Text = "组合键",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);
            modifierBox.DropDownStyle = ComboBoxStyle.DropDownList;
            modifierBox.Dock = DockStyle.Fill;
            modifierBox.Margin = new Padding(3, 5, 3, 5);
            foreach (HotkeyModifierChoice choice in HotkeySettingsDialog.ModifierChoices())
                modifierBox.Items.Add(choice);
            root.Controls.Add(modifierBox, 1, 1);

            root.Controls.Add(new Label
            {
                Text = "按键",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 2);
            keyBox.DropDownStyle = ComboBoxStyle.DropDownList;
            keyBox.Dock = DockStyle.Fill;
            keyBox.Margin = new Padding(3, 2, 3, 2);
            foreach (HotkeyKeyChoice choice in HotkeySettingsDialog.KeyChoices())
                keyBox.Items.Add(choice);
            root.Controls.Add(keyBox, 1, 2);

            previewLabel.Dock = DockStyle.Fill;
            previewLabel.ForeColor = Color.FromArgb(70, 78, 90);
            previewLabel.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(previewLabel, 0, 3);
            root.SetColumnSpan(previewLabel, 2);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            root.Controls.Add(actions, 0, 4);
            Button okButton = new Button { Text = "保存", Width = 76, Height = 28 };
            okButton.Click += delegate { SaveAndClose(); };
            Button cancelButton = new Button { Text = "取消", Width = 76, Height = 28, DialogResult = DialogResult.Cancel };
            Button defaultButton = new Button { Text = "恢复默认", Width = 82, Height = 28 };
            defaultButton.Click += delegate
            {
                HotkeySettingsDialog.SelectModifier(modifierBox, defaultModifiers);
                HotkeySettingsDialog.SelectKey(keyBox, (Keys)defaultKey);
                UpdatePreview();
            };
            actions.Controls.Add(cancelButton);
            actions.Controls.Add(okButton);
            actions.Controls.Add(defaultButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;

            modifierBox.SelectedIndexChanged += delegate { UpdatePreview(); };
            keyBox.SelectedIndexChanged += delegate { UpdatePreview(); };
            HotkeySettingsDialog.SelectModifier(modifierBox, initialModifiers);
            HotkeySettingsDialog.SelectKey(keyBox, initialKey == 0 ? Keys.None : (Keys)initialKey);
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            HotkeyModifierChoice modifier = modifierBox.SelectedItem as HotkeyModifierChoice;
            HotkeyKeyChoice key = keyBox.SelectedItem as HotkeyKeyChoice;
            uint modifierValue = modifier == null ? 0 : modifier.Value;
            uint keyValue = key == null ? 0 : (uint)key.Value;
            previewLabel.Text = "当前设置：" + HotkeyFormatter.Format(modifierValue, keyValue) +
                (keyValue == 0 ? "（已停用）" : "");
        }

        private void SaveAndClose()
        {
            HotkeyModifierChoice modifier = modifierBox.SelectedItem as HotkeyModifierChoice;
            HotkeyKeyChoice key = keyBox.SelectedItem as HotkeyKeyChoice;
            if (modifier == null || key == null) return;
            Modifiers = key.Value == Keys.None ? 0 : modifier.Value;
            Key = key.Value == Keys.None ? 0 : (uint)key.Value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
