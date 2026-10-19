# API and CLI Reference

Audio Radar 1.0.0 has no network API and no stable plugin API. The public contract is the command line plus the user interface.

## Command line

```text
AudioRadar.exe [options]
```

| Option | Description |
|---|---|
| `--probe [path]` | Write endpoint/format diagnostics. Default path: `probe.txt` |
| `--selftest [path]` | Run layout and direction tests without audio hardware. Default: `selftest.txt` |
| `--capture-test [path] [seconds]` | Capture the default endpoint for N seconds, write per-channel RMS/peak and direction samples |
| `--autostart` | Start listening automatically after the window appears |
| `--overlay` | Show the floating radar overlay at startup |
| `--screenshot [path]` | Render the main window to PNG after four seconds and exit |
| `--help` | Reserved; use the README for now |

### Examples

```powershell
AudioRadar.exe
AudioRadar.exe --probe probe.txt
AudioRadar.exe --selftest selftest.txt
AudioRadar.exe --capture-test capture.txt 10
AudioRadar.exe --overlay
AudioRadar.exe --autostart --overlay --screenshot ui.png
```

When `--screenshot` is used with `--overlay`, an additional file named `<name>-overlay.png` is produced.

## Internal C# API

These types are implementation details and may change.

### `AudioEndpoint`

```csharp
IReadOnlyList<AudioEndpoint> EnumerateActive(out string? defaultId);
AudioEndpoint? GetDefault();
string? GetDefaultId();
AudioEndpoint? GetById(string id);
```

Properties include `Id`, `FriendlyName`, `Description`, `FormFactor`, `IsDefault`, `MixFormat`, `PhysicalSpeakerMask`.

### `WasapiLoopbackCapture`

```csharp
WaveFormatInfo Start(string? endpointId, Action<float[], int> onSamples);
void Stop();
```

- `endpointId == null` follows the system default render endpoint.
- `onSamples` receives interleaved float samples and a frame count.
- `Format` exposes the endpoint mix format.
- `Error` reports capture failures.

### `RadarEngine`

```csharp
RadarEngine(IReadOnlyList<SpeakerChannel> layout, int sampleRate);
void Process(float[] interleaved, int frames);
void Reset();
RadarFrame? Latest { get; }
```

Settings: `SensitivityDb`, `LowHz`, `HighHz`, `HoldMs`.

`RadarFrame` contains:

- `ChannelDb[]`
- `ChannelLevels[]`
- `OverallDb`
- `AzimuthDegrees`, `ElevationDegrees`
- `Confidence`
- `HasDirection`, `IsHeld`, `IsSilent`
- `Transient`
- `DirectionText`
- `Blips[]`

### `ChannelLayout`

```csharp
IReadOnlyList<SpeakerChannel> FromMask(uint channelMask, int channelCount);
string Describe(uint channelMask, int channelCount);
bool HasHeightChannels(uint channelMask);
string DirectionText(double azimuth, double elevation);
```

## Overlay API

`RadarOverlayForm` exposes:

```csharp
bool ClickThrough { get; set; }
bool ForceTopMost { get; set; }
float OverlayOpacity { get; set; }
void SetLayout(IReadOnlyList<SpeakerChannel> layout);
void SetFrame(RadarFrame? frame);
void SetOverlaySize(int size);
string TopMostMode { get; }
```

`TopMostMode` reports either `SystemTools band 16` or `TopMost fallback (band error N)`.

## Data and privacy

- No HTTP API, telemetry endpoint, account, or API key.
- Audio is not written to disk by the app.
- Diagnostics files are plain text written only when the corresponding CLI option is used.
