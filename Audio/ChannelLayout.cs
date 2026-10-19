namespace AudioRadar.Audio;

[Flags]
public enum Speaker : uint
{
    None = 0,
    FrontLeft = 0x00000001,
    FrontRight = 0x00000002,
    FrontCenter = 0x00000004,
    LowFrequency = 0x00000008,
    BackLeft = 0x00000010,
    BackRight = 0x00000020,
    FrontLeftOfCenter = 0x00000040,
    FrontRightOfCenter = 0x00000080,
    BackCenter = 0x00000100,
    SideLeft = 0x00000200,
    SideRight = 0x00000400,
    TopCenter = 0x00000800,
    TopFrontLeft = 0x00001000,
    TopFrontCenter = 0x00002000,
    TopFrontRight = 0x00004000,
    TopBackLeft = 0x00008000,
    TopBackCenter = 0x00010000,
    TopBackRight = 0x00020000,
}

/// <summary>
/// A physical/logical speaker channel. Azimuth is degrees clockwise from front
/// (negative = left, positive = right). Elevation is degrees above the horizon.
/// </summary>
public sealed record SpeakerChannel(
    Speaker Speaker,
    string Label,
    double AzimuthDegrees,
    double ElevationDegrees,
    bool IsLfe);

public static class ChannelLayout
{
    private static readonly Dictionary<Speaker, (string Label, double Az, double El, bool Lfe)> Definitions = new()
    {
        [Speaker.FrontLeft] = ("FL", -30, 0, false),
        [Speaker.FrontRight] = ("FR", 30, 0, false),
        [Speaker.FrontCenter] = ("FC", 0, 0, false),
        [Speaker.LowFrequency] = ("LFE", 0, -90, true),
        [Speaker.BackLeft] = ("BL", -135, 0, false),
        [Speaker.BackRight] = ("BR", 135, 0, false),
        [Speaker.FrontLeftOfCenter] = ("FLC", -15, 0, false),
        [Speaker.FrontRightOfCenter] = ("FRC", 15, 0, false),
        [Speaker.BackCenter] = ("BC", 180, 0, false),
        [Speaker.SideLeft] = ("SL", -90, 0, false),
        [Speaker.SideRight] = ("SR", 90, 0, false),
        [Speaker.TopCenter] = ("TC", 0, 90, false),
        [Speaker.TopFrontLeft] = ("TFL", -30, 45, false),
        [Speaker.TopFrontCenter] = ("TFC", 0, 45, false),
        [Speaker.TopFrontRight] = ("TFR", 30, 45, false),
        [Speaker.TopBackLeft] = ("TBL", -135, 45, false),
        [Speaker.TopBackCenter] = ("TBC", 180, 45, false),
        [Speaker.TopBackRight] = ("TBR", 135, 45, false),
    };

    private static readonly Speaker[] AscendingSpeakers =
    [
        Speaker.FrontLeft,
        Speaker.FrontRight,
        Speaker.FrontCenter,
        Speaker.LowFrequency,
        Speaker.BackLeft,
        Speaker.BackRight,
        Speaker.FrontLeftOfCenter,
        Speaker.FrontRightOfCenter,
        Speaker.BackCenter,
        Speaker.SideLeft,
        Speaker.SideRight,
        Speaker.TopCenter,
        Speaker.TopFrontLeft,
        Speaker.TopFrontCenter,
        Speaker.TopFrontRight,
        Speaker.TopBackLeft,
        Speaker.TopBackCenter,
        Speaker.TopBackRight,
    ];

    public static IReadOnlyList<SpeakerChannel> FromMask(uint channelMask, int channelCount)
    {
        if (channelCount <= 0)
        {
            return Array.Empty<SpeakerChannel>();
        }

        var result = new List<SpeakerChannel>(channelCount);
        if (channelMask != 0)
        {
            foreach (var speaker in AscendingSpeakers)
            {
                if ((channelMask & (uint)speaker) == 0)
                {
                    continue;
                }

                var def = Definitions[speaker];
                result.Add(new SpeakerChannel(speaker, def.Label, def.Az, def.El, def.Lfe));
            }
        }

        if (result.Count == channelCount)
        {
            return result;
        }

        return Fallback(channelCount);
    }

    private static IReadOnlyList<SpeakerChannel> Fallback(int channelCount)
    {
        var order = channelCount switch
        {
            1 => new[] { Speaker.FrontCenter },
            2 => new[] { Speaker.FrontLeft, Speaker.FrontRight },
            3 => new[] { Speaker.FrontLeft, Speaker.FrontRight, Speaker.FrontCenter },
            4 => new[] { Speaker.FrontLeft, Speaker.FrontRight, Speaker.BackLeft, Speaker.BackRight },
            5 => new[] { Speaker.FrontLeft, Speaker.FrontRight, Speaker.FrontCenter, Speaker.BackLeft, Speaker.BackRight },
            6 => new[] { Speaker.FrontLeft, Speaker.FrontRight, Speaker.FrontCenter, Speaker.LowFrequency, Speaker.BackLeft, Speaker.BackRight },
            8 => new[] { Speaker.FrontLeft, Speaker.FrontRight, Speaker.FrontCenter, Speaker.LowFrequency, Speaker.BackLeft, Speaker.BackRight, Speaker.SideLeft, Speaker.SideRight },
            10 => new[] { Speaker.FrontLeft, Speaker.FrontRight, Speaker.FrontCenter, Speaker.LowFrequency, Speaker.BackLeft, Speaker.BackRight, Speaker.SideLeft, Speaker.SideRight, Speaker.TopFrontLeft, Speaker.TopFrontRight },
            12 => new[] { Speaker.FrontLeft, Speaker.FrontRight, Speaker.FrontCenter, Speaker.LowFrequency, Speaker.BackLeft, Speaker.BackRight, Speaker.SideLeft, Speaker.SideRight, Speaker.TopFrontLeft, Speaker.TopFrontRight, Speaker.TopBackLeft, Speaker.TopBackRight },
            _ => new[]
            {
                Speaker.FrontLeft, Speaker.FrontRight, Speaker.FrontCenter, Speaker.LowFrequency,
                Speaker.BackLeft, Speaker.BackRight, Speaker.SideLeft, Speaker.SideRight,
                Speaker.TopFrontLeft, Speaker.TopFrontRight, Speaker.TopBackLeft, Speaker.TopBackRight,
            },
        };

        var result = new List<SpeakerChannel>(channelCount);
        for (var i = 0; i < channelCount; i++)
        {
            if (i < order.Length)
            {
                var speaker = order[i];
                var def = Definitions[speaker];
                result.Add(new SpeakerChannel(speaker, def.Label, def.Az, def.El, def.Lfe));
            }
            else
            {
                result.Add(new SpeakerChannel(Speaker.None, $"CH{i + 1}", 0, 0, false));
            }
        }

        return result;
    }

    public static string Describe(uint mask, int channelCount)
    {
        var exact = mask switch
        {
            0x3 => "Stereo 2.0",
            0x3F => "5.1",
            0x63F => "7.1",
            0x503F => "5.1.2 (PCM)",
            0x563F => "7.1.2 (PCM)",
            0x2D03F => "5.1.4 (PCM)",
            0x2D63F => "7.1.4 (PCM)",
            _ => null,
        };

        if (exact is not null)
        {
            return exact;
        }

        var channels = FromMask(mask, channelCount);
        return $"Custom {channelCount}ch [{string.Join(" ", channels.Select(c => c.Label))}]";
    }

    public static bool HasHeightChannels(uint mask)
    {
        const uint heightMask = (uint)(Speaker.TopCenter | Speaker.TopFrontLeft | Speaker.TopFrontCenter |
                                       Speaker.TopFrontRight | Speaker.TopBackLeft | Speaker.TopBackCenter |
                                       Speaker.TopBackRight);
        return (mask & heightMask) != 0;
    }

    public static string DirectionName(double azimuthDegrees)
    {
        var az = Normalize(azimuthDegrees);
        var abs = Math.Abs(az);
        string horizontal;
        if (abs <= 22.5)
        {
            horizontal = "前";
        }
        else if (abs <= 67.5)
        {
            horizontal = az < 0 ? "左前" : "右前";
        }
        else if (abs <= 112.5)
        {
            horizontal = az < 0 ? "左" : "右";
        }
        else if (abs <= 157.5)
        {
            horizontal = az < 0 ? "左后" : "右后";
        }
        else
        {
            horizontal = "后";
        }

        return horizontal;
    }

    public static string DirectionText(double azimuthDegrees, double elevationDegrees)
    {
        var horizontal = DirectionName(azimuthDegrees);
        var abs = Math.Abs(Normalize(azimuthDegrees));
        var text = $"{horizontal} {abs:0}°";
        if (elevationDegrees >= 20)
        {
            text += $" / 仰角 +{elevationDegrees:0}°";
        }
        else if (elevationDegrees <= -20)
        {
            text += $" / 俯角 {elevationDegrees:0}°";
        }

        return text;
    }

    public static double Normalize(double degrees)
    {
        while (degrees > 180)
        {
            degrees -= 360;
        }

        while (degrees < -180)
        {
            degrees += 360;
        }

        return degrees;
    }

    public static (double X, double Y, double Z) UnitVector(double azimuthDegrees, double elevationDegrees)
    {
        var az = azimuthDegrees * Math.PI / 180.0;
        var el = elevationDegrees * Math.PI / 180.0;
        var cosEl = Math.Cos(el);
        return (Math.Sin(az) * cosEl, Math.Cos(az) * cosEl, Math.Sin(el));
    }
}
