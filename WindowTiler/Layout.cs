using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MoliWindowTiler
{
    public enum LayoutAlignment
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        Center,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    // All inputs and outputs use physical desktop pixels, including negative origins.
    public sealed class LayoutPlan
    {
        public Rectangle Area;
        public Rectangle[] Windows;
        public int[] RowCounts;
        public LayoutAlignment Alignment;
        public int Gap;
        public double WorstHidden;
        public double HiddenRatio;
        public long ClippedArea;
        internal double ShapePenalty;

        public string Description
        {
            get
            {
                string grid = string.Join(" + ", RowCounts.Select(x => x.ToString()).ToArray());
                return RowCounts.Length + " 行（每行 " + grid + " 个）";
            }
        }
    }

    public static class LayoutEngine
    {
        public static LayoutPlan Calculate(Rectangle area, IList<Size> sizes, int columns)
        {
            return Calculate(area, sizes, columns, LayoutAlignment.Center, 0);
        }

        public static LayoutPlan Calculate(Rectangle area, IList<Size> sizes, int columns, LayoutAlignment alignment)
        {
            return Calculate(area, sizes, columns, alignment, 0);
        }

        public static LayoutPlan Calculate(Rectangle area, IList<Size> sizes, int columns,
            LayoutAlignment alignment, int gap)
        {
            ValidateInput(area, sizes);
            if (columns < 0 || columns > 6)
                throw new ArgumentException("列数应在 0 至 6 之间。");
            ValidateAlignment(alignment);
            ValidateGap(gap);

            List<int[]> candidates = new List<int[]>();
            if (columns > 0)
            {
                List<int> rows = new List<int>();
                for (int n = sizes.Count; n > 0; n -= Math.Min(n, columns))
                    rows.Add(Math.Min(n, columns));
                candidates.Add(rows.ToArray());
            }
            else
                Partitions(sizes.Count, new List<int>(), candidates);

            LayoutPlan best = null;
            foreach (int[] rows in candidates)
            {
                LayoutPlan plan = Build(area, sizes, rows, alignment, gap);
                if (best == null || Better(plan, best)) best = plan;
            }
            return best;
        }

        // Use an explicit row pattern when the user wants a deterministic layout
        // such as 3,3 or 2,2,2. The pattern is kept when the game enforces a
        // different outer size after SetWindowPos, so preview and actual placement
        // always use the same grouping.
        public static LayoutPlan Calculate(Rectangle area, IList<Size> sizes, IList<int> rowCounts)
        {
            return Calculate(area, sizes, rowCounts, LayoutAlignment.Center, 0);
        }

        public static LayoutPlan Calculate(Rectangle area, IList<Size> sizes, IList<int> rowCounts, LayoutAlignment alignment)
        {
            return Calculate(area, sizes, rowCounts, alignment, 0);
        }

        public static LayoutPlan Calculate(Rectangle area, IList<Size> sizes, IList<int> rowCounts,
            LayoutAlignment alignment, int gap)
        {
            if (rowCounts == null) throw new ArgumentNullException("rowCounts");
            ValidateInput(area, sizes);
            ValidateAlignment(alignment);
            ValidateGap(gap);
            int[] rows = rowCounts.ToArray();
            ValidateRows(sizes, rows);
            return Build(area, sizes, rows, alignment, gap);
        }

        private static void ValidateInput(Rectangle area, IList<Size> sizes)
        {
            if (area.Width <= 0 || area.Height <= 0)
                throw new ArgumentException("显示器可用区域为空。");
            if (sizes == null || sizes.Count == 0 || sizes.Count > 6)
                throw new ArgumentException("请选择 1 至 6 个游戏窗口。");
            if (sizes.Any(s => s.Width <= 0 || s.Height <= 0))
                throw new ArgumentException("窗口尺寸无效，请刷新列表。");
        }

        private static void ValidateAlignment(LayoutAlignment alignment)
        {
            if (!Enum.IsDefined(typeof(LayoutAlignment), alignment))
                throw new ArgumentException("对齐方式无效。", "alignment");
        }

        private static void ValidateGap(int gap)
        {
            if (gap < 0 || gap > 1000)
                throw new ArgumentException("窗口间距应在 0 至 1000 像素之间。", "gap");
        }

        private static void ValidateRows(IList<Size> sizes, IList<int> rows)
        {
            if (rows.Count == 0 || rows.Count > 6)
                throw new ArgumentException("行组合应包含 1 至 6 行。");
            int total = 0;
            foreach (int row in rows)
            {
                if (row < 1 || row > 6) throw new ArgumentException("每行至少 1 个、最多 6 个窗口。");
                total += row;
            }
            if (total != sizes.Count)
                throw new ArgumentException("自定义行组合的数量必须等于已选窗口数（当前为 " + sizes.Count + " 个）。");
            if (sizes.Count == 0 || sizes.Count > 6)
                throw new ArgumentException("请选择 1 至 6 个游戏窗口。");
        }

        private static void Partitions(int remaining, List<int> rows, List<int[]> result)
        {
            if (remaining == 0) { result.Add(rows.ToArray()); return; }
            for (int n = 1; n <= remaining; n++)
            {
                rows.Add(n);
                Partitions(remaining - n, rows, result);
                rows.RemoveAt(rows.Count - 1);
            }
        }

        private enum AxisAlignment { Start, Center, End }

        private static AxisAlignment HorizontalAlignment(LayoutAlignment alignment)
        {
            switch (alignment)
            {
                case LayoutAlignment.TopLeft:
                case LayoutAlignment.MiddleLeft:
                case LayoutAlignment.BottomLeft:
                    return AxisAlignment.Start;
                case LayoutAlignment.TopRight:
                case LayoutAlignment.MiddleRight:
                case LayoutAlignment.BottomRight:
                    return AxisAlignment.End;
                default:
                    return AxisAlignment.Center;
            }
        }

        private static AxisAlignment VerticalAlignment(LayoutAlignment alignment)
        {
            switch (alignment)
            {
                case LayoutAlignment.TopLeft:
                case LayoutAlignment.TopCenter:
                case LayoutAlignment.TopRight:
                    return AxisAlignment.Start;
                case LayoutAlignment.BottomLeft:
                case LayoutAlignment.BottomCenter:
                case LayoutAlignment.BottomRight:
                    return AxisAlignment.End;
                default:
                    return AxisAlignment.Center;
            }
        }

        private static int[] Positions(int origin, int length, int[] sizes, AxisAlignment alignment, int gap)
        {
            int[] positions = new int[sizes.Length];
            double total = sizes.Sum();
            double span = total + (double)gap * Math.Max(0, sizes.Length - 1);
            if (span <= length)
            {
                double free = length - span;
                double offset = alignment == AxisAlignment.Start ? 0
                    : alignment == AxisAlignment.End ? free : free / 2;
                double x = origin + offset;
                for (int i = 0; i < sizes.Length; i++)
                {
                    positions[i] = (int)Math.Round(x);
                    x += sizes[i] + gap;
                }
            }
            else if (sizes.Length == 1)
                positions[0] = alignment == AxisAlignment.End
                    ? Math.Max(origin, origin + length - sizes[0])
                    : origin; // Keep the title bar accessible for oversized windows.
            else if (alignment == AxisAlignment.End)
            {
                double overlap = (span - length) / (sizes.Length - 1);
                double x = origin + length;
                for (int i = sizes.Length - 1; i >= 0; i--)
                {
                    int maxStart = Math.Max(origin, origin + length - sizes[i]);
                    positions[i] = (int)Math.Round(Math.Max(origin, Math.Min(x - sizes[i], maxStart)));
                    x = positions[i] - gap + overlap;
                }
            }
            else
            {
                double overlap = (span - length) / (sizes.Length - 1);
                double x = 0;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int maxStart = Math.Max(origin, origin + length - sizes[i]);
                    positions[i] = (int)Math.Round(Math.Max(origin, Math.Min(origin + x, maxStart)));
                    x += sizes[i] + gap - overlap;
                }
            }
            return positions;
        }

        private static LayoutPlan Build(Rectangle area, IList<Size> sizes, int[] rows,
            LayoutAlignment alignment, int gap)
        {
            int[] heights = new int[rows.Length];
            int offset = 0;
            int naturalWidth = 0;
            for (int row = 0; row < rows.Length; row++)
            {
                int rowWidth = 0;
                for (int col = 0; col < rows[row]; col++)
                {
                    Size size = sizes[offset++];
                    rowWidth += size.Width;
                    heights[row] = Math.Max(heights[row], size.Height);
                }
                rowWidth += gap * Math.Max(0, rows[row] - 1);
                naturalWidth = Math.Max(naturalWidth, rowWidth);
            }
            int[] ys = Positions(area.Top, area.Height, heights, VerticalAlignment(alignment), gap);
            Rectangle[] rects = new Rectangle[sizes.Count];
            offset = 0;
            for (int row = 0; row < rows.Length; row++)
            {
                int[] widths = new int[rows[row]];
                for (int col = 0; col < rows[row]; col++) widths[col] = sizes[offset + col].Width;
                int[] xs = Positions(area.Left, area.Width, widths, HorizontalAlignment(alignment), gap);
                for (int col = 0; col < rows[row]; col++)
                {
                    Size size = sizes[offset];
                    rects[offset++] = new Rectangle(xs[col], ys[row], size.Width, size.Height);
                }
            }

            LayoutPlan plan = new LayoutPlan
            {
                Area = area, Windows = rects, RowCounts = rows,
                Alignment = alignment, Gap = gap
            };
            long totalArea = 0, hiddenArea = 0;
            for (int i = 0; i < rects.Length; i++)
            {
                long full = Surface(rects[i]);
                Rectangle visible = Rectangle.Intersect(rects[i], area);
                long clipped = full - Surface(visible);
                List<Rectangle> covers = new List<Rectangle>();
                for (int j = i + 1; j < rects.Length; j++)
                {
                    Rectangle cover = Rectangle.Intersect(visible, rects[j]);
                    if (cover.Width > 0 && cover.Height > 0) covers.Add(cover);
                }
                long hidden = clipped + UnionArea(covers);
                plan.ClippedArea += clipped;
                plan.WorstHidden = Math.Max(plan.WorstHidden, (double)hidden / full);
                totalArea += full;
                hiddenArea += hidden;
            }
            plan.HiddenRatio = (double)hiddenArea / totalArea;
            double naturalHeight = heights.Sum() + (double)gap * Math.Max(0, rows.Length - 1);
            double ratio = naturalWidth / naturalHeight;
            plan.ShapePenalty = Math.Abs(Math.Log(ratio / ((double)area.Width / area.Height)))
                + (rows.Max() - rows.Min()) * 0.08;
            return plan;
        }

        private static bool Better(LayoutPlan a, LayoutPlan b)
        {
            if (a.ClippedArea != b.ClippedArea) return a.ClippedArea < b.ClippedArea;
            if (Math.Abs(a.WorstHidden - b.WorstHidden) > 0.00001) return a.WorstHidden < b.WorstHidden;
            if (Math.Abs(a.HiddenRatio - b.HiddenRatio) > 0.00001) return a.HiddenRatio < b.HiddenRatio;
            return a.ShapePenalty < b.ShapePenalty - 0.00001;
        }

        private static long Surface(Rectangle r)
        {
            return (long)Math.Max(0, r.Width) * Math.Max(0, r.Height);
        }

        // Exact union avoids counting a triply-covered pixel more than once.
        private static long UnionArea(List<Rectangle> rects)
        {
            int[] xs = rects.SelectMany(r => new int[] { r.Left, r.Right }).Distinct().OrderBy(x => x).ToArray();
            long area = 0;
            for (int i = 1; i < xs.Length; i++)
            {
                Rectangle[] strip = rects.Where(r => r.Left < xs[i] && r.Right > xs[i - 1]).OrderBy(r => r.Top).ToArray();
                int bottom = int.MinValue;
                long covered = 0;
                foreach (Rectangle r in strip)
                {
                    covered += Math.Max(0, r.Bottom - Math.Max(r.Top, bottom));
                    bottom = Math.Max(bottom, r.Bottom);
                }
                area += covered * (xs[i] - xs[i - 1]);
            }
            return area;
        }
    }
}
