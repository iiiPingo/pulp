using System;

namespace Pulp
{
    // Zoom for the history window, in percent: 80% to 150% in 10% steps, 100% is normal.
    static class Zoom
    {
        public const int Min = 80, Max = 150, Normal = 100, StepSize = 10;

        public static int Clamp(int pct) => Math.Max(Min, Math.Min(Max, pct));

        // one step in (dir > 0) or out (dir < 0), snapped to the 10% grid
        public static int Step(int pct, int dir)
        {
            int snapped = (int)Math.Round(Clamp(pct) / (double)StepSize) * StepSize;
            return Clamp(snapped + Math.Sign(dir) * StepSize);
        }

        // settings.ini value; anything unreadable or out of range falls back to safe values
        public static int Parse(string v) => int.TryParse(v, out int z) ? Clamp(z) : Normal;
    }
}
