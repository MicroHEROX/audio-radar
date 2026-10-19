# Changelog

## 1.0.0 — 2026-10-19

Initial public release.

- Windows endpoint-loopback capture using WASAPI shared mode.
- Default endpoint discovery with manual endpoint override.
- Dynamic `WAVEFORMATEXTENSIBLE` channel-mask support:
  - stereo, 5.1, 7.1;
  - 5.1.2, 7.1.2, 5.1.4, 7.1.4.
- Direction estimation with azimuth, elevation, confidence, transient emphasis, and hold.
- Channel-level meters.
- Semi-transparent, click-through, always-on-top radar overlay.
- 33 ms topmost re-assert loop and `SYSTEM_TOOLS` band attempt.
- Diagnostics: endpoint probe, self-test, capture test, screenshot mode.
- English and Simplified Chinese README.
- MIT license.
