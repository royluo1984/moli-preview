using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace MoliWindowTiler
{
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
                    (Modifiers & ~0x000Fu) == 0 && Key > 0 && Key < 256 &&
                    Key != (uint)Keys.ShiftKey &&
                    Key != (uint)Keys.ControlKey && Key != (uint)Keys.Menu;
            }
        }

        public string ShortcutText
        {
            get
            {
                List<string> parts = new List<string>();
                if ((Modifiers & 2) != 0) parts.Add("Ctrl");
                if ((Modifiers & 1) != 0) parts.Add("Alt");
                if ((Modifiers & 4) != 0) parts.Add("Shift");
                if ((Modifiers & 8) != 0) parts.Add("Win");
                if (Key >= (uint)Keys.D0 && Key <= (uint)Keys.D9)
                    parts.Add((Key - (uint)Keys.D0).ToString());
                else if (Key >= (uint)Keys.NumPad0 && Key <= (uint)Keys.NumPad9)
                    parts.Add("Num" + (Key - (uint)Keys.NumPad0));
                else
                    parts.Add(((Keys)Key).ToString());
                return string.Join("+", parts);
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

        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWindows = 0x0008;
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
                Text = "登录角色后设置快捷键，重启后仍按角色匹配。选择“未设置”可移除绑定；离线角色的绑定也会保留。",
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

        private static IEnumerable<HotkeyModifierChoice> ModifierChoices()
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

        private static IEnumerable<HotkeyKeyChoice> KeyChoices()
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

        private static void SelectModifier(ComboBox box, uint value)
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

        private static void SelectKey(ComboBox box, Keys value)
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
            HashSet<uint> reserved = new HashSet<uint>(rows.Where(row => !row.Online &&
                ((HotkeyModifierChoice)row.ModifierBox.SelectedItem).Value == (ModControl | ModAlt))
                .Select(row => (uint)((HotkeyKeyChoice)row.KeyBox.SelectedItem).Value));
            Queue<Keys> available = new Queue<Keys>(Enumerable.Range(1, 10)
                .Select(number => (Keys)((int)Keys.D0 + number % 10))
                .Where(key => !reserved.Contains((uint)key)));
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
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HotkeyRow row in rows)
            {
                HotkeyModifierChoice modifier = row.ModifierBox.SelectedItem as HotkeyModifierChoice;
                HotkeyKeyChoice key = row.KeyBox.SelectedItem as HotkeyKeyChoice;
                if (modifier == null || key == null || key.Value == Keys.None) continue;
                string combination = modifier.Value + ":" + (uint)key.Value;
                if (!used.Add(combination))
                {
                    error = "快捷键重复：" + modifier.Text + " + " + key.Text;
                    return false;
                }
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
}
