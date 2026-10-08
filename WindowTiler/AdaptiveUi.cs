using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MoliWindowTiler
{
    internal static class UiSizing
    {
        internal static float ScaleFor(Size workingArea, int dpi)
        {
            float density = Math.Max(1, dpi) / 96f;
            float logicalWidth = workingArea.Width / density;
            float logicalHeight = workingArea.Height / density;
            // Resolution changes density gently; Windows text scaling remains dominant.
            float room = Math.Min(logicalWidth / 1920f, logicalHeight / 1040f);
            return density * Math.Max(0.9f, Math.Min(1.15f, (float)Math.Sqrt(room)));
        }

        internal static Size InitialSize(Size desired, Rectangle workingArea, float scale)
        {
            return new Size(Math.Max(1, Math.Min((int)Math.Round(desired.Width * scale), (int)(workingArea.Width * 0.8))),
                Math.Max(1, Math.Min((int)Math.Round(desired.Height * scale), (int)(workingArea.Height * 0.8))));
        }

        internal static int Unit(Control control, int value)
        {
            AdaptiveForm form = control.FindForm() as AdaptiveForm;
            return Math.Max(0, (int)Math.Round(value * (form == null ? 1f : form.UiScale)));
        }

        internal static Button Button(string text)
        {
            return new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 4, 10, 4), Margin = new Padding(3), MinimumSize = new Size(58, 0) };
        }

        internal static void ConfigureButton(Button button, string text)
        {
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(10, 4, 10, 4);
            button.Margin = new Padding(3);
            button.MinimumSize = new Size(58, 0);
        }
    }

    // All dimensions are captured once in logical units. Reapplying a monitor's
    // scale uses that baseline, never the previously rounded/scaled values.
    public class AdaptiveForm : Form
    {
        private sealed class Metrics
        {
            internal Padding Margin, Padding;
            internal Size Minimum;
            internal FontStyle Style;
            internal float[] Rows, Columns;
        }

        private readonly Dictionary<Control, Metrics> metrics = new Dictionary<Control, Metrics>();
        private readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        private Size desiredSize = new Size(700, 480);
        private bool ready, applying, pending, userSized, dpiDuringMove;
        private Size moveStart;
        private string screenName;
        private string systemFontFamily;
        private float systemFontPoints;
        private int currentDpi = 96;
        private FormWindowState previousWindowState;
        public float UiScale { get; private set; }
        protected bool ApplyingUiScale { get { return applying; } }
        protected Screen PreferredScreen { get; set; }
        protected Rectangle UiWorkingArea { get; private set; }

        protected AdaptiveForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            UiScale = 1;
            DoubleBuffered = true;
        }

        protected void SetInitialSize(Size logicalSize)
        {
            desiredSize = logicalSize;
            Size = UiSizing.InitialSize(logicalSize, Screen.FromPoint(Cursor.Position).WorkingArea, 1);
        }

        protected override void OnLoad(EventArgs e)
        {
            Screen screen = PreferredScreen ?? (Owner == null ? Screen.FromPoint(Cursor.Position) : Screen.FromControl(Owner));
            Rectangle area = screen.WorkingArea;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            ready = true;
            ApplyEnvironment(screen, DpiSupport.ForWindow(Handle), true);
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            base.OnLoad(e);
        }

        internal void ApplyEnvironment(Screen screen, int dpi, bool resize)
        {
            ApplyEnvironment(screen.WorkingArea, dpi, resize);
            screenName = screen.DeviceName;
        }

        // The explicit work area also allows layout verification without changing
        // the user's monitor resolution or desktop scaling settings.
        internal void ApplyEnvironment(Rectangle area, int dpi, bool resize)
        {
            if (applying || IsDisposed) return;
            applying = true;
            List<Control> layoutTree = ControlTree(this).ToList();
            foreach (Control control in layoutTree) control.SuspendLayout();
            try
            {
                currentDpi = Math.Max(96, dpi);
                UiWorkingArea = area;
                UiScale = UiSizing.ScaleFor(area.Size, currentDpi);
                using (Font systemFont = SystemFonts.MessageBoxFont)
                {
                    systemFontFamily = systemFont.FontFamily.Name;
                    systemFontPoints = Math.Max(9, systemFont.SizeInPoints);
                }
                ApplyControlMetrics(this);
                Size cap = UiSizing.InitialSize(new Size(Math.Min(480, desiredSize.Width), Math.Min(320, desiredSize.Height)), area, UiScale);
                MinimumSize = FormBorderStyle == FormBorderStyle.None ? Size.Empty : cap;
                if (resize && !userSized && WindowState == FormWindowState.Normal)
                    Size = UiSizing.InitialSize(desiredSize, area, UiScale);
            }
            finally
            {
                for (int i = layoutTree.Count - 1; i >= 0; i--) layoutTree[i].ResumeLayout(true);
                applying = false;
            }
            OnUiScaleChanged();
            Invalidate(true);
        }

        private static IEnumerable<Control> ControlTree(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (Control item in ControlTree(child)) yield return item;
        }

        protected void RefreshControlMetrics()
        {
            if (ready && !applying) ApplyControlMetrics(this);
        }

        private Padding Scaled(Padding value)
        {
            return new Padding(ScaleValue(value.Left), ScaleValue(value.Top), ScaleValue(value.Right), ScaleValue(value.Bottom));
        }

        private int ScaleValue(int value) { return Math.Max(0, (int)Math.Round(value * UiScale)); }

        private void ApplyControlMetrics(Control control)
        {
            Metrics original;
            if (!metrics.TryGetValue(control, out original))
            {
                original = new Metrics { Margin = control.Margin, Padding = control.Padding,
                    Minimum = control.MinimumSize, Style = control.Font.Style };
                TableLayoutPanel table = control as TableLayoutPanel;
                if (table != null)
                {
                    original.Rows = table.RowStyles.Cast<RowStyle>().Select(row => row.SizeType == SizeType.Absolute ? row.Height : -1).ToArray();
                    original.Columns = table.ColumnStyles.Cast<ColumnStyle>().Select(column => column.SizeType == SizeType.Absolute ? column.Width : -1).ToArray();
                }
                metrics.Add(control, original);
                control.Disposed += delegate { metrics.Remove(control); };
            }
            Font font;
            float fontSize = systemFontPoints * 96f / 72f * UiScale;
            string fontKey = systemFontFamily + ":" + original.Style + ":" + fontSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (!fonts.TryGetValue(fontKey, out font))
            {
                font = new Font(systemFontFamily, fontSize, original.Style, GraphicsUnit.Pixel);
                fonts.Add(fontKey, font);
            }
            control.Font = font;
            control.Margin = Scaled(original.Margin);
            control.Padding = Scaled(original.Padding);
            if (control != this)
                control.MinimumSize = new Size(ScaleValue(original.Minimum.Width), ScaleValue(original.Minimum.Height));
            TableLayoutPanel layout = control as TableLayoutPanel;
            if (layout != null && original.Rows != null)
            {
                for (int i = 0; i < Math.Min(original.Rows.Length, layout.RowStyles.Count); i++)
                    if (original.Rows[i] >= 0 && layout.RowStyles[i].SizeType == SizeType.Absolute)
                        layout.RowStyles[i].Height = original.Rows[i] * UiScale;
                for (int i = 0; i < Math.Min(original.Columns.Length, layout.ColumnStyles.Count); i++)
                    if (original.Columns[i] >= 0 && layout.ColumnStyles[i].SizeType == SizeType.Absolute)
                        layout.ColumnStyles[i].Width = original.Columns[i] * UiScale;
            }
            foreach (Control child in control.Controls) ApplyControlMetrics(child);
        }

        protected virtual void OnUiScaleChanged() { }

        private void QueueEnvironmentUpdate()
        {
            if (!ready || applying || pending || !IsHandleCreated || IsDisposed || Disposing) return;
            pending = true;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    pending = false;
                    if (!IsDisposed && !Disposing && WindowState != FormWindowState.Minimized)
                        ApplyEnvironment(Screen.FromControl(this), DpiSupport.ForWindow(Handle), true);
                });
            }
            catch (InvalidOperationException) { pending = false; }
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (ready && !applying && Screen.FromControl(this).DeviceName != screenName)
                QueueEnvironmentUpdate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ready && previousWindowState == FormWindowState.Minimized && WindowState == FormWindowState.Normal)
                QueueEnvironmentUpdate();
            previousWindowState = WindowState;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0231) { moveStart = Size; dpiDuringMove = false; } // WM_ENTERSIZEMOVE
            if (message.Msg == 0x0232 && !dpiDuringMove && Size != moveStart) userSized = true;
            if (message.Msg == 0x0214) userSized = true; // WM_SIZING, including a drag across monitors.
            if (message.Msg == 0x0112 && (message.WParam.ToInt64() & 0xFFF0) == 0xF030) userSized = true;
            if (message.Msg == 0x02E0 && ready) // WM_DPICHANGED; scaling is owned here, not by WinForms.
            {
                dpiDuringMove = true;
                NativeRect suggested = (NativeRect)Marshal.PtrToStructure(message.LParam, typeof(NativeRect));
                Rectangle bounds = suggested.Rectangle;
                Bounds = bounds;
                ApplyEnvironment(Screen.FromRectangle(bounds), message.WParam.ToInt32() & 0xFFFF, true);
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == 0x007E || message.Msg == 0x001A) QueueEnvironmentUpdate();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (Font font in fonts.Values) font.Dispose();
                fonts.Clear();
                metrics.Clear();
            }
        }
    }

    internal static class DpiSupport
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);

        internal static void Enable()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; }
            catch (EntryPointNotFoundException) { }
            try { if (SetProcessDpiAwareness(2) == 0) return; }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }

        internal static int ForWindow(IntPtr handle)
        {
            try { uint dpi = GetDpiForWindow(handle); if (dpi > 0) return (int)dpi; }
            catch (EntryPointNotFoundException) { }
            try
            {
                uint x, y;
                if (GetDpiForMonitor(Native.MonitorFromWindow(handle, 2), 0, out x, out y) == 0 && x > 0) return (int)x;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            using (Graphics graphics = Graphics.FromHwnd(handle)) return (int)Math.Round(graphics.DpiX);
        }
    }

    internal sealed class WrappingPanel : FlowLayoutPanel
    {
        internal WrappingPanel()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Fill;
            WrapContents = true;
            Margin = Padding.Empty;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 1 ? proposedSize.Width : ClientSize.Width;
            width = Math.Max(1, width - Padding.Horizontal);
            int x = 0, height = 0, rowHeight = 0;
            foreach (Control child in Controls)
            {
                if (!child.Visible) continue;
                Size size = child.AutoSize ? child.GetPreferredSize(new Size(width, 0)) : child.Size;
                int itemWidth = size.Width + child.Margin.Horizontal;
                int itemHeight = size.Height + child.Margin.Vertical;
                if (x > 0 && x + itemWidth > width) { height += rowHeight; x = 0; rowHeight = 0; }
                x += itemWidth;
                rowHeight = Math.Max(rowHeight, itemHeight);
            }
            return new Size(width + Padding.Horizontal, height + rowHeight + Padding.Vertical);
        }
    }

    internal sealed class SettingsGrid : TableLayoutPanel
    {
        private bool arranging;
        internal SettingsGrid()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top;
            Margin = Padding.Empty;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            UpdateColumns(ClientSize.Width);
            base.OnLayout(e);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            UpdateColumns(proposedSize.Width > 1 ? proposedSize.Width : ClientSize.Width);
            return base.GetPreferredSize(proposedSize);
        }

        private void UpdateColumns(int width)
        {
            if (!arranging && Controls.Count > 0)
            {
                arranging = true;
                try
                {
                    int columns = Math.Max(1, Math.Min(3, width / Math.Max(1, UiSizing.Unit(this, 260))));
                    if (ColumnCount != columns || RowCount != (Controls.Count + columns - 1) / columns)
                    {
                        SuspendLayout();
                        ColumnCount = columns;
                        RowCount = (Controls.Count + columns - 1) / columns;
                        ColumnStyles.Clear(); RowStyles.Clear();
                        for (int i = 0; i < columns; i++) ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
                        for (int i = 0; i < RowCount; i++) RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        for (int i = 0; i < Controls.Count; i++) SetCellPosition(Controls[i], new TableLayoutPanelCellPosition(i % columns, i / columns));
                        ResumeLayout(false);
                    }
                }
                finally { arranging = false; }
            }
        }
    }
}
