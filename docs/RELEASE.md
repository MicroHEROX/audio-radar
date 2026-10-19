# Release Process

## Versioning

Audio Radar uses semantic versioning. The current release is **1.0.0**.

- Package/assembly version: `1.0.0`
- Git tag: `v1.0.0`
- Release ZIP: `AudioRadar-1.0.0-win-x64.zip`
- npm package: `audio-radar-dsh@1.0.0`

## Compatibility

| Item | Value |
|---|---|
| OS | Windows 10/11 x64 |
| Runtime | .NET 8, self-contained release |
| Tested with DeepSeek Harness | 44.7.0 |
| DSH plugin/API dependency | None |

## Build

```powershell
dotnet publish AudioRadar.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/publish
```

Create the runtime ZIP:

```text
AudioRadar.exe
LICENSE.txt
```

Do **not** include:

- README and docs;
- API/glossary/solution documents;
- source files;
- test WAVs;
- screenshots;
- `.git`, `bin`, or `obj`.

## GitHub Release

1. Push the repository.
2. Create tag `v1.0.0`.
3. Upload `AudioRadar-1.0.0-win-x64.zip`.
4. Upload `SHA256SUMS.txt`.
5. Add release notes summarizing features, limitations, and compatibility.
6. Test the public download URL in a clean directory.

## npm

The npm package contains only:

- `package.json`;
- `bin/audio-radar.js`;
- package README;
- license.

It does **not** include the repository docs. The launcher downloads the GitHub Release ZIP on first run.

```bash
npm pack
npm publish --access public
```

## Verification checklist

- [ ] `dotnet build -c Release` succeeds with zero warnings.
- [ ] `--selftest` passes.
- [ ] `--probe` reports the expected endpoint.
- [ ] ZIP runs on a clean machine without the .NET SDK.
- [ ] npm global install launches the app.
- [ ] GitHub Release download and extraction works.
- [ ] No secrets or local paths in the repository.
- [ ] README language switch works.
- [ ] License and acknowledgments are present.
