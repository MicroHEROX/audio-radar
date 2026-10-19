# Glossary

| Term | Meaning |
|---|---|
| **Audio Radar** | The visual direction display produced by this project. |
| **Endpoint** | A Windows audio render device, such as HDMI, USB speakers, or headphones. |
| **Default endpoint** | The render endpoint Windows currently uses for the console role. |
| **WASAPI** | Windows Audio Session API. |
| **Loopback** | Capturing the audio that Windows sends to a render endpoint. |
| **Shared mode** | The normal Windows audio mode where multiple applications mix into one endpoint stream. |
| **Exclusive mode** | An application takes ownership of the endpoint; not used by this project. |
| **WAVEFORMATEXTENSIBLE** | A Windows audio format structure that carries a channel mask and sub-format. |
| **Channel mask** | Bit mask describing which speaker positions are present. |
| **Speaker layout** | Mapping of channels to positions such as FL, FR, FC, LFE, BL, BR, SL, SR, TFL, TFR, TBL, TBR. |
| **LFE** | Low-frequency effects channel; excluded from direction estimation. |
| **Azimuth** | Horizontal angle, measured clockwise from front. |
| **Elevation** | Angle above the horizontal plane. |
| **HRTF** | Head-related transfer function; binaural filtering used by headphone spatial audio. |
| **VBAP** | Vector base amplitude panning; a speaker-panning method. |
| **Dolby Atmos** | Object-based audio format. Home theater output can use a PCM bed plus MAT metadata. |
| **MAT** | Dolby metadata-enhanced audio transport; can carry Atmos objects. |
| **Object audio** | Audio with positional metadata rather than only fixed channels. |
| **PCM bed** | The fixed-channel mix (for example 7.1) that accompanies object metadata. |
| **Cycle-through / merge** | When two estimated directions occupy the same channel/band, the radar cannot separate them. |
| **Track** | A time-associated target hypothesis with state and identity. 1.0.0 does not implement full multi-target tracking. |
| **Confidence** | A heuristic score for how coherent the current direction estimate is. |
| **Transient** | A short, sudden sound such as a footstep, gunshot, or reload. |
| **TopMost** | A Windows window style that keeps a window above normal windows. |
| **Window band** | An internal Windows Z-order group above the normal desktop band. |
| **UIAccess** | A Windows accessibility privilege required by some higher window bands. |
| **RTSS** | RivaTuner Statistics Server, often used for in-game FPS/OSD overlays. |
| **DSH** | DeepSeek Harness. |
