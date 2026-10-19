# Security and Privacy

## Data handling

- Audio frames are processed in memory and discarded.
- The app does not record audio to disk.
- The app does not upload audio or analytics.
- The app has no account system and no API keys.

## Local files

The app writes only:

- diagnostic text files explicitly requested with `--probe`, `--selftest`, or `--capture-test`;
- PNG screenshots explicitly requested with `--screenshot`;
- npm launcher cache under `%LOCALAPPDATA%\AudioRadar` (only when installed through npm).

## Network

The radar itself performs no network requests. The npm launcher contacts GitHub Releases to download the runtime.

## Process and game safety

- No DLL injection.
- No game memory reading.
- No game file modification.
- No driver installation.
- No anti-cheat bypass.

The overlay is a normal external window. It may still be considered an information advantage in multiplayer games. Check the game's terms of service.

## Sensitive repository policy

The public repository must not contain:

- personal email addresses, access tokens, cookies, or credentials;
- local absolute paths from a contributor machine;
- telemetry identifiers;
- downloaded third-party binaries unless required at runtime;
- recorded game audio, voice chat, or personal media.

The release ZIP contains only runtime application files. Documentation stays in the repository.

## Threat model

Audio Radar trusts the Windows audio stack and the selected endpoint. It does not protect against a compromised audio driver, malicious endpoint, or a hostile local process. It is not a security boundary.

## Reporting

For a security issue, open a private security advisory on the GitHub repository rather than a public issue.
