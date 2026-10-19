# Architecture

## Scope

Audio Radar is a standalone Windows Forms application. It reads the final mix from a render endpoint, estimates audio direction, and renders a radar. It is not a DeepSeek Harness plugin and has no DSH API dependency.

## Signal chain

```text
Game / system audio
        │
        ▼
Windows audio engine
        │  shared-mode render endpoint
        ▼
WASAPI endpoint loopback
        │  interleaved float / PCM frames
        ▼
RadarEngine
  ├─ framing
  ├─ per-channel FFT
  ├─ band energy
  ├─ adaptive noise floor
  ├─ 3D direction vector
  ├─ confidence / transient / hold
  └─ RadarFrame
        │
        ├── MainForm radar + channel meters
        └── RadarOverlayForm (33 ms topmost re-assert)
```

## Components

| Component | Responsibility |
|---|---|
| `Audio/AudioEndpoint.cs` | Enumerate active render endpoints, read the default endpoint, read endpoint properties |
| `Audio/WaveFormatInfo.cs` | Parse `WAVEFORMATEX` / `WAVEFORMATEXTENSIBLE` |
| `Audio/ChannelLayout.cs` | Map speaker mask bits to labels, azimuth, elevation |
| `Audio/WasapiLoopbackCapture.cs` | Open a shared-mode loopback stream and convert samples to float |
| `Interop/ComInterfaces.cs` | Minimal COM interop for MMDevice, WASAPI, and property store |
| `Dsp/Fft.cs` | Radix-2 FFT |
| `Dsp/RadarEngine.cs` | Signal processing, direction estimation, transient/hold logic |
| `UI/MainForm.cs` | Main window, endpoint selection, settings, timers |
| `UI/RadarControl.cs` | Main radar drawing |
| `UI/ChannelMeterControl.cs` | Per-channel dBFS meters |
| `UI/RadarOverlayForm.cs` | Borderless, semi-transparent, topmost, click-through overlay |
| `Diagnostics.cs` | Probe, self-test, capture test, screenshot mode |

## Threading

- The UI thread owns WinForms controls and the update timer.
- A dedicated capture thread polls `IAudioCaptureClient`, converts samples, and calls `RadarEngine.Process`.
- `RadarEngine` publishes an immutable `RadarFrame` via `Volatile.Write`.
- The UI timer reads the latest frame, updates the radar, meters, and overlay.
- The overlay has its own 33 ms timer that only re-asserts topmost Z-order.

## Direction model

Each non-LFE channel has a unit vector:

```text
x = sin(az) * cos(el)
y = cos(az) * cos(el)
z = sin(el)
```

Channel band energy is weighted, then summed:

```text
az = atan2(Σwx, Σwy)
el = atan2(Σwz, hypot(Σwx, Σwy))
```

LFE is excluded from direction. Confidence combines vector coherence and normalized channel-energy entropy.

## Stereo fallback

A 2-channel endpoint is mapped to hard left/right (`-90°`, `+90°`) rather than physical speaker angles. The UI labels it as “stereo: front/back unknown.” This is deliberate: a stereo endpoint cannot provide a reliable 360° estimate.

## Latency budget

Typical contributors:

- WASAPI shared-mode buffering: ~10–30 ms
- FFT / framing: ~5–15 ms
- UI timer: up to 33 ms
- Display composition: one or two frames

Total is typically tens of milliseconds. Bluetooth output adds 100–300 ms and is not recommended for competitive use.

## Known architectural limits

- One endpoint mix is not a source-separated recording.
- Dolby MAT object metadata is not exposed through endpoint loopback.
- External windows cannot cover exclusive fullscreen.
- The overlay is not injected and cannot draw inside the game's swapchain.
