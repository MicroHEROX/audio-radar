# Installation and Troubleshooting

## Requirements

- Windows 10 version 2004 or later, x64.
- A render endpoint whose mix format is PCM/float.
- For 360°: a discrete multichannel endpoint and a game configured to output to it.
- For source builds: .NET 8 SDK.

## Install from GitHub Releases

1. Download `AudioRadar-1.0.0-win-x64.zip`.
2. Extract it anywhere outside a protected system directory.
3. Run `AudioRadar.exe`.

The release is self-contained for Windows x64. Documentation is not part of the runtime ZIP.

## Install from npm

```bash
npm install -g audio-radar-dsh
audio-radar
```

The launcher downloads the matching Release ZIP to `%LOCALAPPDATA%\AudioRadar`. It does not install repository documentation.

## Build from source

```powershell
git clone https://github.com/MicroHEROX/audio-radar.git
cd audio-radar
dotnet build
dotnet run
```

## First run

1. Select **System default (follow Windows)** or a specific endpoint.
2. Click **Start listening**.
3. Check the format line in the sidebar.
4. If it says `Stereo 2.0`, the radar can only resolve left/right.

## Endpoint shows 2ch / Stereo 2.0 unexpectedly

Windows may switch an HDMI endpoint to stereo after another application opens it in stereo, or after spatial audio is disabled.

Try:

1. Windows Sound settings → the HDMI endpoint → Spatial sound → **Dolby Atmos for home theater**.
2. Or set the default format to **7.1 / 24-bit 48000 Hz**.
3. Restart Windows Audio or reboot.

The app detects the mismatch between a 7.1 physical speaker mask and a 2ch mix format and shows a warning.

## No audio / no frames

- The endpoint may be silent. Play something to that endpoint.
- The endpoint may be in an encoded bitstream mode. Switch to PCM.
- The endpoint may be in exclusive mode. Close exclusive-mode applications.
- Try the `--capture-test` diagnostic.

## Overlay is behind the game

- Use **borderless/windowed** mode. External overlays cannot guarantee exclusive fullscreen.
- Keep **Force topmost** enabled.
- Check the overlay status. `TopMost fallback (band error 5)` is normal for a non-UIAccess process.
- RTSS OSDs that render directly into the game swapchain are outside external-window control.

## Overlay steals mouse input

Enable **Click-through**. Disable it only to drag the overlay, then re-enable it.

## Uninstall

### ZIP/source

Delete the extracted folder.

### npm

```bash
npm uninstall -g audio-radar-dsh
```

Remove `%LOCALAPPDATA%\AudioRadar` if present.

### Optional Kimi Browser Extension

```powershell
& "$env:USERPROFILE\.kimi-webbridge\bin\kimi-webbridge.exe" uninstall
```

Then remove **Kimi** from `chrome://extensions`.
