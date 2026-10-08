using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MoliWindowTiler
{
    internal static class UiLayoutChecks
    {
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        private static int checks;

        [STAThread]
        private static int Main()
        {
            try
            {
                DpiSupport.Enable();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                SettingsStore settingsStore = new SettingsStore();
                Check(settingsStore.FilePath.StartsWith(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase), "Tests require an isolated output directory.");
                TargetProfile fixture = new TargetProfile { Id = "ui-fixture", Name = "界面验证", ExecutableName = "MoliUiFixtureNeverRunning.exe", Enabled = true };
                settingsStore.Save(new AppSettings { ActiveTargetProfileId = fixture.Id, TargetProfiles = new List<TargetProfile> { fixture },
                    ShowSwitcher = false, MinimizeAllKey = 0, CycleClientsKey = 0 });
                List<GameWindow> games = Enumerable.Range(1, 6).Select(i => new GameWindow { CharacterName = "测试角色" + i,
                    IdentityCandidates = new List<string> { "测试角色" + i } }).ToList();
                List<Form> fixtures = Enumerable.Range(1, 6).Select(i => new Form { Text = "UI fixture " + i }).ToList();
                for (int i = 0; i < games.Count; i++) games[i].Handle = fixtures[i].Handle;
                Func<AdaptiveForm>[] factories = {
                    () => new MainForm(),
                    () => new HotkeySettingsDialog(games, new List<ClientHotkeyBinding>()),
                    () => new GlobalHotkeySettingsDialog("顺序切换快捷键", "按下组合键后，按当前排列顺序激活下一个在线客户端。", 2, 192, 2, 192),
                    () => new TargetProfileEditorDialog(fixture, new List<WindowSample>()),
                    () => new TargetProfileManagerDialog(new List<TargetProfile> { fixture }),
                    () => { SwitcherOverlay overlay = new SwitcherOverlay(delegate { }); overlay.SetWindows(games); return overlay; }
                };
                Rectangle[] areas = { new Rectangle(0, 0, 800, 560), new Rectangle(0, 0, 1024, 728), new Rectangle(0, 0, 1366, 728),
                    new Rectangle(0, 0, 1920, 1032), new Rectangle(0, 0, 2560, 1400), new Rectangle(0, 0, 3840, 2112) };
                int[] dpis = { 96, 120, 144, 192 };
                foreach (Func<AdaptiveForm> factory in factories)
                {
                    using (AdaptiveForm form = factory())
                    {
                        Console.WriteLine("Checking " + form.GetType().Name);
                        form.Show(); Application.DoEvents();
                        foreach (Rectangle area in areas)
                        foreach (int dpi in dpis)
                        {
                            form.ApplyEnvironment(area, dpi, true);
                            Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
                            string scenario = form.GetType().Name + " " + area.Size + " DPI=" + dpi;
                            Check(form.Width <= (int)(area.Width * 0.8) && form.Height <= (int)(area.Height * 0.8), scenario + " exceeds 80%.");
                            foreach (ListView list in Descendants(form).OfType<ListView>().Where(list => list.Visible))
                                Check(list.ClientSize.Height >= list.Font.Height * 2, scenario + " crushes the client list.");
                            foreach (Button button in Descendants(form).OfType<Button>().Where(b => b.Visible))
                            {
                                Check(button.Height >= TextRenderer.MeasureText(button.Text, button.Font).Height, scenario + " clips button text: " + button.Text);
                                Check(button.Parent.DisplayRectangle.Contains(button.Bounds), scenario + " clips button in parent: " + button.Text + " " + button.Bounds + " parent=" + button.Parent.DisplayRectangle);
                            }
                            foreach (Control control in Descendants(form).Where(c => c.Visible &&
                                (c is Button || c is ComboBox || c is TextBox || c is CheckBox || c is Label)))
                                CheckReachable(control, scenario);
                            if ((area.Width == 800 && dpi == 192) || (area.Width == 1366 && dpi == 96) || (area.Width == 1920 && dpi == 192))
                            {
                                using (Bitmap capture = new Bitmap(form.Width, form.Height))
                                {
                                    form.DrawToBitmap(capture, new Rectangle(Point.Empty, capture.Size));
                                    capture.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, form.GetType().Name + "-" + area.Width + "-" + dpi + ".png"));
                                }
                            }
                        }
                        Rectangle normal = new Rectangle(0, 0, 1920, 1032);
                        form.ApplyEnvironment(normal, 96, true); Application.DoEvents();
                        Size original = form.Size; float originalFont = form.Font.Size;
                        for (int repeat = 0; repeat < 3; repeat++) { form.ApplyEnvironment(normal, 192, true); form.ApplyEnvironment(normal, 96, true); }
                        Check(form.Size == original && Math.Abs(form.Font.Size - originalFont) < 0.01f, form.GetType().Name + " DPI round trip drift.");
                        if (!(form is SwitcherOverlay))
                        {
                            SendMessage(form.Handle, 0x0231, IntPtr.Zero, IntPtr.Zero);
                            form.Size = new Size(1650, 900);
                            SendMessage(form.Handle, 0x0232, IntPtr.Zero, IntPtr.Zero);
                            Size userSize = form.Size;
                            form.ApplyEnvironment(normal, 96, true);
                            Check(form.Size == userSize, form.GetType().Name + " overwrote manual resize.");
                        }
                        form.Close();
                    }
                }
                foreach (Form fixtureWindow in fixtures) fixtureWindow.Dispose();
                Console.WriteLine("PASS " + checks + " layout assertions; artifacts: " + AppDomain.CurrentDomain.BaseDirectory);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls) { yield return child; foreach (Control item in Descendants(child)) yield return item; }
        }

        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void CheckReachable(Control control, string scenario)
        {
            Control child = control;
            while (child.Parent != null)
            {
                ScrollableControl scroll = child.Parent as ScrollableControl;
                if (scroll != null && scroll.AutoScroll) return;
                Check(child.Parent.ClientRectangle.Contains(child.Bounds), scenario + " clips " + control.Text + " at " + child.GetType().Name + " bounds=" + child.Bounds + " in " + child.Parent.ClientRectangle);
                child = child.Parent;
            }
        }
    }
}
