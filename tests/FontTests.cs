// Off-Windows checks for the bundled fonts. Run: tests/run.sh
using System;
using System.Drawing;
using Pulp;

static class FontTests
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }

    static int Main()
    {
        var asm = typeof(Fonts).Assembly;
        foreach (var f in Fonts.Files)
            Check(asm.GetManifestResourceStream("Pulp.Fonts." + f) != null, "embedded: " + f);

        foreach (var n in new[] { Fonts.Sans, Fonts.Arabic })
            Check(Fonts.Family(n) != null, "loads family: " + n);
        // Windows names the SemiBold files by their own family ("... SmBld"); Mono's fontconfig
        // folds them into the regular family instead. Either way the file must load and not crash.
        using (var f = Fonts.Make(Fonts.SansSemi, 10f, "Segoe UI", FontStyle.Bold))
            Check(f.SizeInPoints > 9.9f, "semibold makes a usable font (got " + f.Name + " " + f.Style + ")");

        using (var f = Fonts.Make(Fonts.Sans, 12f, "Segoe UI"))
            Check(f.Name == Fonts.Sans && Math.Abs(f.SizeInPoints - 12f) < 0.01f, "Make() gives Plex Sans 12pt (got " + f.Name + " " + f.SizeInPoints + ")");
        using (var f = Fonts.Make(Fonts.Arabic, 12f, "Segoe UI"))
            Check(f.Name == Fonts.Arabic, "Make() gives Plex Sans Arabic (got " + f.Name + ")");
        using (var f = Fonts.Make("No Such Font", 10f, "Segoe UI"))
            Check(f.SizeInPoints > 9.9f, "missing font falls back without crashing (got " + f.Name + ")");

        // zoom steps (Zoom.cs)
        Check(Zoom.Step(100, 1) == 110 && Zoom.Step(100, -1) == 90, "zoom steps 10% in and out");
        Check(Zoom.Step(150, 1) == 150 && Zoom.Step(80, -1) == 80, "zoom stops at 80% and 150%");
        Check(Zoom.Step(103, 1) == 110 && Zoom.Step(500, -1) == 140, "odd values snap back onto the steps");
        Check(Zoom.Step(100, 240) == 110 && Zoom.Step(100, -360) == 90, "a big wheel turn is still one step");
        Check(Zoom.Parse("120") == 120 && Zoom.Parse("999") == 150 && Zoom.Parse("10") == 80, "settings value is kept in range");
        Check(Zoom.Parse("") == 100 && Zoom.Parse("abc") == 100, "missing or broken value means 100%");

        Console.WriteLine(fails == 0 ? "All tests passed" : fails + " failed");
        return fails == 0 ? 0 : 1;
    }
}
