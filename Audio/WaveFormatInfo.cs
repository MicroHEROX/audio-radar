using System.Runtime.InteropServices;

namespace AudioRadar.Audio;

public sealed class WaveFormatInfo
{
    private static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00AA00389B71");
    private static readonly Guid SubtypeFloat = new("00000003-0000-0010-8000-00AA00389B71");

    public ushort FormatTag { get; init; }
    public ushort Channels { get; init; }
    public uint SampleRate { get; init; }
    public ushort BlockAlign { get; init; }
    public ushort BitsPerSample { get; init; }
    public ushort ValidBitsPerSample { get; init; }
    public uint ChannelMask { get; init; }
    public Guid SubFormat { get; init; }
    public bool IsExtensible { get; init; }
    public int BytesPerSample { get; init; }
    public bool IsFloat { get; init; }
    public bool IsPcm { get; init; }

    public string LayoutName => ChannelLayout.Describe(ChannelMask, Channels);
    public IReadOnlyList<SpeakerChannel> ChannelsLayout => ChannelLayout.FromMask(ChannelMask, Channels);

    public string FormatName
    {
        get
        {
            if (IsFloat)
            {
                return $"IEEE float {BitsPerSample}-bit";
            }

            if (IsPcm)
            {
                return ValidBitsPerSample != 0 && ValidBitsPerSample != BitsPerSample
                    ? $"PCM {ValidBitsPerSample}-in-{BitsPerSample}-bit"
                    : $"PCM {BitsPerSample}-bit";
            }

            return $"Encoded/unsupported (tag 0x{FormatTag:X4})";
        }
    }

    public string Summary =>
        $"{Channels}ch · {SampleRate} Hz · {FormatName} · mask 0x{ChannelMask:X} · {LayoutName}";

    public static WaveFormatInfo FromPointer(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(pointer));
        }

        var formatTag = (ushort)Marshal.ReadInt16(pointer, 0);
        var channels = (ushort)Marshal.ReadInt16(pointer, 2);
        var sampleRate = (uint)Marshal.ReadInt32(pointer, 4);
        var blockAlign = (ushort)Marshal.ReadInt16(pointer, 12);
        var bitsPerSample = (ushort)Marshal.ReadInt16(pointer, 14);
        var cbSize = (ushort)Marshal.ReadInt16(pointer, 16);

        var isExtensible = formatTag == 0xFFFE && cbSize >= 22;
        var validBits = bitsPerSample;
        uint channelMask = 0;
        var subFormat = Guid.Empty;

        if (isExtensible)
        {
            validBits = (ushort)Marshal.ReadInt16(pointer, 18);
            channelMask = (uint)Marshal.ReadInt32(pointer, 20);
            var subFormatBytes = new byte[16];
            Marshal.Copy(pointer + 24, subFormatBytes, 0, 16);
            subFormat = new Guid(subFormatBytes);
        }

        var isPcm = formatTag == 1 || (isExtensible && subFormat == SubtypePcm);
        var isFloat = formatTag == 3 || (isExtensible && subFormat == SubtypeFloat);
        var bytesPerSample = channels > 0 ? blockAlign / channels : 0;
        if (bytesPerSample == 0)
        {
            bytesPerSample = Math.Max(1, bitsPerSample / 8);
        }

        return new WaveFormatInfo
        {
            FormatTag = formatTag,
            Channels = channels,
            SampleRate = sampleRate,
            BlockAlign = blockAlign,
            BitsPerSample = bitsPerSample,
            ValidBitsPerSample = validBits,
            ChannelMask = channelMask,
            SubFormat = subFormat,
            IsExtensible = isExtensible,
            IsPcm = isPcm,
            IsFloat = isFloat,
            BytesPerSample = bytesPerSample,
        };
    }

    public static WaveFormatInfo FromPropertyBlob(byte[] blob)
    {
        // PKEY_AudioEngine_DeviceFormat blobs on this platform have an 8-byte
        // wrapper before WAVEFORMATEX. GetMixFormat is preferred; this is only
        // used for endpoint listing/diagnostics.
        var offset = 0;
        if (blob.Length >= 26 && BitConverter.ToUInt16(blob, 0) != 0xFFFE &&
            BitConverter.ToUInt16(blob, 0) != 1 && BitConverter.ToUInt16(blob, 0) != 3 &&
            BitConverter.ToUInt16(blob, 8) == 0xFFFE)
        {
            offset = 8;
        }

        if (blob.Length - offset < 18)
        {
            throw new ArgumentException("Blob is too small to be WAVEFORMATEX.", nameof(blob));
        }

        var handle = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            return FromPointer(handle.AddrOfPinnedObject() + offset);
        }
        finally
        {
            handle.Free();
        }
    }
}
