using System.Runtime.InteropServices;
using AudioRadar.Interop;

namespace AudioRadar.Audio;

internal sealed class LoopbackDeviceInvalidatedException : Exception
{
    public LoopbackDeviceInvalidatedException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Shared-mode WASAPI endpoint loopback. This captures the final audio stream
/// that Windows sends to the selected render endpoint. It does not open, inject
/// into, or read memory from any game process.
/// </summary>
internal sealed class WasapiLoopbackCapture : IDisposable
{
    private readonly object _lifecycle = new();
    private IMMDevice? _device;
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private Thread? _thread;
    private volatile bool _running;
    private Action<float[], int>? _onSamples;
    private float[] _sampleBuffer = Array.Empty<float>();

    public WaveFormatInfo? Format { get; private set; }
    public string? EndpointId { get; private set; }

    public event Action<Exception>? Error;

    public WaveFormatInfo Start(string? endpointId, Action<float[], int> onSamples)
    {
        ArgumentNullException.ThrowIfNull(onSamples);

        lock (_lifecycle)
        {
            StopCore();

            _device = OpenDevice(endpointId);
            try
            {
                _device.GetId(out var actualId);
                EndpointId = actualId;

                var iid = ComClsIds.AudioClient;
                var hr = _device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var activated);
                ComUtil.ThrowIfFailed(hr, "IMMDevice.Activate(IAudioClient)");
                _audioClient = activated as IAudioClient
                    ?? throw new InvalidOperationException("Endpoint did not return IAudioClient.");

                hr = _audioClient.GetMixFormat(out var formatPointer);
                ComUtil.ThrowIfFailed(hr, "IAudioClient.GetMixFormat");
                try
                {
                    Format = WaveFormatInfo.FromPointer(formatPointer);

                    if (Format.Channels == 0)
                    {
                        throw new NotSupportedException("The endpoint reports zero channels.");
                    }

                    if (!Format.IsPcm && !Format.IsFloat)
                    {
                        throw new NotSupportedException(
                            $"The endpoint mix format is not PCM/float: {Format.FormatName}. " +
                            "Loopback analysis needs a PCM bed. For Dolby/DTS bitstream modes, " +
                            "switch the endpoint to PCM 5.1/7.1 or use a PCM bed output.");
                    }

                    if (Format.BytesPerSample is not (1 or 2 or 3 or 4))
                    {
                        throw new NotSupportedException(
                            $"Unsupported sample container: {Format.BytesPerSample} bytes/sample.");
                    }

                    const long bufferDuration = 200_000; // 20 ms in 100-ns units.
                    hr = _audioClient.Initialize(
                        AudioClientShareMode.Shared,
                        AudioClientStreamFlags.Loopback,
                        bufferDuration,
                        0,
                        formatPointer,
                        IntPtr.Zero);
                    ComUtil.ThrowIfFailed(hr, "IAudioClient.Initialize(loopback)");
                }
                finally
                {
                    Marshal.FreeCoTaskMem(formatPointer);
                }

                var captureIid = ComClsIds.AudioCaptureClient;
                hr = _audioClient.GetService(ref captureIid, out var service);
                ComUtil.ThrowIfFailed(hr, "IAudioClient.GetService(IAudioCaptureClient)");
                _captureClient = service as IAudioCaptureClient
                    ?? throw new InvalidOperationException("Endpoint did not return IAudioCaptureClient.");

                _onSamples = onSamples;
                _running = true;
                _thread = new Thread(CaptureLoop)
                {
                    IsBackground = true,
                    Name = "WASAPI endpoint loopback",
                    Priority = ThreadPriority.AboveNormal,
                };
                _thread.Start();

                hr = _audioClient.Start();
                if (hr < 0)
                {
                    _running = false;
                    throw new COMException("IAudioClient.Start failed.", hr);
                }

                return Format;
            }
            catch
            {
                StopCore();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_lifecycle)
        {
            StopCore();
        }
    }

    private void StopCore()
    {
        _running = false;
        var thread = _thread;
        _thread = null;

        if (thread is not null && thread != Thread.CurrentThread)
        {
            try
            {
                thread.Join(2000);
            }
            catch
            {
                // Ignore shutdown races.
            }
        }

        try
        {
            _audioClient?.Stop();
        }
        catch
        {
            // Ignore.
        }

        ComUtil.Release(_captureClient);
        ComUtil.Release(_audioClient);
        ComUtil.Release(_device);
        _captureClient = null;
        _audioClient = null;
        _device = null;
        _onSamples = null;
        _sampleBuffer = Array.Empty<float>();
    }

    private void CaptureLoop()
    {
        try
        {
            var capture = _captureClient ?? throw new InvalidOperationException("Capture client is null.");
            while (_running)
            {
                var hr = capture.GetNextPacketSize(out var packetFrames);
                if (hr < 0)
                {
                    ThrowLoopbackHr(hr, "IAudioCaptureClient.GetNextPacketSize");
                }

                if (packetFrames == 0)
                {
                    Thread.Sleep(4);
                    continue;
                }

                while (packetFrames > 0 && _running)
                {
                    hr = capture.GetBuffer(out var data, out var frames, out var flags,
                        out _, out _);
                    if (hr == HResult.AudclntSBufferEmpty)
                    {
                        break;
                    }

                    if (hr < 0)
                    {
                        ThrowLoopbackHr(hr, "IAudioCaptureClient.GetBuffer");
                    }

                    try
                    {
                        if (frames > 0)
                        {
                            var count = checked((int)frames) * (Format?.Channels ?? 1);
                            EnsureSampleBuffer(count);

                            if ((flags & AudioClientBufferFlags.Silent) != 0)
                            {
                                Array.Clear(_sampleBuffer, 0, count);
                            }
                            else
                            {
                                ConvertToFloat(data, (int)frames, _sampleBuffer);
                            }

                            _onSamples?.Invoke(_sampleBuffer, (int)frames);
                        }
                    }
                    finally
                    {
                        capture.ReleaseBuffer(frames);
                    }

                    hr = capture.GetNextPacketSize(out packetFrames);
                    if (hr < 0)
                    {
                        ThrowLoopbackHr(hr, "IAudioCaptureClient.GetNextPacketSize");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (_running)
            {
                _running = false;
                Error?.Invoke(ex);
            }
        }
    }

    private static void ThrowLoopbackHr(int hr, string operation)
    {
        if (hr == HResult.AudclntEDeviceInvalidated)
        {
            throw new LoopbackDeviceInvalidatedException(
                $"{operation}: the endpoint was invalidated (device removed or default changed).",
                Marshal.GetExceptionForHR(hr));
        }

        throw new COMException($"{operation} failed.", hr);
    }

    private void EnsureSampleBuffer(int count)
    {
        if (_sampleBuffer.Length < count)
        {
            _sampleBuffer = new float[Math.Max(count, 4096)];
        }
    }

    private unsafe void ConvertToFloat(IntPtr data, int frames, float[] destination)
    {
        var format = Format ?? throw new InvalidOperationException("Format is unavailable.");
        var channels = format.Channels;
        var stride = format.BlockAlign;
        var bytesPerSample = format.BytesPerSample;
        var sampleCount = frames * channels;

        if (format.IsFloat && bytesPerSample == 4)
        {
            Marshal.Copy(data, destination, 0, sampleCount);
            return;
        }

        var source = (byte*)data;
        switch (format.IsPcm ? bytesPerSample : -1)
        {
            case 1:
                for (var frame = 0; frame < frames; frame++)
                {
                    var frameBase = source + frame * stride;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        destination[frame * channels + ch] = (frameBase[ch] - 128) / 128f;
                    }
                }

                break;

            case 2:
                for (var frame = 0; frame < frames; frame++)
                {
                    var frameBase = source + frame * stride;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        var value = *(short*)(frameBase + ch * 2);
                        destination[frame * channels + ch] = value / 32768f;
                    }
                }

                break;

            case 3:
                for (var frame = 0; frame < frames; frame++)
                {
                    var frameBase = source + frame * stride;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        var p = frameBase + ch * 3;
                        var value = p[0] | (p[1] << 8) | (p[2] << 16);
                        if ((value & 0x800000) != 0)
                        {
                            value |= unchecked((int)0xFF000000);
                        }

                        destination[frame * channels + ch] = value / 8388608f;
                    }
                }

                break;

            case 4:
                for (var frame = 0; frame < frames; frame++)
                {
                    var frameBase = source + frame * stride;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        var value = *(int*)(frameBase + ch * 4);
                        destination[frame * channels + ch] = value / 2147483648f;
                    }
                }

                break;

            default:
                throw new NotSupportedException($"Unsupported PCM container: {bytesPerSample} bytes.");
        }
    }

    private static IMMDevice OpenDevice(string? endpointId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            int hr;
            IMMDevice? device;
            if (string.IsNullOrWhiteSpace(endpointId))
            {
                hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out device);
            }
            else
            {
                hr = enumerator.GetDevice(endpointId, out device);
            }

            ComUtil.ThrowIfFailed(hr, "Open render endpoint");
            return device ?? throw new InvalidOperationException("Render endpoint was not found.");
        }
        finally
        {
            ComUtil.Release(enumerator);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
