using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using AudioRadar.Audio;
using AudioRadar.Dsp;

namespace AudioRadar.UI;

/// <summary>
/// A borderless, always-on-top, semi-transparent radar window.
///
/// This is deliberately a normal external window: it does not inject into the
/// game, does not read game memory, and can be made click-through so it does not
/// steal mouse input while playing.
/// </summary>
public sealed class RadarOverlayForm : Form
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int WmNcHitTest = 0x0084;
    private const int HtCaption = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint ZbidSystemTools = 16;
    private static readonly IntPtr HwndTopMost = new(-1);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    private delegate bool SetWindowBandDelegate(IntPtr hWnd, IntPtr hWndInsertAfter, uint dwBand);

    private readonly RadarControl _radar = new();
    private readonly System.Windows.Forms.Timer _topMostTimer = new() { Interval = 33 };
    private bool _clickThrough = true;
    private bool _positioned;
    private bool _forceTopMost = true;
    private bool _bandAttempted;
    private bool _bandApplied;
    private int _bandError;

    public RadarOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(56, 232, 198);
        Padding = new Padding(1);
        Opacity = 0.8;
        DoubleBuffered = true;
        Text = "Audio Radar Overlay";

        _radar.Dock = DockStyle.Fill;
        _radar.Margin = new Padding(0);
        // Disabled so the parent form receives mouse events for dragging while
        // click-through is off. The custom paint code does not depend on Enabled.
        _radar.Enabled = false;
        Controls.Add(_radar);

        ClientSize = new Size(320, 320);
        UpdateRoundedRegion();
        _topMostTimer.Tick += (_, _) => ReassertTopMost();
    }

    public bool ForceTopMost
    {
        get => _forceTopMost;
        set
        {
            _forceTopMost = value;
            if (!value)
            {
                _topMostTimer.Stop();
            }
            else if (Visible)
            {
                ReassertTopMost();
                _topMostTimer.Start();
            }
        }
    }

    public string TopMostMode => _bandApplied
        ? "SystemTools band 16"
        : _bandAttempted
            ? $"TopMost fallback (band error {_bandError})"
            : "TopMost fallback";

    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            if (_clickThrough == value)
            {
                return;
            }

            _clickThrough = value;
            if (IsHandleCreated)
            {
                ApplyExtendedStyles();
            }
        }
    }

    public float OverlayOpacity
    {
        get => (float)Opacity;
        set => Opacity = Math.Clamp(value, 0.2f, 1.0f);
    }

    public void SetLayout(IReadOnlyList<SpeakerChannel> layout)
    {
        _radar.SetLayout(layout);
    }

    public void SetFrame(RadarFrame? frame)
    {
        _radar.SetFrame(frame);
    }

    public void SetOverlaySize(int size)
    {
        var clamped = Math.Clamp(size, 180, 700);
        ClientSize = new Size(clamped, clamped);
        UpdateRoundedRegion();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExLayered;
            if (_clickThrough)
            {
                cp.ExStyle |= WsExTransparent | WsExNoActivate;
            }

            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_positioned)
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(
                Math.Max(area.Left + 12, area.Right - Width - 24),
                Math.Max(area.Top + 12, area.Top + 24));
            _positioned = true;
        }

        ApplyExtendedStyles();
        TryApplySystemBand();
        ReassertTopMost();
        if (_forceTopMost)
        {
            _topMostTimer.Start();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && _forceTopMost)
        {
            TryApplySystemBand();
            ReassertTopMost();
            _topMostTimer.Start();
        }
        else
        {
            _topMostTimer.Stop();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _topMostTimer.Stop();
        _topMostTimer.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRoundedRegion();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest && !_clickThrough)
        {
            m.Result = HtCaption;
            return;
        }

        base.WndProc(ref m);
    }

    private void ApplyExtendedStyles()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var exStyle = GetWindowLong(Handle, GwlExStyle);
        exStyle |= WsExToolWindow | WsExLayered;
        if (_clickThrough)
        {
            exStyle |= WsExTransparent | WsExNoActivate;
        }
        else
        {
            exStyle &= ~(WsExTransparent | WsExNoActivate);
        }

        SetWindowLong(Handle, GwlExStyle, exStyle);
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private void ReassertTopMost()
    {
        if (!_forceTopMost || !Visible || !IsHandleCreated)
        {
            return;
        }

        // Repeatedly put this window at the front of the topmost group. This is
        // what wins against overlays that also use WS_EX_TOPMOST (RTSS, Discord,
        // etc.) without injecting anything into the game.
        SetWindowPos(Handle, HwndTopMost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private void TryApplySystemBand()
    {
        if (_bandAttempted || !IsHandleCreated)
        {
            return;
        }

        _bandAttempted = true;
        try
        {
            // SetWindowBand is an undocumented user32 export. It can put the
            // window into the SYSTEM_TOOLS band, above normal topmost windows.
            // It may fail with ERROR_ACCESS_DENIED unless the process has
            // UIAccess; the periodic topmost fallback still works.
            var user32 = GetModuleHandle("user32.dll");
            var proc = GetProcAddress(user32, "SetWindowBand");
            if (proc == IntPtr.Zero)
            {
                _bandError = 127;
                return;
            }

            var setWindowBand = Marshal.GetDelegateForFunctionPointer<SetWindowBandDelegate>(proc);
            _bandApplied = setWindowBand(Handle, IntPtr.Zero, ZbidSystemTools);
            if (!_bandApplied)
            {
                _bandError = Marshal.GetLastWin32Error();
            }
        }
        catch (Exception ex)
        {
            _bandError = ex.HResult;
        }
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var old = Region;
        using var path = RoundedRectangle(new Rectangle(0, 0, Width, Height), 18);
        Region = new Region(path);
        old?.Dispose();
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _topMostTimer.Stop();
            _topMostTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
