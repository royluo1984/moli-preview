using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MoliWindowTiler
{
    // A compact, semi-transparent switcher inspired by the quick client
    // selection part of EVE-O Preview. It only activates an existing window.
    public sealed class SwitcherOverlay : Form
    {
        private readonly Action<IntPtr> activate;
        private readonly FlowLayoutPanel buttons = new FlowLayoutPanel();
        private readonly Label caption = new Label();
        private readonly Button closeButton = new Button();
        private Button pressedButton;
        private Point pressedPoint;
        private bool dragging;
        private bool ignoreNextClick;

        public event EventHandler UserClosed;
        public event Action<IList<IntPtr>> OrderChanged;

        public SwitcherOverlay(Action<IntPtr> activate)
        {
            this.activate = activate;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(32, 36, 44);
            ForeColor = Color.White;
            Opacity = 0.86;
            Padding = new Padding(6);
            Width = 390;
            Height = 92;

            caption.Text = "  点击切换；拖动交换位置";
            caption.ForeColor = Color.White;
            caption.Font = new Font(Font, FontStyle.Bold);
            caption.AutoSize = false;
            caption.TextAlign = ContentAlignment.MiddleLeft;
            caption.Location = new Point(6, 4);
            caption.Size = new Size(240, 24);
            Controls.Add(caption);

            closeButton.Text = "×";
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.BackColor = Color.FromArgb(75, 80, 92);
            closeButton.ForeColor = Color.White;
            closeButton.Font = new Font(Font, FontStyle.Bold);
            closeButton.Size = new Size(28, 24);
            closeButton.Location = new Point(Width - 34, 4);
            closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeButton.Click += delegate { HideByUser(); };
            Controls.Add(closeButton);

            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = true;
            buttons.AutoScroll = true;
            buttons.Location = new Point(6, 32);
            buttons.Size = new Size(Width - 12, Height - 38);
            buttons.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            buttons.Padding = new Padding(0);
            buttons.AllowDrop = true;
            Controls.Add(buttons);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return value;
            }
        }

        public void SetWindows(IList<GameWindow> games, Func<GameWindow, string> hotkeyText = null)
        {
            buttons.SuspendLayout();
            try
            {
                foreach (Control control in buttons.Controls.Cast<Control>().ToList()) control.Dispose();
                buttons.Controls.Clear();
                int count = games == null ? 0 : games.Count;
                int buttonWidth = count <= 3 ? 118 : 112;
                foreach (GameWindow game in (games ?? new List<GameWindow>()).Where(g => g != null && Native.IsWindow(g.Handle)))
                {
                    Button button = new Button();
                    button.Text = ShortName(game.CharacterName);
                    button.Tag = game.Handle;
                    button.Width = buttonWidth;
                    button.Height = 30;
                    button.Margin = new Padding(2);
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Color.FromArgb(110, 155, 220);
                    button.BackColor = Color.FromArgb(54, 63, 78);
                    button.ForeColor = Color.White;
                    button.Cursor = Cursors.Hand;
                    button.TabStop = false;
                    button.MouseDown += ButtonMouseDown;
                    button.MouseMove += ButtonMouseMove;
                    button.MouseUp += ButtonMouseUp;
                    ToolTip tip = new ToolTip();
                    string thread = string.IsNullOrWhiteSpace(game.ThreadDescription)
                        ? game.ThreadId.ToString()
                        : game.ThreadId + " / " + game.ThreadDescription.Trim();
                    string shortcut = hotkeyText == null ? "" : hotkeyText(game);
                    tip.SetToolTip(button, game.CharacterName + "  (线程 " + thread + ")" +
                        (string.IsNullOrEmpty(shortcut) ? "" : "\n快捷键：" + shortcut));
                    button.Disposed += delegate { tip.Dispose(); };
                    button.Click += delegate(object sender, EventArgs args)
                    {
                        if (ignoreNextClick)
                        {
                            ignoreNextClick = false;
                            return;
                        }
                        Button clicked = sender as Button;
                        if (clicked != null && clicked.Tag is IntPtr) activate((IntPtr)clicked.Tag);
                    };
                    buttons.Controls.Add(button);
                }
                caption.Text = "  点击切换；拖动交换位置（" + buttons.Controls.Count + " 个）";
                Height = buttons.Controls.Count <= 3 ? 72 : 108;
                buttons.Height = Height - 38;
            }
            finally { buttons.ResumeLayout(true); }
        }

        public void ShowOnScreen(Screen screen)
        {
            if (screen == null) screen = Screen.PrimaryScreen;
            Rectangle area = screen.WorkingArea;
            Left = Math.Max(area.Left, area.Right - Width - 12);
            Top = area.Top + 12;
            if (!Visible) Show();
            else BringToFront();
        }

        public void HideByUser()
        {
            Hide();
            EventHandler handler = UserClosed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static string ShortName(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "未命名客户端" : value.Trim();
            return value.Length > 14 ? value.Substring(0, 14) + "…" : value;
        }

        private void ButtonMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            pressedButton = sender as Button;
            pressedPoint = e.Location;
            dragging = false;
        }

        private void ButtonMouseMove(object sender, MouseEventArgs e)
        {
            Button button = sender as Button;
            if (button == null || pressedButton != button || e.Button != MouseButtons.Left) return;
            if (dragging) return;
            Rectangle threshold = new Rectangle(
                pressedPoint.X - SystemInformation.DragSize.Width / 2,
                pressedPoint.Y - SystemInformation.DragSize.Height / 2,
                SystemInformation.DragSize.Width,
                SystemInformation.DragSize.Height);
            if (threshold.Contains(e.Location)) return;
            dragging = true;
            ignoreNextClick = true;
            button.Capture = true;
            button.BackColor = Color.FromArgb(88, 112, 150);
        }

        private void ButtonMouseUp(object sender, MouseEventArgs e)
        {
            Button source = sender as Button;
            if (source == null || pressedButton != source || e.Button != MouseButtons.Left) return;
            bool wasDragging = dragging;
            Button target = wasDragging ? FindDropTarget(source, Cursor.Position) : null;
            source.Capture = false;
            source.BackColor = Color.FromArgb(54, 63, 78);
            pressedButton = null;
            dragging = false;
            if (!wasDragging) return;
            if (target != null && target != source)
            {
                SwapButtons(source, target);
                RaiseOrderChanged();
            }
            ClearIgnoredClickLater();
        }

        private Button FindDropTarget(Button source, Point screenPoint)
        {
            Point point = buttons.PointToClient(screenPoint);
            foreach (Control control in buttons.Controls)
            {
                Button button = control as Button;
                if (button != null && button != source && button.Visible && button.Bounds.Contains(point))
                    return button;
            }
            return null;
        }

        private void SwapButtons(Button first, Button second)
        {
            int firstIndex = buttons.Controls.GetChildIndex(first);
            int secondIndex = buttons.Controls.GetChildIndex(second);
            if (firstIndex < 0 || secondIndex < 0 || firstIndex == secondIndex) return;
            List<Control> ordered = buttons.Controls.Cast<Control>().ToList();
            ordered[firstIndex] = second;
            ordered[secondIndex] = first;
            buttons.SuspendLayout();
            try
            {
                buttons.Controls.Clear();
                buttons.Controls.AddRange(ordered.ToArray());
            }
            finally { buttons.ResumeLayout(true); }
        }

        private void RaiseOrderChanged()
        {
            Action<IList<IntPtr>> handler = OrderChanged;
            if (handler == null) return;
            List<IntPtr> handles = new List<IntPtr>();
            foreach (Control control in buttons.Controls)
            {
                Button button = control as Button;
                if (button != null && button.Tag is IntPtr) handles.Add((IntPtr)button.Tag);
            }
            handler(handles);
        }

        private void ClearIgnoredClickLater()
        {
            if (!IsDisposed && IsHandleCreated)
            {
                try { BeginInvoke((MethodInvoker)delegate { ignoreNextClick = false; }); }
                catch { ignoreNextClick = false; }
            }
            else ignoreNextClick = false;
        }
    }
}
