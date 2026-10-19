# Testing

## Automated self-test

No audio hardware is required.

```powershell
AudioRadar.exe --selftest selftest.txt
```

The self-test covers:

- stereo layout;
- 5.1, 7.1;
- 5.1.2, 7.1.2;
- 5.1.4, 7.1.4;
- direction estimates for stereo, 5.1, 7.1, 5.1.2 and 7.1.4 layouts.

Expected result:

```text
RESULT: PASS
```

## Endpoint probe

```powershell
AudioRadar.exe --probe probe.txt
```

Check:

- default endpoint ID;
- channel count;
- sample rate;
- `WAVEFORMATEXTENSIBLE` sub-format;
- channel mask;
- channel order;
- height-channel presence.

## Capture test

```powershell
AudioRadar.exe --capture-test capture.txt 10
```

While it runs, play a known multichannel source. The report includes per-channel RMS/peak and direction samples.

### Channel-isolation test

Use a 7.1 WAV with a tone only in FL. Expected: FL has signal; all other channels are near `-90 dBFS`.

### Direction-sequence test

Use a 7.1 WAV with a tone moving through BL, BR, SL, SR. Expected direction sequence:

```text
left rear 135° → right rear 135° → left 90° → right 90° → silence
```

## Release installation test

After publishing a GitHub Release:

1. download the release ZIP from the public URL;
2. extract to a clean temporary directory;
3. run `AudioRadar.exe --probe probe.txt`;
4. run `AudioRadar.exe --selftest selftest.txt`;
5. start the UI and verify the overlay opens.

Test the npm package separately:

```bash
npm install -g audio-radar-dsh
audio-radar --selftest selftest.txt
```

The npm launcher must download only the release runtime ZIP, not repository docs.

## Windows overlay checks

Use a tool such as `EnumWindows` / `GetWindowLong` to verify:

- `WS_EX_TOPMOST`;
- `WS_EX_LAYERED`;
- `WS_EX_TRANSPARENT` when click-through is on;
- `WS_EX_NOACTIVATE` when click-through is on;
- `WS_EX_TOOLWINDOW`.

`SetWindowBand` normally fails with `ERROR_ACCESS_DENIED (5)` without UIAccess; the fallback topmost timer must still run.

## Known test environment

| Item | Value |
|---|---|
| Windows | 10.0.26300 x64 |
| .NET | 8 |
| DeepSeek Harness | 44.7.0 |
| Default endpoint in test | TCL HDMI, 8ch 48 kHz float, 7.1 mask `0x63F` |
| Height channels | Not exposed by that endpoint |

The test environment is documented for reproducibility, not as a minimum requirement.
