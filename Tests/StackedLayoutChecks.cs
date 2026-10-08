using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace MoliWindowTiler
{
    internal static class StackedLayoutChecks
    {
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
                CheckEngine();
                CheckWindowPlacement();
                Console.WriteLine("PASS " + checks + " stacked-layout assertions; artifacts: " + AppDomain.CurrentDomain.BaseDirectory);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static void CheckEngine()
        {
            Rectangle area = new Rectangle(100, 40, 1200, 900);
            int[] largeX = { 100, 300, 500 }, largeY = { 40, 190, 340 };
            int[] smallX = { 100, 380, 660 }, smallY = { 40, 250, 460 };
            for (int count = 1; count <= 6; count++)
            for (int alignment = 0; alignment < 9; alignment++)
            {
                List<Size> sizes = Enumerable.Range(0, count).Select(i => i % 2 == 0 ? new Size(800, 600) : new Size(640, 480)).ToList();
                LayoutPlan plan = LayoutEngine.CalculateStacked(area, sizes, (LayoutAlignment)alignment);
                Check(plan.Stacked && plan.Windows.Length == count && plan.Description == "全部重叠", "Stacked layout metadata.");
                Check(plan.Gap == 0 && plan.ClippedArea == 0, "Stacking should have no gap or clipping in a sufficient work area.");
                for (int i = 0; i < count; i++)
                {
                    Point expected = new Point((i % 2 == 0 ? largeX : smallX)[alignment % 3], (i % 2 == 0 ? largeY : smallY)[alignment / 3]);
                    Check(plan.Windows[i] == new Rectangle(expected, sizes[i]), "Mixed sizes must share the selected anchor, alignment=" + alignment);
                }
            }
            Rectangle negativeArea = new Rectangle(-1920, -1080, 1920, 1040);
            LayoutPlan identical = LayoutEngine.CalculateStacked(negativeArea, Enumerable.Repeat(new Size(800, 600), 6).ToList(), LayoutAlignment.Center);
            Check(identical.Windows.All(r => r == new Rectangle(-1360, -860, 800, 600)), "Six equal clients should exactly overlap on a negative-origin monitor.");
            Check(Math.Abs(identical.HiddenRatio - 5.0 / 6) < 0.000001 && identical.WorstHidden == 1, "Stacked visibility measurement.");
            for (int alignment = 0; alignment < 9; alignment++)
            {
                LayoutPlan oversized = LayoutEngine.CalculateStacked(new Rectangle(40, 30, 640, 480), new[] { new Size(800, 600) }, (LayoutAlignment)alignment);
                Check(oversized.Windows[0] == new Rectangle(40, 30, 800, 600) && oversized.ClippedArea == 172800, "Oversized clients retain their original frame and accessible title bar.");
            }
            LayoutPlan rows = LayoutEngine.Calculate(new Rectangle(0, 0, 3200, 2000), Enumerable.Repeat(new Size(800, 600), 6).ToList(), new[] { 3, 3 }, LayoutAlignment.Center, 20);
            Check(!rows.Stacked && rows.RowCounts.SequenceEqual(new[] { 3, 3 }) && rows.HiddenRatio == 0, "Existing 3+3 layout metadata.");
            Check(rows.Windows[0] == new Rectangle(380, 390, 800, 600) && rows.Windows[5] == new Rectangle(2020, 1010, 800, 600), "Existing grid spacing and alignment.");
        }

        private static void CheckWindowPlacement()
        {
            SettingsStore settings = new SettingsStore();
            PositionStore positions = new PositionStore();
            Check(settings.FilePath.StartsWith(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase) &&
                positions.FilePath.StartsWith(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase), "Tests require an isolated output directory.");
            TargetProfile profile = new TargetProfile { Id = "stacked-fixture", Name = "重叠排列验证",
                ExecutableName = Path.GetFileName(Application.ExecutablePath), TitleContains = "Stacked fixture ", IdentitySource = TargetProfile.IdentityTitle, Enabled = true };
            Screen screen = Screen.PrimaryScreen;
            settings.Save(new AppSettings { ActiveTargetProfileId = profile.Id, TargetProfiles = new List<TargetProfile> { profile },
                CustomRows = "3,3", Gap = 27, Margin = 8, Alignment = (int)LayoutAlignment.Center, Monitor = screen.DeviceName,
                ShowSwitcher = false, MinimizeAllKey = 0, CycleClientsKey = 0 });
            List<Form> fixtures = new List<Form>();
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    Form fixture = new Form { Text = "Stacked fixture " + i, AutoScaleMode = AutoScaleMode.None,
                        StartPosition = FormStartPosition.Manual,
                        Size = i % 2 == 0 ? new Size(800, 600) : new Size(640, 480),
                        Location = new Point(screen.WorkingArea.Left + 20 + i * 10, screen.WorkingArea.Top + 20 + i * 10) };
                    fixtures.Add(fixture);
                    fixture.Show();
                    // Same-process fixtures share a thread/process identity. Seed
                    // separate named records so these tests exercise real placement
                    // without relying on separate game processes for each character.
                    positions.Set(new GameWindow { Handle = fixture.Handle, CharacterName = fixture.Text,
                        TargetProfileId = profile.Id, IdentityCandidates = new List<string> { fixture.Text } }, Native.WindowBounds(fixture.Handle));
                }
                positions.Save();
                Application.DoEvents();
                Dictionary<IntPtr, Size> originalSizes = fixtures.ToDictionary(f => f.Handle, f => Native.WindowBounds(f.Handle).Size);
                Dictionary<IntPtr, Size> originalClients = fixtures.ToDictionary(f => f.Handle, f => ClientSizeOf(f.Handle));
                using (MainForm main = new MainForm())
                {
                    main.Show(); Application.DoEvents();
                    ComboBox modes = Field<ComboBox>(main, "columnsBox");
                    ComboBox alignment = Field<ComboBox>(main, "alignmentBox");
                    NumericUpDown gap = Field<NumericUpDown>(main, "gapBox");
                    TextBox customRows = Field<TextBox>(main, "customRowsBox");
                    Check(modes.Items[5].ToString() == "自定义组合" && modes.FindStringExact("全部重叠") == 6, "Saved layout indexes must remain compatible.");
                    modes.SelectedIndex = 5;
                    Check(customRows.Enabled && gap.Enabled, "Custom-grid options should be available.");
                    modes.SelectedIndex = 6;
                    Check(!customRows.Enabled && !gap.Enabled && gap.Value == 27 && customRows.Text == "3,3", "Stacking disables inapplicable inputs and retains grid preferences.");
                    for (int index = 0; index < 9; index++)
                    {
                        alignment.SelectedIndex = index;
                        LayoutPlan preview = Field<LayoutPlan>(main, "currentPlan");
                        List<GameWindow> games = Field<List<GameWindow>>(main, "games");
                        Check(games.Count == 6 && preview != null && preview.Stacked, "Preview should recognize all six fixture windows: count=" + games.Count + "; " + Field<Label>(main, "planLabel").Text);
                        Check(!Field<Label>(main, "planLabel").Text.Contains("当前屏幕不足"), "Intentional overlap should not report insufficient screen space.");
                        Dictionary<IntPtr, Rectangle> expected = games.Select((game, i) => new { game.Handle, Bounds = preview.Windows[i] }).ToDictionary(pair => pair.Handle, pair => pair.Bounds);
                        Field<Button>(main, "arrangeButton").PerformClick();
                        Application.DoEvents();
                        foreach (GameWindow game in games)
                        {
                            Rectangle actual = Native.WindowBounds(game.Handle);
                            Check(actual == expected[game.Handle], "Actual placement must match the stacked preview after both movement passes, alignment=" + index);
                            Check(actual.Size == originalSizes[game.Handle] && ClientSizeOf(game.Handle) == originalClients[game.Handle], "Arranging must preserve outer and client-area sizes.");
                        }
                    }
                    // A size change made by the client after preview must be used
                    // by the second pass, with the stacking mode still selected.
                    alignment.SelectedIndex = (int)LayoutAlignment.Center;
                    fixtures[0].Size = new Size(760, 560);
                    Field<Button>(main, "arrangeButton").PerformClick();
                    Application.DoEvents();
                    Rectangle area = screen.WorkingArea; area.Inflate(-8, -8);
                    Rectangle changed = Native.WindowBounds(fixtures[0].Handle);
                    Check(changed.Size == new Size(760, 560) &&
                        Math.Abs(changed.Left + changed.Width / 2.0 - (area.Left + area.Width / 2.0)) <= 0.5 &&
                        Math.Abs(changed.Top + changed.Height / 2.0 - (area.Top + area.Height / 2.0)) <= 0.5, "The final pass must use the client's live size while preserving center alignment.");
                    using (Bitmap capture = new Bitmap(main.Width, main.Height))
                    {
                        main.DrawToBitmap(capture, new Rectangle(Point.Empty, capture.Size));
                        capture.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stacked-center.png"));
                    }
                    main.Close();
                }
                AppSettings saved = settings.Load();
                Check(saved.LayoutMode == 6 && saved.Alignment == (int)LayoutAlignment.Center && saved.Gap == 27 && saved.CustomRows == "3,3", "Stacking and alignment must survive settings serialization.");
                using (MainForm reopened = new MainForm())
                {
                    reopened.Show(); Application.DoEvents();
                    ComboBox modes = Field<ComboBox>(reopened, "columnsBox");
                    Check(modes.SelectedIndex == 6 && Field<LayoutPlan>(reopened, "currentPlan").Stacked && !Field<NumericUpDown>(reopened, "gapBox").Enabled, "Reopening should restore stacked mode and input state.");
                    modes.SelectedIndex = 5;
                    Check(Field<NumericUpDown>(reopened, "gapBox").Enabled && Field<TextBox>(reopened, "customRowsBox").Enabled &&
                        Field<LayoutPlan>(reopened, "currentPlan").RowCounts.SequenceEqual(new[] { 3, 3 }), "Returning to a grid restores its controls and row grouping.");
                    reopened.Close();
                }
            }
            finally { foreach (Form fixture in fixtures) fixture.Dispose(); }
        }

        private static Size ClientSizeOf(IntPtr handle)
        {
            NativeRect rect;
            if (!Native.GetClientRect(handle, out rect)) throw new InvalidOperationException("Fixture client rectangle missing.");
            return rect.Rectangle.Size;
        }

        private static T Field<T>(object owner, string name)
        {
            return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        }

        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
