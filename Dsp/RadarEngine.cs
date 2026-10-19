using System.Numerics;
using AudioRadar.Audio;

namespace AudioRadar.Dsp;

public sealed record ChannelBlip(string Label, double AzimuthDegrees, double ElevationDegrees, float Intensity);

public sealed class RadarFrame
{
    public required DateTime Timestamp { get; init; }
    public required float[] ChannelDb { get; init; }
    public required float[] ChannelLevels { get; init; }
    public required float OverallDb { get; init; }
    public required double AzimuthDegrees { get; init; }
    public required double ElevationDegrees { get; init; }
    public required double Confidence { get; init; }
    public required bool HasDirection { get; init; }
    public required bool IsHeld { get; init; }
    public required bool IsSilent { get; init; }
    public required float Transient { get; init; }
    public required string DirectionText { get; init; }
    public required IReadOnlyList<ChannelBlip> Blips { get; init; }
}

/// <summary>
/// Turns interleaved endpoint audio into a directional radar frame.
///
/// The important design choice: direction comes from per-channel energy in a
/// user-selected band, not from a stereo HRTF model. On a discrete 5.1/7.1/
/// height-channel PCM bed this is a direct measurement of the game's panning.
/// On a stereo endpoint it degrades to a left/right estimate.
/// </summary>
public sealed class RadarEngine
{
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly int _frameSize;
    private readonly IReadOnlyList<SpeakerChannel> _layout;
    private readonly bool _stereo;
    private readonly float[] _frame;
    private readonly float[] _window;
    private readonly Complex[][] _fft;
    private readonly float[] _noiseAmp;
    private int _frameFill;
    private double _smoothedAz;
    private double _smoothedEl;
    private bool _hasSmoothed;
    private double _holdAz;
    private double _holdEl;
    private double _holdConfidence;
    private DateTime _holdUntil;
    private double _slowDb = -90;
    private RadarFrame? _latest;

    private int _sensitivityDb10 = 60;
    private int _lowHz = 120;
    private int _highHz = 2500;
    private int _holdMs = 450;

    public RadarEngine(IReadOnlyList<SpeakerChannel> layout, int sampleRate)
    {
        _layout = layout;
        _stereo = layout.Count == 2;
        _sampleRate = Math.Max(8000, sampleRate);
        _channels = Math.Max(1, layout.Count);
        _frameSize = NextPowerOfTwo(Math.Clamp((int)(_sampleRate * 0.01), 512, 4096));
        _frame = new float[_frameSize * _channels];
        _window = new float[_frameSize];
        for (var i = 0; i < _frameSize; i++)
        {
            _window[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (_frameSize - 1));
        }

        _fft = new Complex[_channels][];
        for (var ch = 0; ch < _channels; ch++)
        {
            _fft[ch] = new Complex[_frameSize];
        }

        _noiseAmp = new float[_channels];
        Array.Fill(_noiseAmp, 1e-8f);
    }

    public IReadOnlyList<SpeakerChannel> Layout => _layout;
    public int SampleRate => _sampleRate;
    public int FrameSize => _frameSize;
    public int Channels => _channels;
    public bool IsStereo => _stereo;

    public RadarFrame? Latest => Volatile.Read(ref _latest);

    public float SensitivityDb
    {
        get => Volatile.Read(ref _sensitivityDb10) / 10f;
        set => Volatile.Write(ref _sensitivityDb10, (int)Math.Clamp(value * 10, 0, 300));
    }

    public float LowHz
    {
        get => Volatile.Read(ref _lowHz);
        set => Volatile.Write(ref _lowHz, (int)Math.Clamp(value, 20, _sampleRate / 2 - 1));
    }

    public float HighHz
    {
        get => Volatile.Read(ref _highHz);
        set => Volatile.Write(ref _highHz, (int)Math.Clamp(value, 21, _sampleRate / 2 - 1));
    }

    public int HoldMs
    {
        get => Volatile.Read(ref _holdMs);
        set => Volatile.Write(ref _holdMs, Math.Clamp(value, 0, 2000));
    }

    public void Reset()
    {
        _frameFill = 0;
        Array.Fill(_noiseAmp, 1e-8f);
        _slowDb = -90;
        _hasSmoothed = false;
        _holdUntil = DateTime.MinValue;
        _holdConfidence = 0;
        Volatile.Write(ref _latest, null);
    }

    public void Process(float[] interleaved, int frames)
    {
        if (frames <= 0 || interleaved.Length < frames * _channels)
        {
            return;
        }

        var offsetFrames = 0;
        while (frames > 0)
        {
            var take = Math.Min(frames, _frameSize - _frameFill);
            Array.Copy(interleaved, offsetFrames * _channels, _frame, _frameFill * _channels,
                take * _channels);
            _frameFill += take;
            offsetFrames += take;
            frames -= take;

            if (_frameFill == _frameSize)
            {
                AnalyzeFrame();
                _frameFill = 0;
            }
        }
    }

    private void AnalyzeFrame()
    {
        var n = _frameSize;
        var channels = _channels;
        var lowHz = Math.Min(LowHz, HighHz - 1);
        var highHz = Math.Max(HighHz, lowHz + 1);
        var lowBin = Math.Clamp((int)Math.Floor(lowHz * n / _sampleRate), 1, n / 2 - 1);
        var highBin = Math.Clamp((int)Math.Ceiling(highHz * n / _sampleRate), lowBin + 1, n / 2);

        var bandAmp = new float[channels];
        var channelDb = new float[channels];
        var channelLevels = new float[channels];

        for (var ch = 0; ch < channels; ch++)
        {
            var fft = _fft[ch];
            double sumSquares = 0;
            for (var i = 0; i < n; i++)
            {
                var sample = _frame[i * channels + ch];
                sumSquares += sample * sample;
                fft[i] = new Complex(sample * _window[i], 0);
            }

            Fft.Transform(fft);

            double bandPower = 0;
            for (var bin = lowBin; bin < highBin; bin++)
            {
                var c = fft[bin];
                bandPower += c.Real * c.Real + c.Imaginary * c.Imaginary;
            }

            bandAmp[ch] = (float)Math.Sqrt(bandPower);
            var rms = Math.Sqrt(sumSquares / n);
            var db = ToDb(rms);
            channelDb[ch] = db;
            channelLevels[ch] = Math.Clamp((db + 90f) / 90f, 0f, 1f);
        }

        var gate = MathF.Pow(10, SensitivityDb / 20f);
        var weights = new float[channels];
        var maxWeight = 0f;
        double weightSum = 0;
        for (var ch = 0; ch < channels; ch++)
        {
            if (_layout[ch].IsLfe)
            {
                continue;
            }

            var noise = _noiseAmp[ch];
            var amp = bandAmp[ch];
            var w = Math.Max(0, amp - noise * gate);

            if (amp < noise)
            {
                _noiseAmp[ch] = noise * 0.98f + amp * 0.02f;
            }
            else
            {
                // Slow rise: sustained music should not immediately become "noise",
                // but a held footstep will eventually stop being a transient.
                _noiseAmp[ch] = noise * 0.999f + amp * 0.001f;
            }

            weights[ch] = w;
            maxWeight = Math.Max(maxWeight, w);
            weightSum += w;
        }

        var overallRms = 0.0;
        for (var ch = 0; ch < channels; ch++)
        {
            var rms = Math.Pow(10, channelDb[ch] / 20.0);
            overallRms += rms * rms;
        }

        overallRms = Math.Sqrt(overallRms / channels);
        var overallDb = ToDb(overallRms);
        var transient = Math.Clamp((overallDb - _slowDb) / 12.0, 0, 1);
        if (overallDb > _slowDb)
        {
            _slowDb = _slowDb * 0.7 + overallDb * 0.3;
        }
        else
        {
            _slowDb = _slowDb * 0.995 + overallDb * 0.005;
        }

        var currentDirection = false;
        double az = 0;
        double el = 0;
        double confidence = 0;
        var blips = new List<ChannelBlip>();

        if (maxWeight > 1e-8f && overallDb > -75)
        {
            double x = 0;
            double y = 0;
            double z = 0;
            var entropy = 0.0;
            var nonLfeCount = 0;

            for (var ch = 0; ch < channels; ch++)
            {
                if (_layout[ch].IsLfe)
                {
                    continue;
                }

                nonLfeCount++;
                var w = weights[ch];
                if (w <= 0)
                {
                    continue;
                }

                var p = w / weightSum;
                entropy -= p * Math.Log(p + 1e-12);
                var channelAz = _stereo ? (ch == 0 ? -90 : 90) : _layout[ch].AzimuthDegrees;
                var channelEl = _stereo ? 0 : _layout[ch].ElevationDegrees;
                var (vx, vy, vz) = ChannelLayout.UnitVector(channelAz, channelEl);
                x += w * vx;
                y += w * vy;
                z += w * vz;
            }

            var magnitude = Math.Sqrt(x * x + y * y + z * z);
            var coherence = weightSum > 0 ? magnitude / weightSum : 0;
            var maxEntropy = Math.Log(Math.Max(2, nonLfeCount));
            var entropyNorm = maxEntropy > 0 ? entropy / maxEntropy : 0;
            confidence = Math.Clamp(0.75 * coherence + 0.25 * (1 - entropyNorm), 0, 1);

            if (coherence > 0.08)
            {
                currentDirection = true;
                az = Math.Atan2(x, y) * 180.0 / Math.PI;
                el = Math.Atan2(z, Math.Sqrt(x * x + y * y)) * 180.0 / Math.PI;

                if (!_hasSmoothed)
                {
                    _smoothedAz = az;
                    _smoothedEl = el;
                    _hasSmoothed = true;
                }
                else
                {
                    var t = 0.28 + 0.25 * transient;
                    _smoothedAz = LerpAngle(_smoothedAz, az, t);
                    _smoothedEl = _smoothedEl + (el - _smoothedEl) * t;
                }

                if (transient > 0.18 && confidence > 0.22)
                {
                    _holdAz = _smoothedAz;
                    _holdEl = _smoothedEl;
                    _holdConfidence = confidence;
                    _holdUntil = DateTime.UtcNow.AddMilliseconds(HoldMs);
                }

                for (var ch = 0; ch < channels; ch++)
                {
                    if (_layout[ch].IsLfe || maxWeight <= 0)
                    {
                        continue;
                    }

                    var intensity = weights[ch] / maxWeight;
                    if (intensity < 0.18f)
                    {
                        continue;
                    }

                    blips.Add(new ChannelBlip(
                        _layout[ch].Label,
                        _stereo ? (ch == 0 ? -90 : 90) : _layout[ch].AzimuthDegrees,
                        _stereo ? 0 : _layout[ch].ElevationDegrees,
                        intensity));
                }

                if (blips.Count > 1)
                {
                    blips.Sort((a, b) => b.Intensity.CompareTo(a.Intensity));
                    if (blips.Count > 4)
                    {
                        blips.RemoveRange(4, blips.Count - 4);
                    }
                }
            }
        }

        var now = DateTime.UtcNow;
        var held = false;
        if (!currentDirection && now < _holdUntil && _holdConfidence > 0.1)
        {
            held = true;
            currentDirection = true;
            az = _holdAz;
            el = _holdEl;
            var remaining = (_holdUntil - now).TotalMilliseconds / Math.Max(1, HoldMs);
            confidence = _holdConfidence * Math.Clamp(remaining, 0, 1);
        }

        var silent = overallDb < -70;
        string directionText;
        if (!currentDirection)
        {
            directionText = silent ? "静音" : "方向不明确";
        }
        else
        {
            directionText = FormatDirection(az, el, held);
        }

        var frame = new RadarFrame
        {
            Timestamp = now,
            ChannelDb = channelDb,
            ChannelLevels = channelLevels,
            OverallDb = overallDb,
            AzimuthDegrees = az,
            ElevationDegrees = el,
            Confidence = confidence,
            HasDirection = currentDirection,
            IsHeld = held,
            IsSilent = silent,
            Transient = (float)transient,
            DirectionText = directionText,
            Blips = blips,
        };

        Volatile.Write(ref _latest, frame);
    }

    private string FormatDirection(double az, double el, bool held)
    {
        if (_stereo)
        {
            var side = az < -10 ? "左" : az > 10 ? "右" : "中间";
            return (held ? "保持 " : "") + side + "（立体声：前后未知）";
        }

        return (held ? "保持 " : "") + ChannelLayout.DirectionText(az, el);
    }

    private static float ToDb(double amplitude)
    {
        if (amplitude <= 1e-9)
        {
            return -90;
        }

        return (float)Math.Clamp(20 * Math.Log10(amplitude), -90, 0);
    }

    private static double LerpAngle(double from, double to, double t)
    {
        var delta = ChannelLayout.Normalize(to - from);
        return ChannelLayout.Normalize(from + delta * t);
    }

    private static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }
}
