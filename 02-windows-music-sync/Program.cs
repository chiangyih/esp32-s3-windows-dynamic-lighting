using System.Diagnostics;
using MusicSync.UI;

namespace MusicSync;

internal static class Program
{
    internal const string DisplayName = "音樂動態燈光設置";
    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(true, @"Local\Esp32MusicSync", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("程式已在執行。請從工作列或通知區開啟既有視窗。", DisplayName);
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var options = LaunchOptions.Parse(args);
        using var form = new MainForm(options);
        try { Application.Run(form); }
        finally
        {
            form.Dispose();
            form.SaveExitReport();
        }
    }
}

internal sealed class LaunchOptions
{
    public bool AutoStart { get; private set; }
    public int DiagnosticSeconds { get; private set; }
    public string? ReportPath { get; private set; }
    public string? ScreenshotPath { get; private set; }
    public bool CloseViaButton { get; private set; }
    public bool CloseViaTray { get; private set; }
    public int CloseAfterMilliseconds { get; private set; }
    public bool ExerciseEffects { get; private set; }
    public bool ExerciseLifecycle { get; private set; }
    public bool ShowAbout { get; private set; }
    public int? LampCountOverride { get; private set; }
    public bool ExerciseLampCounts { get; private set; }

    public static LaunchOptions Parse(string[] args)
    {
        var result = new LaunchOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--autostart": result.AutoStart = true; break;
                case "--close-via-button": result.CloseViaButton = true; break;
                case "--close-via-tray": result.CloseViaTray = true; break;
                case "--exercise-effects": result.ExerciseEffects = true; break;
                case "--exercise-lifecycle": result.ExerciseLifecycle = true; break;
                case "--about": result.ShowAbout = true; break;
                case "--exercise-lamp-counts": result.ExerciseLampCounts = true; break;
                case "--lamp-count" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var lampCount) && lampCount >= 1 && lampCount <= 64)
                        result.LampCountOverride = lampCount;
                    break;
                case "--close-after-ms" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var closeMs))
                    {
                        result.CloseAfterMilliseconds = Math.Clamp(closeMs, 50, 60000);
                        result.AutoStart = true;
                    }
                    break;
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
