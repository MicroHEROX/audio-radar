using System.Text;
using AudioRadar.Audio;
using AudioRadar.Dsp;

namespace AudioRadar;

internal static class Diagnostics
{
    public static void WriteProbe(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Audio Radar endpoint probe");
        sb.AppendLine($"Time: {DateTime.Now:O}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"64-bit process: {Environment.Is64BitProcess}");
        sb.AppendLine();

        try
        {
            var endpoints = AudioEndpoint.EnumerateActive(out var defaultId);
            sb.AppendLine($"Default render endpoint id: {defaultId ?? "(none)"}");
            sb.AppendLine($"Active render endpoints: {endpoints.Count}");
            sb.AppendLine();

            foreach (var endpoint in endpoints)
            {
                sb.AppendLine(endpoint.IsDefault ? "[DEFAULT]" : "[endpoint]");
                sb.AppendLine($"  Name: {endpoint.FriendlyName}");
                sb.AppendLine($"  Desc: {endpoint.Description}");
                sb.AppendLine($"  FormFactor: {endpoint.FormFactor}");
                sb.AppendLine($"  Id: {endpoint.Id}");
                sb.AppendLine($"  PhysicalSpeakerMask: 0x{endpoint.PhysicalSpeakerMask:X}");
                if (endpoint.MixFormat is { } format)
                {
                    sb.AppendLine($"  MixFormat: {format.Summary}");
                    sb.AppendLine($"  FormatTag: 0x{format.FormatTag:X4}");
                    sb.AppendLine($"  SubFormat: {format.SubFormat}");
                    sb.AppendLine($"  BlockAlign: {format.BlockAlign}");
                    sb.AppendLine($"  BytesPerSample: {format.BytesPerSample}");
                    sb.AppendLine($"  HeightChannels: {ChannelLayout.HasHeightChannels(format.ChannelMask)}");
                    sb.AppendLine($"  ChannelOrder: {string.Join(", ", format.ChannelsLayout.Select(c => c.Label))}");
                }
                else
                {
                    sb.AppendLine("  MixFormat: unavailable");
                }

                sb.AppendLine();
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("PROBE FAILED");
            sb.AppendLine(ex.ToString());
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"Probe written to: {Path.GetFullPath(path)}");
    }

    public static void WriteCaptureTest(string path, int seconds)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Audio Radar loopback capture test");
        sb.AppendLine($"Time: {DateTime.Now:O}");
        sb.AppendLine($"Duration: {seconds}s");
        sb.AppendLine();

        WasapiLoopbackCapture? capture = null;
        try
        {
            capture = new WasapiLoopbackCapture();
            var gate = new object();
            var channels = 0;
            var sums = Array.Empty<double>();
            var peaks = Array.Empty<float>();
            long sampleFrames = 0;
            var ready = false;
            RadarEngine? engine = null;
            var directionSamples = new List<string>();

            var format = capture.Start(null, (samples, frames) =>
            {
                lock (gate)
                {
                    if (!ready)
                    {
                        return;
                    }

                    sampleFrames += frames;
                    for (var frame = 0; frame < frames; frame++)
                    {
                        var baseIndex = frame * channels;
                        for (var ch = 0; ch < channels; ch++)
                        {
                            var value = samples[baseIndex + ch];
                            sums[ch] += value * value;
                            peaks[ch] = Math.Max(peaks[ch], Math.Abs(value));
                        }
                    }
                }

                try
                {
                    engine?.Process(samples, frames);
                }
                catch
                {
                    // Keep the capture test alive; the direction report will be empty.
                }
            });

            channels = format.Channels;
            sums = new double[channels];
            peaks = new float[channels];
            engine = new RadarEngine(format.ChannelsLayout, (int)format.SampleRate)
            {
                SensitivityDb = 6,
                LowHz = 200,
                HighHz = 1600,
                HoldMs = 300,
            };
            lock (gate)
            {
                ready = true;
            }

            var start = DateTime.UtcNow;
            var end = start.AddSeconds(Math.Max(1, seconds));
            while (DateTime.UtcNow < end)
            {
                Thread.Sleep(250);
                var frame = engine.Latest;
                if (frame is not null)
                {
                    var elapsed = (DateTime.UtcNow - start).TotalSeconds;
                    directionSamples.Add(
                        $"{elapsed,5:0.0}s  {frame.DirectionText,-28} " +
                        $"conf={frame.Confidence:0.00}  {frame.OverallDb,7:0.0} dBFS");
                }
            }

            capture.Stop();

            sb.AppendLine($"Endpoint: {capture.EndpointId ?? "(default)"}");
            sb.AppendLine($"Format: {format.Summary}");
            sb.AppendLine($"Layout: {string.Join(", ", format.ChannelsLayout.Select(c => c.Label))}");
            sb.AppendLine($"Frames captured: {sampleFrames}");
            sb.AppendLine();
            sb.AppendLine("Channel RMS / peak:");
            for (var ch = 0; ch < format.Channels; ch++)
            {
                var rms = sampleFrames > 0 ? Math.Sqrt(sums[ch] / sampleFrames) : 0;
                var rmsDb = rms > 1e-9 ? 20 * Math.Log10(rms) : -90;
                var peakDb = peaks[ch] > 1e-9 ? 20 * Math.Log10(peaks[ch]) : -90;
                sb.AppendLine(
                    $"  {format.ChannelsLayout[ch].Label,-4} rms={rmsDb,7:0.0} dBFS  peak={peakDb,7:0.0} dBFS");
            }

            sb.AppendLine();
            sb.AppendLine("Direction samples:");
            foreach (var sample in directionSamples.TakeLast(40))
            {
                sb.AppendLine("  " + sample);
            }

            var nonZero = 0;
            for (var ch = 0; ch < format.Channels; ch++)
            {
                var rms = sampleFrames > 0 ? Math.Sqrt(sums[ch] / sampleFrames) : 0;
                if (rms > 1e-5)
                {
                    nonZero++;
                }
            }

            sb.AppendLine();
            sb.AppendLine(nonZero == 0
                ? "RESULT: no audio observed. Play something to the selected endpoint and retry."
                : $"RESULT: audio observed on {nonZero}/{format.Channels} channels.");
        }
        catch (Exception ex)
        {
            sb.AppendLine("CAPTURE TEST FAILED");
            sb.AppendLine(ex.ToString());
        }
        finally
        {
            capture?.Dispose();
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"Capture test written to: {Path.GetFullPath(path)}");
    }

    public static void WriteSelfTest(string path)
    {
        var sb = new StringBuilder();
        var failures = 0;
        sb.AppendLine("Audio Radar self-test");
        sb.AppendLine($"Time: {DateTime.Now:O}");
        sb.AppendLine();

        CheckLayout(sb, "stereo", 0x3, 2, "Stereo 2.0");
        CheckLayout(sb, "5.1", 0x3F, 6, "5.1");
        CheckLayout(sb, "7.1", 0x63F, 8, "7.1");
        CheckLayout(sb, "5.1.2", 0x503F, 8, "5.1.2 (PCM)");
        CheckLayout(sb, "7.1.2", 0x563F, 10, "7.1.2 (PCM)");
        CheckLayout(sb, "5.1.4", 0x2D03F, 10, "5.1.4 (PCM)");
        CheckLayout(sb, "7.1.4", 0x2D63F, 12, "7.1.4 (PCM)");

        CheckDirection(sb, "stereo right", 0x3, 2, Speaker.FrontRight, 90, 0);
        CheckDirection(sb, "5.1 rear-left", 0x3F, 6, Speaker.BackLeft, -135, 0);
        CheckDirection(sb, "7.1 side-right", 0x63F, 8, Speaker.SideRight, 90, 0);
        CheckDirection(sb, "5.1.2 top-front-left", 0x503F, 8, Speaker.TopFrontLeft, -30, 45);
        CheckDirection(sb, "7.1.4 top-back-right", 0x2D63F, 12, Speaker.TopBackRight, 135, 45);

        sb.AppendLine();
        sb.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Console.WriteLine(failures == 0
            ? $"Self-test: PASS ({Path.GetFullPath(path)})"
            : $"Self-test: FAIL ({failures}) ({Path.GetFullPath(path)})");
    }

    private static void CheckLayout(StringBuilder sb, string name, uint mask, int count, string expected)
    {
        var actual = ChannelLayout.Describe(mask, count);
        var ok = actual == expected;
        sb.AppendLine($"{(ok ? "PASS" : "FAIL")} layout {name}: {actual}");
    }

    private static void CheckDirection(
        StringBuilder sb, string name, uint mask, int channels, Speaker target,
        double expectedAz, double expectedEl)
    {
        var layout = ChannelLayout.FromMask(mask, channels);
        var targetIndex = -1;
        for (var i = 0; i < layout.Count; i++)
        {
            if (layout[i].Speaker == target)
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
        {
            sb.AppendLine($"FAIL direction {name}: target {target} not in layout");
            return;
        }

        const int sampleRate = 48000;
        var engine = new RadarEngine(layout, sampleRate)
        {
            SensitivityDb = 3,
            LowHz = 200,
            HighHz = 1200,
            HoldMs = 0,
        };

        var frames = sampleRate / 2;
        var chunkFrames = 480;
        var buffer = new float[chunkFrames * channels];
        for (var start = 0; start < frames; start += chunkFrames)
        {
            var count = Math.Min(chunkFrames, frames - start);
            Array.Clear(buffer, 0, buffer.Length);
            for (var i = 0; i < count; i++)
            {
                var sample = 0.35f * MathF.Sin(2 * MathF.PI * 500 * (start + i) / sampleRate);
                buffer[i * channels + targetIndex] = sample;
            }

            engine.Process(buffer, count);
        }

        var frame = engine.Latest;
        if (frame is null || !frame.HasDirection)
        {
            sb.AppendLine($"FAIL direction {name}: no direction (latest={frame?.DirectionText ?? "null"})");
            return;
        }

        var azError = Math.Abs(ChannelLayout.Normalize(frame.AzimuthDegrees - expectedAz));
        var elError = Math.Abs(frame.ElevationDegrees - expectedEl);
        var ok = azError <= 15 && elError <= 20;
        sb.AppendLine(
            $"{(ok ? "PASS" : "FAIL")} direction {name}: " +
            $"az={frame.AzimuthDegrees:0.0}° (want {expectedAz:0}°), " +
            $"el={frame.ElevationDegrees:0.0}° (want {expectedEl:0}°), " +
            $"conf={frame.Confidence:0.00} {frame.DirectionText}");
    }
}
