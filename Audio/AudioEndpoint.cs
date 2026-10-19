using System.Runtime.InteropServices;
using AudioRadar.Interop;

namespace AudioRadar.Audio;

internal sealed class AudioEndpoint
{
    private static readonly PROPERTYKEY PkeyDeviceFriendlyName =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    private static readonly PROPERTYKEY PkeyDeviceDescription =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 2);

    private static readonly PROPERTYKEY PkeyEndpointFormFactor =
        new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 0);

    private static readonly PROPERTYKEY PkeyPhysicalSpeakers =
        new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 3);

    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public required string Description { get; init; }
    public required string FormFactor { get; init; }
    public required bool IsDefault { get; init; }
    public WaveFormatInfo? MixFormat { get; init; }
    public uint PhysicalSpeakerMask { get; init; }

    public string DisplayName
    {
        get
        {
            var suffix = MixFormat is null
                ? ""
                : $" [{MixFormat.Channels}ch {MixFormat.SampleRate / 1000.0:0.#}k {MixFormat.LayoutName}]";
            return FriendlyName + suffix;
        }
    }

    public string Detail
    {
        get
        {
            var format = MixFormat?.Summary ?? "mix format unavailable";
            return $"{Description} · {FormFactor} · {format} · physical mask 0x{PhysicalSpeakerMask:X}";
        }
    }

    public override string ToString() => DisplayName;

    public static IReadOnlyList<AudioEndpoint> EnumerateActive(out string? defaultId)
    {
        defaultId = null;
        var result = new List<AudioEndpoint>();

        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        IMMDeviceCollection? collection = null;
        try
        {
            var defaultHr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out var defaultDevice);
            if (defaultHr >= 0 && defaultDevice is not null)
            {
                try
                {
                    if (defaultDevice.GetId(out var id) >= 0)
                    {
                        defaultId = id;
                    }
                }
                finally
                {
                    ComUtil.Release(defaultDevice);
                }
            }

            var hr = enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out collection);
            ComUtil.ThrowIfFailed(hr, "EnumAudioEndpoints");

            hr = collection.GetCount(out var count);
            ComUtil.ThrowIfFailed(hr, "IMMDeviceCollection.GetCount");

            for (var i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    hr = collection.Item(i, out device);
                    if (hr < 0 || device is null)
                    {
                        continue;
                    }

                    var endpoint = FromDevice(device, defaultId);
                    if (endpoint is not null)
                    {
                        result.Add(endpoint);
                    }
                }
                finally
                {
                    ComUtil.Release(device);
                }
            }
        }
        finally
        {
            ComUtil.Release(collection);
            ComUtil.Release(enumerator);
        }

        return result
            .OrderByDescending(e => e.IsDefault)
            .ThenBy(e => e.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static AudioEndpoint? GetDefault()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        IMMDevice? device = null;
        try
        {
            var hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out device);
            if (hr < 0 || device is null)
            {
                return null;
            }

            return FromDevice(device, null);
        }
        finally
        {
            ComUtil.Release(device);
            ComUtil.Release(enumerator);
        }
    }

    public static string? GetDefaultId()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        IMMDevice? device = null;
        try
        {
            var hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out device);
            if (hr < 0 || device is null)
            {
                return null;
            }

            return device.GetId(out var id) >= 0 ? id : null;
        }
        finally
        {
            ComUtil.Release(device);
            ComUtil.Release(enumerator);
        }
    }

    public static AudioEndpoint? GetById(string id)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        IMMDevice? device = null;
        try
        {
            var hr = enumerator.GetDevice(id, out device);
            if (hr < 0 || device is null)
            {
                return null;
            }

            var defaultId = GetDefaultId();
            return FromDevice(device, defaultId);
        }
        finally
        {
            ComUtil.Release(device);
            ComUtil.Release(enumerator);
        }
    }

    private static AudioEndpoint? FromDevice(IMMDevice device, string? defaultId)
    {
        var hr = device.GetId(out var id);
        if (hr < 0 || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var friendlyName = id;
        var description = "";
        var formFactor = "Unknown";
        uint physicalSpeakers = 0;

        IPropertyStore? store = null;
        try
        {
            hr = device.OpenPropertyStore(0 /* STGM_READ */, out store);
            if (hr >= 0 && store is not null)
            {
                friendlyName = ReadString(store, PkeyDeviceFriendlyName) ?? friendlyName;
                description = ReadString(store, PkeyDeviceDescription) ?? description;
                var formFactorValue = ReadUInt(store, PkeyEndpointFormFactor);
                if (formFactorValue is not null)
                {
                    formFactor = FormFactorName(formFactorValue.Value);
                }

                physicalSpeakers = ReadUInt(store, PkeyPhysicalSpeakers) ?? 0;
            }
        }
        finally
        {
            ComUtil.Release(store);
        }

        WaveFormatInfo? mixFormat = null;
        IAudioClient? audioClient = null;
        try
        {
            var iid = ComClsIds.AudioClient;
            hr = device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var activated);
            if (hr >= 0 && activated is IAudioClient client)
            {
                audioClient = client;
                hr = audioClient.GetMixFormat(out var formatPointer);
                if (hr >= 0 && formatPointer != IntPtr.Zero)
                {
                    try
                    {
                        mixFormat = WaveFormatInfo.FromPointer(formatPointer);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(formatPointer);
                    }
                }
            }
        }
        catch
        {
            // Some endpoints cannot be activated while another process owns them
            // exclusively. Listing should still succeed without the format.
        }
        finally
        {
            ComUtil.Release(audioClient);
        }

        return new AudioEndpoint
        {
            Id = id,
            FriendlyName = friendlyName,
            Description = description,
            FormFactor = formFactor,
            IsDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase),
            MixFormat = mixFormat,
            PhysicalSpeakerMask = physicalSpeakers,
        };
    }

    private static string? ReadString(IPropertyStore store, PROPERTYKEY key)
    {
        var pv = default(PROPVARIANT);
        try
        {
            if (store.GetValue(ref key, out pv) < 0)
            {
                return null;
            }

            return pv.vt switch
            {
                VarType.LPWStr => Marshal.PtrToStringUni(pv.pointerValue),
                VarType.Bstr => Marshal.PtrToStringBSTR(pv.pointerValue),
                _ => null,
            };
        }
        finally
        {
            if (pv.vt != VarType.Empty)
            {
                Ole32.PropVariantClear(ref pv);
            }
        }
    }

    private static uint? ReadUInt(IPropertyStore store, PROPERTYKEY key)
    {
        var pv = default(PROPVARIANT);
        try
        {
            if (store.GetValue(ref key, out pv) < 0)
            {
                return null;
            }

            return pv.vt switch
            {
                VarType.UI4 => pv.uintValue,
                VarType.I4 => unchecked((uint)pv.intValue),
                _ => null,
            };
        }
        finally
        {
            if (pv.vt != VarType.Empty)
            {
                Ole32.PropVariantClear(ref pv);
            }
        }
    }

    private static string FormFactorName(uint value) => value switch
    {
        0 => "RemoteNetworkDevice",
        1 => "Speakers",
        2 => "LineLevel",
        3 => "Headphones",
        4 => "Microphone",
        5 => "Headset",
        6 => "Handset",
        7 => "DigitalPassthrough",
        8 => "SPDIF",
        9 => "HDMI / DigitalDisplay",
        10 => "Unknown",
        _ => $"FormFactor {value}",
    };
}
