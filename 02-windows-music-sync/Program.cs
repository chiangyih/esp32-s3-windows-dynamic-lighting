using System.Diagnostics;
using MusicSync.UI;

namespace MusicSync;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(true, @"Local\Esp32MusicSync", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("音樂同步已在執行。請從工作列或通知區開啟既有視窗。", "ESP32 音樂同步");
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var options = LaunchOptions.Parse(args);
        Application.Run(new MainForm(options));
    }
}

internal sealed class LaunchOptions
{
    public bool AutoStart { get; private set; }
    public int DiagnosticSeconds { get; private set; }
    public string? ReportPath { get; private set; }
    public string? ScreenshotPath { get; private set; }

    public static LaunchOptions Parse(string[] args)
    {
        var result = new LaunchOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--autostart": result.AutoStart = true; break;
                case "--diagnose" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var seconds))
                    {
                        result.DiagnosticSeconds = Math.Clamp(seconds, 3, 600);
                        result.AutoStart = true;
                    }
                    break;
                case "--report" when i + 1 < args.Length: result.ReportPath = Path.GetFullPath(args[++i]); break;
                case "--screenshot" when i + 1 < args.Length: result.ScreenshotPath = Path.GetFullPath(args[++i]); break;
            }
        }
        return result;
    }
}
