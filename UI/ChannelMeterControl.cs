using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AudioRadar.Audio;

namespace AudioRadar.UI;

public sealed class ChannelMeterControl : Control
{
    private static readonly Color Background = Color.FromArgb(9, 14, 20);
    private static readonly Color Grid = Color.FromArgb(35, 48, 60);
    private static readonly Color TextColor = Color.FromArgb(220, 231, 242);
    private static readonly Color Muted = Color.FromArgb(115, 135, 152);
    private static readonly Color Accent = Color.FromArgb(56, 232, 198);
    private static readonly Color HeightAccent = Color.FromArgb(143, 184, 255);
    private static readonly Color LfeColor = Color.FromArgb(120, 130, 145);

    private readonly Font _fontSmall = new("Microsoft YaHei UI", 8f);
    private readonly Font _fontTiny = new("Microsoft YaHei UI", 7f);
    private IReadOnlyList<SpeakerChannel> _layout = Array.Empty<SpeakerChannel>();
    private float[] _db = Array.Empty<float>();

    public ChannelMeterControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Background;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
    }

    public void SetLayout(IReadOnlyList<SpeakerChannel> layout)
    {
        _layout = layout;
        _db = new float[layout.Count];
        Array.Fill(_db, -90);
        Invalidate();
    }

    public void SetData(float[] db)
    {
        if (db.Length != _layout.Count)
        {
            return;
        }

        _db = db;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Background);

        if (_layout.Count == 0)
        {
            using var placeholder = new SolidBrush(Muted);
            g.DrawString("声道电平 — 启动后显示", _fontSmall, placeholder, 12, 10);
            return;
        }

        const int left = 12;
        const int right = 12;
        const int top = 24;
        const int bottom = 30;
        var width = Width - left - right;
        var height = Height - top - bottom;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        using var titleBrush = new SolidBrush(Muted);
        g.DrawString("声道电平 (dBFS) · 端点混音", _fontSmall, titleBrush, left, 6);

        using var gridPen = new Pen(Grid, 1);
        for (var db = -60; db <= 0; db += 20)
        {
            var y = top + height * (1 - (db + 90) / 90.0);
            g.DrawLine(gridPen, left, (float)y, left + width, (float)y);
            g.DrawString($"{db}", _fontTiny, titleBrush, left + width - 24, (float)y - 10);
        }

        var count = _layout.Count;
        var slot = (float)width / count;
        var barWidth = Math.Max(6f, Math.Min(34f, slot * 0.62f));

        for (var i = 0; i < count; i++)
        {
            var channel = _layout[i];
            var db = Math.Clamp(_db[i], -90, 0);
            var level = (db + 90) / 90.0;
            var x = left + slot * i + (slot - barWidth) / 2;
            var y = (float)(top + height * (1 - level));
            var h = Math.Max(0, top + height - y);

            using var backBrush = new SolidBrush(Color.FromArgb(20, 28, 36));
            g.FillRectangle(backBrush, x, top, barWidth, height);

            var color = channel.IsLfe ? LfeColor : channel.ElevationDegrees > 15 ? HeightAccent : Accent;
            using var fillBrush = new LinearGradientBrush(
                new RectangleF(x, y, barWidth, Math.Max(1, h)),
                Color.FromArgb(80, color),
                color,
                LinearGradientMode.Vertical);
            g.FillRectangle(fillBrush, x, y, barWidth, Math.Max(1, h));

            using var labelBrush = new SolidBrush(channel.IsLfe ? Muted : TextColor);
            using var format = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString(channel.Label, _fontTiny, labelBrush, x + barWidth / 2, top + height + 4, format);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fontSmall.Dispose();
            _fontTiny.Dispose();
        }

        base.Dispose(disposing);
    }
}
