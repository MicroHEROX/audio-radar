# audio-radar-dsh

Windows endpoint-loopback audio direction radar for accessibility and DeepSeek Harness workflows.

```bash
npm install -g audio-radar-dsh
audio-radar
```

The launcher downloads the matching Windows x64 release from GitHub on first run and verifies its SHA-256 checksum.

- Windows 10 2004+ x64
- No .NET SDK required (self-contained release)
- No audio recording, telemetry, or API keys
- Not affiliated with Microsoft, Dolby, or DeepSeek

Documentation, architecture, API reference, glossary, and solution notes live in the [GitHub repository](https://github.com/MicroHEROX/audio-radar). They are intentionally not included in this npm package.

## Uninstall

```bash
npm uninstall -g audio-radar-dsh
```

Optionally remove the runtime cache:

```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\AudioRadar" -ErrorAction SilentlyContinue
```

MIT licensed.
