using System;
using System.Windows.Forms;

namespace CodeWalker.DoorEditor.Controls
{
    internal static class SplitLayout
    {
        /// <summary>
        /// Creates a SplitContainer and applies SplitterDistance only after it has a real size,
        /// avoiding InvalidOperationException during construction.
        /// </summary>
        public static SplitContainer Create(
            Orientation orientation,
            int preferredDistance,
            int panel1Min = 120,
            int panel2Min = 120)
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = orientation,
                Panel1MinSize = Math.Min(panel1Min, 50),
                Panel2MinSize = Math.Min(panel2Min, 50),
            };

            void Apply(object? sender, EventArgs e)
            {
                ApplyDistance(split, preferredDistance, panel1Min, panel2Min);
            }

            split.HandleCreated += Apply;
            split.SizeChanged += Apply;
            return split;
        }

        public static void ApplyDistance(SplitContainer split, int preferredDistance, int panel1Min, int panel2Min)
        {
            if (!split.IsHandleCreated) return;

            int total = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            int avail = total - split.SplitterWidth;
            if (avail <= 0) return;

            int p1 = Math.Min(panel1Min, Math.Max(0, avail / 4));
            int p2 = Math.Min(panel2Min, Math.Max(0, avail / 4));
            if (p1 + p2 >= avail)
            {
                p1 = Math.Max(0, avail / 3);
                p2 = Math.Max(0, avail / 3);
            }

            try
            {
                split.Panel1MinSize = p1;
                split.Panel2MinSize = p2;
                int max = Math.Max(p1, avail - p2);
                int d = Math.Clamp(preferredDistance, p1, max);
                if (split.SplitterDistance != d)
                    split.SplitterDistance = d;
            }
            catch (InvalidOperationException)
            {
                // Size still too small — SizeChanged will retry.
            }
        }
    }
}
