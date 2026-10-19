using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AudioRadar.Audio;
using AudioRadar.Dsp;

namespace AudioRadar.UI;

public sealed class RadarControl : Control
{
    private static readonly Color Background = Color.FromArgb(7, 11, 16);
    private static readonly Color Grid = Color.FromArgb(38, 55, 68);
    private static readonly Color GridStrong = Color.FromArgb(60, 84, 100);
    private static readonly Color Accent = Color.FromArgb(56, 232, 198);
    private static readonly Color HeightAccent = Color.FromArgb(143, 184, 255);
    private static readonly Color HeldAccent = Color.FromArgb(255, 184, 72);
    private static readonly Color TextColor = Color.FromArgb(220, 231, 242);
    private static readonly Color Muted = Color.FromArgb(115, 135, 152);

    private readonly Font _fontSmall = new("Microsoft YaHei UI", 8.5f);
    private readonly Font _fontLarge = new("Microsoft YaHei UI", 13f, FontStyle.Bold);
    private readonly Font _fontTiny = new("Microsoft YaHei UI", 7.2f);

    private IReadOnlyList<SpeakerChannel> _layout = Array.Empty<SpeakerChannel>();
    private RadarFrame? _frame;
    private bool _showChannelBlips = true;

    public RadarControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Background;
        ForeColor = TextColor;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
    }

    public bool ShowChannelBlips
    {
        get => _showChannelBlips;
        set
        {
            _showChannelBlips = value;
            Invalidate();
        }
    }

    public void SetLayout(IReadOnlyList<SpeakerChannel> layout)
    {
        _layout = layout;
        Invalidate();
    }

    public void SetFrame(RadarFrame? frame)
    {
        _frame = frame;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Background);

        var w = Width;
        var h = Height;
        if (w < 120 || h < 120)
        {
            return;
        }

        var size = Math.Min(w - 60, h - 90);
        var radius = Math.Max(60, size / 2);
        var cx = w / 2;
        var cy = h / 2 + 4;

        DrawGrid(g, cx, cy, radius);
        DrawChannelAnchors(g, cx, cy, radius);
        DrawFrame(g, cx, cy, radius);
        DrawCaption(g, cx, cy, radius);
    }

    private void DrawGrid(Graphics g, int cx, int cy, int radius)
    {
        using var gridPen = new Pen(Grid, 1);
        using var strongPen = new Pen(GridStrong, 1);

        for (var i = 1; i <= 3; i++)
        {
            var r = radius * i / 3;
            g.DrawEllipse(gridPen, cx - r, cy - r, r * 2, r * 2);
        }

        g.DrawEllipse(strongPen, cx - radius, cy - radius, radius * 2, radius * 2);
        g.DrawLine(strongPen, cx - radius, cy, cx + radius, cy);
        g.DrawLine(strongPen, cx, cy - radius, cx, cy + radius);

        using var labelBrush = new SolidBrush(Muted);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("前", _fontSmall, labelBrush, cx, cy - radius + 18, format);
        g.DrawString("后", _fontSmall, labelBrush, cx, cy + radius - 18, format);
        g.DrawString("左", _fontSmall, labelBrush, cx - radius + 18, cy, format);
        g.DrawString("右", _fontSmall, labelBrush, cx + radius - 18, cy, format);
    }

    private void DrawChannelAnchors(Graphics g, int cx, int cy, int radius)
    {
        if (_layout.Count == 0)
        {
            return;
        }

        using var normalBrush = new SolidBrush(Color.FromArgb(90, 115, 135, 152));
        using var heightBrush = new SolidBrush(Color.FromArgb(120, HeightAccent));

        foreach (var channel in _layout)
        {
            if (channel.IsLfe)
            {
                continue;
            }

            var (x, y) = Project(channel.AzimuthDegrees, channel.ElevationDegrees, radius);
            var brush = channel.ElevationDegrees > 15 ? heightBrush : normalBrush;
            var dot = channel.ElevationDegrees > 15 ? 2.5f : 1.8f;
            g.FillEllipse(brush, cx + (float)x - dot, cy + (float)y - dot, dot * 2, dot * 2);
        }
    }

    private void DrawFrame(Graphics g, int cx, int cy, int radius)
    {
        var frame = _frame;
        if (frame is null)
        {
            using var brush = new SolidBrush(Muted);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("等待音频…", _fontLarge, brush, cx, cy, format);
            return;
        }

        if (!frame.HasDirection)
        {
            using var brush = new SolidBrush(Muted);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(frame.DirectionText, _fontLarge, brush, cx, cy, format);
        }
        else
        {
            DrawDirection(g, frame, cx, cy, radius);
        }

        if (_showChannelBlips && frame.Blips.Count > 0)
        {
            DrawBlips(g, frame, cx, cy, radius);
        }
    }

    private void DrawDirection(Graphics g, RadarFrame frame, int cx, int cy, int radius)
    {
        var color = frame.IsHeld ? HeldAccent : Accent;
        var level = Math.Clamp((frame.OverallDb + 60) / 60, 0.15, 1.0);
        var alpha = (int)(90 + 150 * level * (0.45 + 0.55 * frame.Confidence));
        alpha = Math.Clamp(alpha, 35, 245);

        var (x, y) = Project(frame.AzimuthDegrees, frame.ElevationDegrees, radius * 0.9);
        var px = cx + (float)x;
        var py = cy + (float)y;

        using var glowPen = new Pen(Color.FromArgb(alpha / 5, color), 14) { EndCap = LineCap.Round };
        using var linePen = new Pen(Color.FromArgb(alpha / 2, color), 4) { EndCap = LineCap.Round };
        using var corePen = new Pen(Color.FromArgb(alpha, color), 1.8f) { EndCap = LineCap.Round };
        g.DrawLine(glowPen, cx, cy, px, py);
        g.DrawLine(linePen, cx, cy, px, py);
        g.DrawLine(corePen, cx, cy, px, py);

        var dot = 5f + 7f * (float)frame.Confidence;
        using var glowBrush = new SolidBrush(Color.FromArgb(alpha / 3, color));
        using var dotBrush = new SolidBrush(Color.FromArgb(alpha, color));
        g.FillEllipse(glowBrush, px - dot * 2.1f, py - dot * 2.1f, dot * 4.2f, dot * 4.2f);
        g.FillEllipse(dotBrush, px - dot, py - dot, dot * 2, dot * 2);

        if (frame.ElevationDegrees >= 20)
        {
            using var upBrush = new SolidBrush(Color.FromArgb(Math.Min(255, alpha + 20), HeightAccent));
            using var format = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString($"↑ +{frame.ElevationDegrees:0}°", _fontTiny, upBrush, px, py - dot - 20, format);
        }

        using var centerBrush = new SolidBrush(Color.FromArgb(180, TextColor));
        g.FillEllipse(centerBrush, cx - 3, cy - 3, 6, 6);
    }

    private void DrawBlips(Graphics g, RadarFrame frame, int cx, int cy, int radius)
    {
        foreach (var blip in frame.Blips)
        {
            var (x, y) = Project(blip.AzimuthDegrees, blip.ElevationDegrees, radius * 0.92);
            var px = cx + (float)x;
            var py = cy + (float)y;
            var a = (int)Math.Clamp(50 + 130 * blip.Intensity, 40, 200);
            var color = blip.ElevationDegrees > 15 ? HeightAccent : Accent;
            using var brush = new SolidBrush(Color.FromArgb(a, color));
            var size = 2.5f + 4f * blip.Intensity;
            g.FillEllipse(brush, px - size, py - size, size * 2, size * 2);
        }
    }

    private void DrawCaption(Graphics g, int cx, int cy, int radius)
    {
        var frame = _frame;
        var text = "Windows 端点环回 · 无进程抓取";
        if (frame is not null && frame.HasDirection)
        {
            text = $"{frame.DirectionText}   ·   置信度 {frame.Confidence:P0}   ·   {frame.OverallDb:0} dBFS";
        }
        else if (frame is not null)
        {
            text = $"{frame.DirectionText}   ·   {frame.OverallDb:0} dBFS";
        }

        using var brush = new SolidBrush(frame?.IsHeld == true ? HeldAccent : Muted);
        using var format = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(text, _fontSmall, brush, cx, cy + radius + 16, format);
    }

    private static (double X, double Y) Project(double azimuthDegrees, double elevationDegrees, double radius)
    {
        var az = azimuthDegrees * Math.PI / 180.0;
        var elevationFactor = 1.0 - 0.45 * Math.Clamp(elevationDegrees / 90.0, 0, 1);
        return (Math.Sin(az) * radius * elevationFactor, -Math.Cos(az) * radius * elevationFactor);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fontSmall.Dispose();
            _fontLarge.Dispose();
            _fontTiny.Dispose();
        }

        base.Dispose(disposing);
    }
}
