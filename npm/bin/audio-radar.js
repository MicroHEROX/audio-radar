#!/usr/bin/env node
'use strict';

const fs = require('fs');
const fsp = fs.promises;
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');

const LAUNCHER_VERSION = '1.0.1';
const APP_VERSION = '1.0.0';
const OWNER = 'MicroHEROX';
const REPO = 'audio-radar';
const ASSET = `AudioRadar-${APP_VERSION}-win-x64.zip`;
const RELEASE_BASE = `https://github.com/${OWNER}/${REPO}/releases/download/v${APP_VERSION}`;
const ZIP_URL = `${RELEASE_BASE}/${ASSET}`;
const SUMS_URL = `${RELEASE_BASE}/SHA256SUMS.txt`;

function usage() {
  console.log(`Audio Radar ${APP_VERSION} (npm launcher ${LAUNCHER_VERSION})

Usage:
  audio-radar [options]

Options:
  --probe [path]              Write endpoint diagnostics
  --selftest [path]           Run algorithm self-test
  --capture-test [path] [s]   Capture default endpoint for N seconds
  --autostart                 Start listening after launch
  --overlay                   Open the floating radar overlay
  --screenshot [path]         Render a screenshot and exit
  --version, -v               Print version
  --help, -h                  Show this help

The launcher downloads the Windows x64 release from GitHub on first run.
`);
}

function cacheRoot() {
  if (process.env.AUDIO_RADAR_CACHE) {
    return process.env.AUDIO_RADAR_CACHE;
  }
  const local = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
  return path.join(local, 'AudioRadar', APP_VERSION);
}

function download(url, destination) {
  // PowerShell honours the Windows system proxy and handles GitHub redirects.
  // Node's built-in fetch does not consistently use WinHTTP/system proxy
  // settings on Windows, so use the platform's own downloader.
  const escapedUrl = url.replace(/'/g, "''");
  const escapedDest = destination.replace(/'/g, "''");
  const command = [
    "$ErrorActionPreference = 'Stop'",
    "$ProgressPreference = 'SilentlyContinue'",
    `Invoke-WebRequest -Uri '${escapedUrl}' -OutFile '${escapedDest}' -UseBasicParsing -TimeoutSec 300`,
  ].join('; ');
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', command], {
    stdio: 'inherit',
  });
  if (result.status !== 0) {
    throw new Error(`Download failed with exit code ${result.status}: ${url}`);
  }
}

function expectedHash(sumsPath) {
  const text = fs.readFileSync(sumsPath, 'utf8');
  for (const line of text.split(/\r?\n/)) {
    const match = line.trim().match(/^([a-fA-F0-9]{64})\s+\*?(.+)$/);
    if (match && match[2].trim() === ASSET) {
      return match[1].toLowerCase();
    }
  }
  throw new Error(`Checksum for ${ASSET} not found in SHA256SUMS.txt`);
}

async function sha256File(filePath) {
  return new Promise((resolve, reject) => {
    const hash = crypto.createHash('sha256');
    fs.createReadStream(filePath)
      .on('data', (chunk) => hash.update(chunk))
      .on('end', () => resolve(hash.digest('hex')))
      .on('error', reject);
  });
}

function extract(zipPath, destination) {
  fs.mkdirSync(destination, { recursive: true });
  const escapedZip = zipPath.replace(/'/g, "''");
  const escapedDest = destination.replace(/'/g, "''");
  const command = [
    "$ErrorActionPreference = 'Stop'",
    `Expand-Archive -LiteralPath '${escapedZip}' -DestinationPath '${escapedDest}' -Force`,
  ].join('; ');
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', command], {
    stdio: 'inherit',
  });
  if (result.status !== 0) {
    throw new Error(`Expand-Archive failed with exit code ${result.status}`);
  }
}

async function ensureRuntime() {
  if (process.platform !== 'win32') {
    throw new Error('Audio Radar is a Windows application. This launcher only runs on Windows.');
  }
  if (process.arch !== 'x64') {
    throw new Error(`Unsupported architecture: ${process.arch}. Windows x64 is required.`);
  }

  const root = cacheRoot();
  const exePath = path.join(root, 'AudioRadar.exe');
  if (fs.existsSync(exePath)) {
    return exePath;
  }

  fs.mkdirSync(root, { recursive: true });
  const zipPath = path.join(root, ASSET);
  const tempZip = `${zipPath}.download`;
  const sumsPath = `${zipPath}.sums`;

  if (!fs.existsSync(zipPath)) {
    console.error(`Downloading Audio Radar ${APP_VERSION}...`);
    download(SUMS_URL, sumsPath);
    const expected = expectedHash(sumsPath);
    download(ZIP_URL, tempZip);
    const actual = await sha256File(tempZip);
    if (actual !== expected) {
      await fsp.rm(tempZip, { force: true });
      await fsp.rm(sumsPath, { force: true });
      throw new Error(`Checksum mismatch.\nExpected: ${expected}\nActual:   ${actual}`);
    }
    await fsp.rename(tempZip, zipPath);
    await fsp.rm(sumsPath, { force: true });
  }

  console.error('Extracting...');
  extract(zipPath, root);
  if (!fs.existsSync(exePath)) {
    throw new Error(`Extraction completed but ${exePath} was not found.`);
  }
  return exePath;
}

async function main() {
  const args = process.argv.slice(2);
  if (args.includes('--version') || args.includes('-v')) {
    console.log(`${LAUNCHER_VERSION} (Audio Radar ${APP_VERSION})`);
    return;
  }
  if (args.includes('--help') || args.includes('-h')) {
    usage();
    return;
  }

  const exe = await ensureRuntime();
  const result = spawnSync(exe, args, { stdio: 'inherit', windowsHide: false });
  if (result.error) {
    throw result.error;
  }
  process.exit(result.status === null ? 1 : result.status);
}

main().catch((error) => {
  console.error(`audio-radar: ${error.message}`);
  process.exit(1);
});
