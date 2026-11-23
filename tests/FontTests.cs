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

        Console.WriteLine(fails == 0 ? "All font tests passed" : fails + " failed");
        return fails == 0 ? 0 : 1;
    }
}
