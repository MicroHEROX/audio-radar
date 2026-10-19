using AudioRadar.UI;

namespace AudioRadar;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // No console attached (WinExe); diagnostics still write UTF-8 files.
        }

        if (args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "selftest.txt";
            Diagnostics.WriteSelfTest(path);
            return;
        }

        if (args.Any(a => string.Equals(a, "--capture-test", StringComparison.OrdinalIgnoreCase)))
        {
            var seconds = args
                .Where(a => int.TryParse(a, out _))
                .Select(int.Parse)
                .FirstOrDefault(v => v > 0);
            if (seconds <= 0)
            {
                seconds = 5;
            }

            var path = args.FirstOrDefault(a =>
                !a.StartsWith("--", StringComparison.Ordinal) && !int.TryParse(a, out _)) ?? "capture-test.txt";
            Diagnostics.WriteCaptureTest(path, seconds);
            return;
        }

        if (args.Any(a => string.Equals(a, "--probe", StringComparison.OrdinalIgnoreCase)))
        {
            var path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "probe.txt";
            Diagnostics.WriteProbe(path);
            return;
        }

        ApplicationConfiguration.Initialize();
        string? screenshotPath = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--screenshot", StringComparison.OrdinalIgnoreCase))
            {
                screenshotPath = args[i + 1];
                break;
            }
        }

        Application.Run(new MainForm(
            args.Any(a => string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase)),
            screenshotPath,
            args.Any(a => string.Equals(a, "--overlay", StringComparison.OrdinalIgnoreCase))));
    }
}
