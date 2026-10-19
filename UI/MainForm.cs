using System.Diagnostics;
using AudioRadar.Audio;
using AudioRadar.Dsp;

namespace AudioRadar.UI;

public sealed class MainForm : Form
{

    private static readonly Color Page = Color.FromArgb(5, 8, 12);
    private static readonly Color Panel = Color.FromArgb(11, 17, 24);
    private static readonly Color PanelAlt = Color.FromArgb(14, 22, 30);
    private static readonly Color Border = Color.FromArgb(32, 45, 57);
    private static readonly Color TextColor = Color.FromArgb(220, 231, 242);
    private static readonly Color Muted = Color.FromArgb(115, 135, 152);
    private static readonly Color Accent = Color.FromArgb(56, 232, 198);
    private static readonly Color Warning = Color.FromArgb(255, 184, 72);

    private readonly ComboBox _endpointCombo = new();
    private readonly Button _refreshButton = CreateButton("刷新设备");
    private readonly Button _startButton = CreateButton("开始监听");
    private readonly Button _overlayButton = CreateButton("悬浮雷达");
    private readonly Label _statusLabel = new();
    private readonly Label _detailLabel = new();
    private readonly Label _engineInfo = new();
    private readonly Label _warningLabel = new();
    private readonly RadarControl _radar = new();
    private readonly ChannelMeterControl _meters = new();
    private readonly TrackBar _sensitivity = new();
    private readonly Label _sensitivityValue = new();
    private readonly NumericUpDown _lowHz = new();
    private readonly NumericUpDown _highHz = new();
    private readonly TrackBar _hold = new();
    private readonly Label _holdValue = new();
    private readonly CheckBox _blipsCheck = new();
    private readonly TrackBar _overlayOpacity = new();
    private readonly Label _overlayOpacityValue = new();
    private readonly NumericUpDown _overlaySize = new();
    private readonly CheckBox _overlayClickThrough = new();
    private readonly CheckBox _overlayForceTop = new();
    private readonly Label _overlayTopStatus = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 33 };
    private readonly System.Windows.Forms.Timer _deviceTimer = new() { Interval = 2000 };
    private readonly WasapiLoopbackCapture _capture = new();
    private readonly bool _autoStart;
    private readonly bool _showOverlay;
    private readonly string? _screenshotPath;

    private RadarEngine? _engine;
    private RadarOverlayForm? _overlay;
    private IReadOnlyList<AudioEndpoint> _endpoints = Array.Empty<AudioEndpoint>();
    private bool _updatingEndpoints;
    private bool _starting;
    private bool _followDefault = true;
    private string? _activeEndpointId;
    private string? _lastDefaultId;
    private string? _lastAudioError;

    public MainForm(bool autoStart = false, string? screenshotPath = null, bool showOverlay = false)
    {
        _autoStart = autoStart;
        _showOverlay = showOverlay;
        _screenshotPath = screenshotPath;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Audio Radar 1.0.0 — Windows endpoint loopback experiment";
        ClientSize = new Size(1280, 860);
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Page;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9f);
        DoubleBuffered = true;

        BuildUi();

        _capture.Error += OnCaptureError;
        _uiTimer.Tick += (_, _) => UpdateFromEngine();
        _deviceTimer.Tick += (_, _) => CheckDefaultDevice();
        _sensitivity.ValueChanged += (_, _) => ApplySettings();
        _lowHz.ValueChanged += (_, _) => ApplySettings();
        _highHz.ValueChanged += (_, _) => ApplySettings();
        _hold.ValueChanged += (_, _) => ApplySettings();
        _blipsCheck.CheckedChanged += (_, _) => _radar.ShowChannelBlips = _blipsCheck.Checked;
        _overlayButton.Click += (_, _) => ToggleOverlay();
        _overlayOpacity.ValueChanged += (_, _) =>
        {
            _overlayOpacityValue.Text = $"悬浮雷达不透明度：{_overlayOpacity.Value}%";
            if (_overlay is { IsDisposed: false })
            {
                _overlay.OverlayOpacity = _overlayOpacity.Value / 100f;
            }
        };
        _overlaySize.ValueChanged += (_, _) =>
        {
            if (_overlay is { IsDisposed: false })
            {
                _overlay.SetOverlaySize((int)_overlaySize.Value);
            }
        };
        _overlayClickThrough.CheckedChanged += (_, _) =>
        {
            if (_overlay is { IsDisposed: false })
            {
                _overlay.ClickThrough = _overlayClickThrough.Checked;
            }
        };
        _overlayForceTop.CheckedChanged += (_, _) =>
        {
            if (_overlay is { IsDisposed: false })
            {
                _overlay.ForceTopMost = _overlayForceTop.Checked;
                _overlayTopStatus.Text = _overlay.Visible
                    ? $"置顶模式：{_overlay.TopMostMode}"
                    : "置顶模式：已隐藏";
            }
        };
        _refreshButton.Click += (_, _) => RefreshEndpoints();
        _startButton.Click += (_, _) =>
        {
            if (_activeEndpointId is null)
            {
                StartCapture();
            }
            else
            {
                StopCapture();
            }
        };
        _endpointCombo.SelectedIndexChanged += (_, _) => OnEndpointSelectionChanged();

        Shown += (_, _) =>
        {
            FitToScreen();
            RefreshEndpoints();
            _deviceTimer.Start();
            if (_showOverlay)
            {
                BeginInvoke(new Action(ToggleOverlay));
            }

            if (_autoStart)
            {
                BeginInvoke(new Action(StartCapture));
            }

            if (_screenshotPath is not null)
            {
                var timer = new System.Windows.Forms.Timer { Interval = 4000 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    SaveScreenshot(_screenshotPath);
                };
                timer.Start();
            }
        };
        FormClosing += (_, _) =>
        {
            _uiTimer.Stop();
            _deviceTimer.Stop();
            _capture.Dispose();
            var overlay = _overlay;
            _overlay = null;
            if (overlay is { IsDisposed: false })
            {
                overlay.Close();
                overlay.Dispose();
            }
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Page,
            Padding = new Padding(0),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 3,
            BackColor = Panel,
            Padding = new Padding(16, 10, 16, 6),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titlePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Panel,
            Margin = new Padding(0),
        };
        titlePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        titlePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        var title = new Label
        {
            Text = "AUDIO RADAR",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            ForeColor = Accent,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        var subtitle = new Label
        {
            Text = "Windows 端点环回 · 多声道方向实验",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        titlePanel.Controls.Add(title, 0, 0);
        titlePanel.Controls.Add(subtitle, 0, 1);
        header.Controls.Add(titlePanel, 0, 0);
        header.SetRowSpan(titlePanel, 2);

        _endpointCombo.Dock = DockStyle.Fill;
        _endpointCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _endpointCombo.FlatStyle = FlatStyle.Flat;
        _endpointCombo.BackColor = PanelAlt;
        _endpointCombo.ForeColor = TextColor;
        _endpointCombo.Margin = new Padding(0, 4, 8, 4);
        header.Controls.Add(_endpointCombo, 1, 0);

        StyleButton(_refreshButton);
        _refreshButton.Dock = DockStyle.Fill;
        _refreshButton.Margin = new Padding(4, 4, 4, 4);
        header.Controls.Add(_refreshButton, 2, 0);

        StyleButton(_overlayButton);
        _overlayButton.Dock = DockStyle.Fill;
        _overlayButton.Margin = new Padding(4, 4, 4, 4);
        header.Controls.Add(_overlayButton, 3, 0);

        StyleButton(_startButton, Accent);
        _startButton.Dock = DockStyle.Fill;
        _startButton.Margin = new Padding(4, 4, 0, 4);
        header.Controls.Add(_startButton, 4, 0);

        _detailLabel.Dock = DockStyle.Fill;
        _detailLabel.ForeColor = Muted;
        _detailLabel.Font = new Font("Microsoft YaHei UI", 8f);
        _detailLabel.TextAlign = ContentAlignment.MiddleLeft;
        _detailLabel.AutoEllipsis = true;
        _detailLabel.Text = "正在读取 Windows 默认音频端点…";
        _detailLabel.Margin = new Padding(0, 5, 0, 0);
        header.Controls.Add(_detailLabel, 1, 1);
        header.SetColumnSpan(_detailLabel, 4);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = Muted;
        _statusLabel.Font = new Font("Microsoft YaHei UI", 8.2f);
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Text = "就绪。只读取 Windows 端点混音，不打开游戏进程。";
        header.Controls.Add(_statusLabel, 1, 2);
        header.SetColumnSpan(_statusLabel, 4);

        return header;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Page,
            Padding = new Padding(12, 8, 12, 8),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        body.Controls.Add(BuildSidePanel(), 0, 0);
        body.Controls.Add(BuildRadarPanel(), 1, 0);
        return body;
    }

    private Control BuildSidePanel()
    {
        var side = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Panel,
            Padding = new Padding(14),
            AutoScroll = true,
            Margin = new Padding(0, 0, 8, 0),
        };

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Panel,
        };

        stack.Controls.Add(SectionLabel("分析设置"));

        _sensitivity.Minimum = 0;
        _sensitivity.Maximum = 24;
        _sensitivity.Value = 6;
        _sensitivity.TickFrequency = 2;
        _sensitivity.SmallChange = 1;
        _sensitivity.LargeChange = 2;
        _sensitivity.Width = 286;
        _sensitivity.BackColor = Panel;
        _sensitivityValue.ForeColor = Accent;
        _sensitivityValue.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        _sensitivityValue.AutoSize = true;
        stack.Controls.Add(_sensitivityValue);
        stack.Controls.Add(_sensitivity);
        stack.Controls.Add(Hint("只保留高于自适应噪声底的声道能量；值越大，雷达越不敏感。"));

        stack.Controls.Add(Spacer(4));
        stack.Controls.Add(FieldLabel("分析频段 (Hz)"));
        var bandPanel = new TableLayoutPanel
        {
            Width = 286,
            Height = 30,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Panel,
        };
        bandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        bandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));
        bandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        bandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        ConfigureNumeric(_lowHz, 120);
        ConfigureNumeric(_highHz, 2500);
        bandPanel.Controls.Add(_lowHz, 0, 0);
        bandPanel.Controls.Add(new Label { Text = "–", ForeColor = Muted, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill }, 1, 0);
        bandPanel.Controls.Add(_highHz, 2, 0);
        stack.Controls.Add(bandPanel);
        stack.Controls.Add(Hint("脚步常用 120–2500 Hz；枪声/换弹可放宽到 8000 Hz。高频段需要更高采样率。"));

        stack.Controls.Add(Spacer(4));
        stack.Controls.Add(FieldLabel("方向保持"));
        _hold.Minimum = 0;
        _hold.Maximum = 1000;
        _hold.Value = 450;
        _hold.TickFrequency = 100;
        _hold.SmallChange = 25;
        _hold.LargeChange = 100;
        _hold.Width = 286;
        _hold.BackColor = Panel;
        _holdValue.ForeColor = Accent;
        _holdValue.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        _holdValue.AutoSize = true;
        stack.Controls.Add(_holdValue);
        stack.Controls.Add(_hold);
        stack.Controls.Add(Hint("短暂声音消失后，雷达继续显示最后方向的时间。"));

        stack.Controls.Add(Spacer(6));
        _blipsCheck.Text = "显示各声道独立光点";
        _blipsCheck.Checked = true;
        _blipsCheck.ForeColor = TextColor;
        _blipsCheck.BackColor = Panel;
        _blipsCheck.AutoSize = true;
        stack.Controls.Add(_blipsCheck);

        stack.Controls.Add(Spacer(10));
        stack.Controls.Add(SectionLabel("悬浮雷达"));
        _overlayOpacity.Minimum = 20;
        _overlayOpacity.Maximum = 100;
        _overlayOpacity.Value = 80;
        _overlayOpacity.TickFrequency = 10;
        _overlayOpacity.SmallChange = 5;
        _overlayOpacity.LargeChange = 10;
        _overlayOpacity.Width = 286;
        _overlayOpacity.BackColor = Panel;
        _overlayOpacityValue.ForeColor = Accent;
        _overlayOpacityValue.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        _overlayOpacityValue.AutoSize = true;
        _overlayOpacityValue.Text = "悬浮雷达不透明度：80%";
        stack.Controls.Add(_overlayOpacityValue);
        stack.Controls.Add(_overlayOpacity);

        var overlaySizePanel = new TableLayoutPanel
        {
            Width = 286,
            Height = 30,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Panel,
        };
        overlaySizePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        overlaySizePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        overlaySizePanel.Controls.Add(new Label
        {
            Text = "尺寸 (px)",
            ForeColor = TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
        }, 0, 0);
        _overlaySize.Minimum = 180;
        _overlaySize.Maximum = 700;
        _overlaySize.Increment = 20;
        _overlaySize.Value = 320;
        _overlaySize.Dock = DockStyle.Fill;
        _overlaySize.BackColor = PanelAlt;
        _overlaySize.ForeColor = TextColor;
        _overlaySize.BorderStyle = BorderStyle.FixedSingle;
        overlaySizePanel.Controls.Add(_overlaySize, 1, 0);
        stack.Controls.Add(overlaySizePanel);

        _overlayClickThrough.Text = "鼠标穿透（不挡游戏操作）";
        _overlayClickThrough.Checked = true;
        _overlayClickThrough.ForeColor = TextColor;
        _overlayClickThrough.BackColor = Panel;
        _overlayClickThrough.AutoSize = true;
        stack.Controls.Add(_overlayClickThrough);
        stack.Controls.Add(Hint("取消穿透后可直接拖动悬浮雷达定位；重新勾选穿透可避免游戏中误点。"));

        _overlayForceTop.Text = "强制置顶（对抗 RTSS / 游戏覆盖）";
        _overlayForceTop.Checked = true;
        _overlayForceTop.ForeColor = TextColor;
        _overlayForceTop.BackColor = Panel;
        _overlayForceTop.AutoSize = true;
        stack.Controls.Add(_overlayForceTop);

        _overlayTopStatus.AutoSize = true;
        _overlayTopStatus.MaximumSize = new Size(286, 0);
        _overlayTopStatus.ForeColor = Muted;
        _overlayTopStatus.Font = new Font("Microsoft YaHei UI", 7.8f);
        _overlayTopStatus.Text = "置顶模式：未启动";
        stack.Controls.Add(_overlayTopStatus);
        stack.Controls.Add(Hint("强制置顶每 33ms 重新置顶一次；会尝试 SYSTEM_TOOLS 窗口带，失败时回退到普通 TopMost。独占全屏游戏仍可能看不到外部窗口。"));

        stack.Controls.Add(Spacer(10));
        stack.Controls.Add(SectionLabel("端点与格式"));
        _engineInfo.AutoSize = true;
        _engineInfo.MaximumSize = new Size(286, 0);
        _engineInfo.ForeColor = Muted;
        _engineInfo.Font = new Font("Microsoft YaHei UI", 8.2f);
        _engineInfo.Text = "尚未启动。";
        stack.Controls.Add(_engineInfo);

        stack.Controls.Add(Spacer(8));
        _warningLabel.AutoSize = true;
        _warningLabel.MaximumSize = new Size(286, 0);
        _warningLabel.ForeColor = Warning;
        _warningLabel.Font = new Font("Microsoft YaHei UI", 8f);
        _warningLabel.Text =
            "Dolby / Atmos：端点环回看到的是 Windows 实际送出的 PCM 声道。" +
            "如果 Dolby Atmos for home theater 把高度对象封装进 MAT，loopback 只能分析 bed（本机通常是 7.1），" +
            "独立的 Top 声道不会出现；只有端点真正暴露 5.1.2/7.1.4 PCM 时，雷达才有仰角。";
        stack.Controls.Add(_warningLabel);

        stack.Controls.Add(Spacer(10));
        var soundButton = CreateButton("打开 Windows 声音设置");
        StyleButton(soundButton);
        soundButton.Width = 286;
        soundButton.Height = 30;
        soundButton.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
            }
            catch
            {
                // Ignore.
            }
        };
        stack.Controls.Add(soundButton);

        side.Controls.Add(stack);
        return side;
    }

    private Control BuildRadarPanel()
    {
        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Page,
        };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 72));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 28));

        _radar.Dock = DockStyle.Fill;
        _radar.Margin = new Padding(0, 0, 0, 6);
        _meters.Dock = DockStyle.Fill;
        _meters.Margin = new Padding(0, 6, 0, 0);

        right.Controls.Add(_radar, 0, 0);
        right.Controls.Add(_meters, 0, 1);
        return right;
    }

    private void RefreshEndpoints()
    {
        if (_updatingEndpoints)
        {
            return;
        }

        _updatingEndpoints = true;
        try
        {
            var previousId = (_endpointCombo.SelectedItem as AudioEndpoint)?.Id;
            var previousFollow = _endpointCombo.SelectedIndex <= 0;
            _endpoints = AudioEndpoint.EnumerateActive(out var defaultId);
            _lastDefaultId = defaultId;

            _endpointCombo.Items.Clear();
            _endpointCombo.Items.Add("系统默认（跟随 Windows）");
            foreach (var endpoint in _endpoints)
            {
                _endpointCombo.Items.Add(endpoint);
            }

            var targetIndex = 0;
            if (!previousFollow && previousId is not null)
            {
                for (var i = 0; i < _endpoints.Count; i++)
                {
                    if (string.Equals(_endpoints[i].Id, previousId, StringComparison.OrdinalIgnoreCase))
                    {
                        targetIndex = i + 1;
                        break;
                    }
                }
            }

            _endpointCombo.SelectedIndex = targetIndex;
            _followDefault = targetIndex == 0;
            _detailLabel.Text = _endpoints.Count == 0
                ? "没有发现活动输出端点。"
                : $"默认端点：{_endpoints.FirstOrDefault(e => e.IsDefault)?.FriendlyName ?? "(unknown)"} · 共 {_endpoints.Count} 个活动端点";
            ShowStatus("设备列表已刷新。", Muted);
        }
        catch (Exception ex)
        {
            ShowStatus("枚举音频端点失败：" + ex.Message, Warning);
        }
        finally
        {
            _updatingEndpoints = false;
        }

        if (_activeEndpointId is not null)
        {
            // The selected device may have disappeared; restart against the current selection.
            StopCaptureInternal();
            StartCapture();
        }
    }

    private void StartCapture()
    {
        if (_starting)
        {
            return;
        }

        _starting = true;
        _startButton.Enabled = false;
        try
        {
            var endpoint = ResolveSelectedEndpoint();
            if (endpoint is null)
            {
                ShowStatus("没有可用的默认输出端点。请先在 Windows 声音设置中选择输出设备。", Warning);
                return;
            }

            StopCaptureInternal();

            var format = _capture.Start(endpoint.Id, OnAudio);
            _engine = new RadarEngine(format.ChannelsLayout, (int)format.SampleRate)
            {
                SensitivityDb = _sensitivity.Value,
                LowHz = (float)_lowHz.Value,
                HighHz = (float)_highHz.Value,
                HoldMs = _hold.Value,
            };

            _radar.SetLayout(format.ChannelsLayout);
            _meters.SetLayout(format.ChannelsLayout);
            _radar.SetFrame(null);
            _overlay?.SetLayout(format.ChannelsLayout);
            _overlay?.SetFrame(null);

            _activeEndpointId = _capture.EndpointId ?? endpoint.Id;
            _startButton.Text = "停止监听";
            _sensitivityValue.Text = $"灵敏度：{_sensitivity.Value} dB";
            _holdValue.Text = $"保持：{_hold.Value} ms";
            UpdateEngineInfo(endpoint, format);
            ShowStatus($"正在监听：{endpoint.FriendlyName} · {format.Channels}ch · {format.LayoutName}", Accent);
            _uiTimer.Start();
        }
        catch (Exception ex)
        {
            StopCaptureInternal();
            ShowStatus("启动失败：" + ex.Message, Warning);
            MessageBox.Show(this,
                ex + "\r\n\r\n提示：如果端点是 Dolby/DTS 比特流或独占模式，请先在 Windows 声音设置中切换为 PCM 7.1/5.1，并确认该端点允许共享模式环回。",
                "启动音频环回失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _starting = false;
            _startButton.Enabled = true;
        }
    }

    private void StopCapture()
    {
        StopCaptureInternal();
        ShowStatus("已停止。", Muted);
    }

    private void StopCaptureInternal()
    {
        _uiTimer.Stop();
        _capture.Stop();
        _engine = null;
        _activeEndpointId = null;
        _startButton.Text = "开始监听";
        _radar.SetFrame(null);
        _overlay?.SetFrame(null);
        _meters.SetLayout(Array.Empty<SpeakerChannel>());
        _lowHz.Enabled = true;
        _highHz.Enabled = true;
        _sensitivity.Enabled = true;
        _hold.Enabled = true;
    }

    private void OnAudio(float[] samples, int frames)
    {
        var engine = _engine;
        if (engine is null)
        {
            return;
        }

        try
        {
            engine.Process(samples, frames);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _lastAudioError, ex.Message);
        }
    }

    private void UpdateFromEngine()
    {
        var error = Interlocked.Exchange(ref _lastAudioError, null);
        if (error is not null)
        {
            ShowStatus("分析线程错误：" + error, Warning);
        }

        var engine = _engine;
        if (engine is null)
        {
            return;
        }

        var frame = engine.Latest;
        if (frame is not null &&
            DateTime.UtcNow - frame.Timestamp > TimeSpan.FromMilliseconds(750))
        {
            _radar.SetFrame(null);
            _overlay?.SetFrame(null);
            ShowStatus("等待音频数据…（端点当前没有可分析的混音）", Muted);
            return;
        }

        _radar.SetFrame(frame);
        _overlay?.SetFrame(frame);
        if (frame is not null)
        {
            _meters.SetData(frame.ChannelDb);
            if (frame.HasDirection)
            {
                ShowStatus(
                    $"{frame.DirectionText} · 置信度 {frame.Confidence:P0} · {frame.OverallDb:0} dBFS" +
                    (frame.IsHeld ? " · 保持中" : "") +
                    (frame.Transient > 0.4 ? " · 瞬态" : ""),
                    frame.IsHeld ? Warning : Accent);
            }
            else
            {
                ShowStatus(frame.IsSilent ? "静音中…" : "方向不明确（可能是一个扩散声场）", Muted);
            }
        }
    }

    private void ToggleOverlay()
    {
        if (_overlay is { IsDisposed: false, Visible: true })
        {
            _overlay.Hide();
            _overlayButton.Text = "悬浮雷达";
            _overlayTopStatus.Text = "置顶模式：已隐藏";
            return;
        }

        var overlay = EnsureOverlay();
        overlay.Show();
        overlay.BringToFront();
        _overlayButton.Text = "隐藏悬浮";
        _overlayTopStatus.Text = $"置顶模式：{overlay.TopMostMode}";
    }

    private RadarOverlayForm EnsureOverlay()
    {
        if (_overlay is { IsDisposed: false })
        {
            return _overlay;
        }

        var overlay = new RadarOverlayForm
        {
            OverlayOpacity = _overlayOpacity.Value / 100f,
            ClickThrough = _overlayClickThrough.Checked,
            ForceTopMost = _overlayForceTop.Checked,
        };
        overlay.SetOverlaySize((int)_overlaySize.Value);

        var layout = _engine?.Layout ?? _capture.Format?.ChannelsLayout;
        if (layout is not null)
        {
            overlay.SetLayout(layout);
        }

        overlay.SetFrame(_engine?.Latest);
        overlay.FormClosed += (_, _) =>
        {
            _overlay = null;
            _overlayButton.Text = "悬浮雷达";
            _overlayTopStatus.Text = "置顶模式：未启动";
        };

        _overlay = overlay;
        return overlay;
    }

    private void OnCaptureError(Exception exception)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            StopCaptureInternal();
            var prefix = exception is LoopbackDeviceInvalidatedException
                ? "音频端点已失效（设备移除或默认设备改变）"
                : "音频环回错误";
            ShowStatus($"{prefix}：{exception.Message}", Warning);
            RefreshEndpoints();
        }));
    }

    private void CheckDefaultDevice()
    {
        try
        {
            var defaultId = AudioEndpoint.GetDefaultId();
            if (defaultId is null)
            {
                return;
            }

            if (_followDefault && _activeEndpointId is not null &&
                _lastDefaultId is not null &&
                !string.Equals(defaultId, _lastDefaultId, StringComparison.OrdinalIgnoreCase))
            {
                _lastDefaultId = defaultId;
                ShowStatus("Windows 默认设备已改变，正在切换…", Warning);
                StopCaptureInternal();
                RefreshEndpoints();
                StartCapture();
            }
            else
            {
                _lastDefaultId = defaultId;
            }
        }
        catch
        {
            // Polling is best-effort.
        }
    }

    private void OnEndpointSelectionChanged()
    {
        if (_updatingEndpoints)
        {
            return;
        }

        _followDefault = _endpointCombo.SelectedIndex <= 0;
        if (_activeEndpointId is not null && !_starting)
        {
            StopCaptureInternal();
            StartCapture();
        }
    }

    private AudioEndpoint? ResolveSelectedEndpoint()
    {
        if (_endpointCombo.SelectedIndex <= 0)
        {
            return _endpoints.FirstOrDefault(e => e.IsDefault) ?? AudioEndpoint.GetDefault();
        }

        return _endpointCombo.SelectedItem as AudioEndpoint;
    }

    private void ApplySettings()
    {
        _sensitivityValue.Text = $"灵敏度：{_sensitivity.Value} dB";
        _holdValue.Text = $"保持：{_hold.Value} ms";
        if (_lowHz.Value >= _highHz.Value)
        {
            _highHz.Value = Math.Min(_highHz.Maximum, _lowHz.Value + 10);
        }

        var engine = _engine;
        if (engine is null)
        {
            return;
        }

        engine.SensitivityDb = _sensitivity.Value;
        engine.LowHz = (float)_lowHz.Value;
        engine.HighHz = (float)_highHz.Value;
        engine.HoldMs = _hold.Value;
    }

    private void UpdateEngineInfo(AudioEndpoint endpoint, WaveFormatInfo format)
    {
        var height = ChannelLayout.HasHeightChannels(format.ChannelMask);
        var stereoNote = format.Channels == 2
            ? (endpoint.PhysicalSpeakerMask == 0x63F
                ? "\r\n警告：端点物理配置是 7.1，但当前 mix format 是 2ch；请在 Windows 声音设置中重新选择 Dolby Atmos for home theater，或把默认格式设为 7.1。"
                : "\r\n立体声模式：只能判断左右；前后/高度未知。要 360° 请把 Windows 端点/游戏设为 7.1。")
            : "";
        _engineInfo.Text =
            $"端点：{endpoint.FriendlyName}\r\n" +
            $"格式：{format.Summary}\r\n" +
            $"声道顺序：{string.Join(", ", format.ChannelsLayout.Select(c => c.Label))}\r\n" +
            $"高度声道：{(height ? "有" : "无")}" +
            stereoNote;
    }

    private void FitToScreen()
    {
        var area = Screen.FromControl(this).WorkingArea;
        var width = Math.Min(1280, Math.Max(960, area.Width - 80));
        var height = Math.Min(860, Math.Max(640, area.Height - 80));
        ClientSize = new Size(width, height);
        CenterToScreen();
    }

    private void SaveScreenshot(string path)
    {
        try
        {
            using var bitmap = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            if (_overlay is { IsDisposed: false, Visible: true })
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
                var overlayPath = Path.Combine(
                    directory,
                    Path.GetFileNameWithoutExtension(path) + "-overlay.png");
                using var overlayBitmap = new Bitmap(
                    Math.Max(1, _overlay.Width), Math.Max(1, _overlay.Height));
                _overlay.DrawToBitmap(overlayBitmap,
                    new Rectangle(0, 0, overlayBitmap.Width, overlayBitmap.Height));
                overlayBitmap.Save(overlayPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        catch
        {
            // Screenshot mode is diagnostic only.
        }

        Close();
    }

    private void ShowStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    private static Button CreateButton(string text, Color? accent = null)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = accent is null ? PanelAlt : Color.FromArgb(19, 58, 52),
            ForeColor = accent ?? TextColor,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = accent ?? Border;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 38, 48);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(18, 26, 34);
        return button;
    }

    private static void StyleButton(Button button, Color? accent = null)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = accent is null ? PanelAlt : Color.FromArgb(19, 58, 52);
        button.ForeColor = accent ?? TextColor;
        button.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderColor = accent ?? Border;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 38, 48);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(18, 26, 34);
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextColor,
        Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
        Margin = new Padding(0, 4, 0, 8),
    };

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextColor,
        Font = new Font("Microsoft YaHei UI", 8.8f),
        Margin = new Padding(0, 2, 0, 2),
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(286, 0),
        ForeColor = Muted,
        Font = new Font("Microsoft YaHei UI", 7.8f),
        Margin = new Padding(0, 2, 0, 2),
    };

    private static Control Spacer(int height) => new Panel
    {
        Height = height,
        Width = 1,
        BackColor = Color.Transparent,
        Margin = new Padding(0),
    };

    private static void ConfigureNumeric(NumericUpDown numeric, decimal value)
    {
        numeric.Minimum = 20;
        numeric.Maximum = 20000;
        numeric.Increment = 10;
        numeric.DecimalPlaces = 0;
        numeric.Value = value;
        numeric.Dock = DockStyle.Fill;
        numeric.BackColor = PanelAlt;
        numeric.ForeColor = TextColor;
        numeric.BorderStyle = BorderStyle.FixedSingle;
        numeric.ThousandsSeparator = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _capture.Dispose();
            _uiTimer.Dispose();
            _deviceTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
