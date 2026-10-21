using System.Diagnostics;
using System.Text.Json;
using MusicSync.Core;
using MusicSync.Lighting;

var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks.Add(name);
}

var meter = new LevelMeter();
Check(meter.Update(0, true, 0.033, 0) == 0, "Silence stays dark");
Check(meter.Update(0.0001, true, 1, 0) == 0, "Noise gate rejects -80 dBFS");
for (var i = 0; i < 6; i++) meter.Update(0.2, true, 0.033, 0);
Check(meter.Level > 0.85, "Music responds within 200 ms");
for (var i = 0; i < 31; i++) meter.Update(0.2, false, 0.033, 0);
Check(meter.Level == 0, "Stale capture fades to black within 1.1 seconds");
Check(meter.Update(double.NaN, true, 0.1, 0) == 0, "Invalid RMS does not light LEDs");
var now = Stopwatch.GetTimestamp();
Check(!LevelMeter.IsFresh(0, now), "Missing sample is stale");
Check(LevelMeter.IsFresh(now, now), "Current sample is fresh");
Check(!LevelMeter.IsFresh(now - Stopwatch.Frequency, now), "One-second sample is stale");
meter.Reset();
var normal = meter.Update(0.01, true, 1, 0);
meter.Reset();
Check(meter.Update(0.01, true, 1, 12) > normal, "Sensitivity gain changes the response");

var frame = new LedColor[8];
FrameMapper.Fill(0, 1, frame);
Check(frame.All(c => c == default), "Zero level is all black");
FrameMapper.Fill(1, 1, frame);
Check(frame.All(c => c.R > 0 || c.G > 0), "Full level addresses all eight LEDs");
FrameMapper.Fill(0.5, 1, frame);
Check(frame.Take(4).All(c => c.G > 0) && frame.Skip(4).All(c => c == default), "Half level addresses first four LEDs");
FrameMapper.Fill(0.0625, 1, frame);
Check(frame[0].G == 110 && frame.Skip(1).All(c => c == default), "Fractional end segment avoids full-step flicker");
FrameMapper.Fill(1, 0, frame);
Check(frame.All(c => c == default), "Zero brightness is dark");
FrameMapper.Fill(double.NaN, 1, frame);
Check(frame.All(c => c == default), "Invalid level is dark");
FrameMapper.Fill(1, 0.5, frame);
Check(frame.All(c => c.R <= 128 && c.G <= 110), "Brightness scales channels");
for (var i = 0; i < 10000; i++) FrameMapper.Fill((i % 100) / 100.0, 0.65, frame);
var allocationStart = GC.GetAllocatedBytesForCurrentThread();
for (var i = 0; i < 10000; i++) FrameMapper.Fill((i % 100) / 100.0, 0.65, frame);
var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
Check(allocated == 0, $"Frame generation performs no managed allocation ({allocated} bytes)");

var audioCount = AudioChecks.Run();
var report = new { ObservedAtUtc = DateTimeOffset.UtcNow, Status = "Passed", CoreCheckCount = checks.Count, AudioCheckCount = audioCount, Checks = checks };
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (args.Length > 0)
{
    var path = Path.GetFullPath(args[0]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json);
}
