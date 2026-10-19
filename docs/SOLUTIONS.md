# Solutions and Trade-offs

## Goal

Provide directional awareness for players who cannot localize game audio, without injecting into the game or reading game memory.

## Viable solutions

### 1. Discrete endpoint loopback (current)

Read the 5.1/7.1/7.1.4 PCM mix from the selected Windows render endpoint.

**Pros**

- No game process interaction.
- Works with any game that outputs to that endpoint.
- Direct relationship between speaker channels and direction.
- Low implementation complexity compared with ML separation.

**Cons**

- Only sees what Windows sends to the endpoint.
- Atmos height objects may be hidden in MAT.
- Exclusive fullscreen overlay limitations remain.

### 2. Endpoint loopback + event classification

Add a classifier for footsteps, gunshots, reloads, and other transients.

**Pros**

- Reduces music/ambient false positives.
- Can improve UI relevance.

**Cons**

- Requires labeled game audio.
- Classifier errors become new false positives.

### 3. Multi-peak display without tracking

Show several channel peaks or transient directions at once.

**Pros**

- Cheap.
- Useful when two sounds are separated in channels.

**Cons**

- Still not multi-target tracking.
- Adjacent channels can duplicate one source.

### 4. Spectral direction histogram

Estimate direction per FFT bin and cluster bins into spatial peaks.

**Pros**

- Can separate sources with different spectra.
- Better than raw channel peaks for simultaneous sounds.

**Cons**

- Similar spectra merge.
- More tuning and latency.

### 5. Personalized HRTF playback

For users with one working ear or asymmetric hearing, capture before mono downmix, analyze, then downmix for the user.

**Pros**

- Preserves direction for the radar while making audio audible.
- Avoids Windows “mono audio” destroying the analysis input.

**Cons**

- Requires an audio routing/virtual cable setup.
- Mono downmix can mask some cues.

## Conditionally viable

### Multi-target tracking

Feasible for 2–4 targets when sources are separated by direction and/or spectrum. Requires:

- multi-peak measurement extraction;
- gating + Hungarian assignment;
- Kalman/alpha-beta tracking;
- track birth/death and identity uncertainty.

Not reliable when targets share the same channel and band.

### 7.1.4 height tracking

Only when the endpoint exposes real 12-channel PCM. A 7.1 bed plus MAT metadata is not enough.

## Not viable / not recommended

### Source separation from one mixed endpoint

Two sources mixed into the same channel/band are not separable by a display algorithm. Blind source separation or learned models can create plausible stems, but cannot guarantee the game's true sources.

### Player identity

There is no player ID, object ID, or game state in the endpoint mix.

### Distance in meters

Loudness and timbre are affected by game mix, occlusion, and volume. Only relative “near/far” estimates are possible.

### Atmos object radar from WASAPI loopback

Endpoint loopback does not expose MAT object metadata.

### Overlay above exclusive fullscreen without injection

External topmost windows cannot guarantee visibility over exclusive fullscreen. Borderless/windowed mode is required.

### Guaranteed anti-cheat safety in multiplayer

External visualization can still be considered cheating. Check the game's terms and use built-in accessibility features where available.

## Recommended roadmap

1. Stabilize 5.1/7.1 endpoint capture and calibration.
2. Add event classification and game presets.
3. Add optional multi-peak display.
4. Prototype 2–4 target tracking with synthetic tests.
5. Add HRTF routing and mono-downmix accessibility workflow.
6. Consider ML only after a labeled dataset exists.

## Safety and ethics

Audio Radar is intended for accessibility, research, and single-player use. Multiplayer use may violate game rules. The project does not inject, but an external radar can still provide an information advantage.
