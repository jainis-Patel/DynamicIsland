---
name: dynamic-island-wasapi-media
description: >-
  Use this skill when working on media session tracking (GSMTC / WinRT), WASAPI loopback
  audio spectrum FFT analysis, album artwork color extraction, or synchronized lyrics
  (LRCLIB). Covers thread-safe audio capture, handling device invalidation/unplugging
  (AUDCLNT_E_DEVICE_INVALIDATED), zero-allocation audio ring buffers, and SkiaSharp
  visualizer rendering.
---

# Dynamic Island WASAPI Audio & Media Reactivity

The Now Playing capsule, live audio equalizer, bass-reactive backlight halo, and synchronized lyrics are among the most highlighted features in Dynamic Island community discussions on Reddit. 

However, Windows real-time audio capture and media integration present notorious technical challenges:
* **WASAPI Device Invalidation**: Unplugging headphones, turning off Bluetooth headsets, or switching default outputs crashes unhandled capture sessions with `AUDCLNT_E_DEVICE_INVALIDATED (0x88890004)`.
* **Thread Marshalling Lag**: Audio buffers arrive at 100Hz–200Hz. Marshalling each buffer to the WPF UI thread causes catastrophic UI stutter.
* **Synchronized Lyrics Timing**: Aligning millisecond LRC timestamps with fluctuating media playback positions across Spotify, YouTube, and browser sessions.

Dynamic Island handles this through `Services/AudioSpectrum.cs`, `Services/MediaSessionService.cs`, and `Services/LyricsService.cs`.

---

## Architecture Flow

```
┌─────────────────────────────────┐
│ System Default Audio Endpoint   │
└───────────────┬─────────────────┘
                │ (WASAPI Loopback Capture)
                ▼
┌─────────────────────────────────┐
│   Services/AudioSpectrum.cs     │
│   • Lock-free FFT 4-band buffer │
│   • Auto-recovery on device loss│
└───────────────┬─────────────────┘
                │ (Atomic Read via Dispatcher Timer)
                ▼
┌─────────────────────────────────┐      ┌──────────────────────────────┐
│  SkiaSharp Waveform / Equalizer │ ◄─── │   Services/LyricsService.cs  │ (LRCLIB synced lyrics)
│  & Bass Reactive Halo Brush     │      └──────────────┬───────────────┘
└───────────────▲─────────────────┘                     │
                │                                       ▼
┌───────────────┴─────────────────┐      ┌──────────────────────────────┐
│  Services/ColorExtractor.cs     │ ◄─── │ Services/MediaSessionService │ (GSMTC WinRT manager)
│  • Vibrant Artwork Accent Hue   │      └──────────────────────────────┘
└─────────────────────────────────┘
```

---

## 1. WASAPI Loopback Capture & Lock-Free Ring Buffer

In [AudioSpectrum.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Services/AudioSpectrum.cs):
* Uses `WasapiLoopbackCapture` from NAudio to capture the mixed system output stream.
* Performs Fast Fourier Transform (FFT) on incoming PCM samples into 4 normalized frequency bands:
  * **Sub-Bass / Bass**: $20\text{ Hz} - 250\text{ Hz}$ (drives island pulse & backlight halo).
  * **Low-Mids**: $250\text{ Hz} - 1000\text{ Hz}$.
  * **High-Mids**: $1000\text{ Hz} - 4000\text{ Hz}$.
  * **Treble**: $4000\text{ Hz} - 16000\text{ Hz}$.
* **Lock-Free Reading**: Audio callbacks update a thread-safe `float[]` array using memory barriers or volatile reads. The UI thread samples this array on its own render tick, eliminating thread locking.

---

## 2. Handling Device Invalidation (`AUDCLNT_E_DEVICE_INVALIDATED`)

When a user disconnects Bluetooth headphones or switches the default audio device:
1. NAudio's `DataAvailable` or `RecordingStopped` fires with `COMException` containing HRESULT `0x88890004` or `0x8889000A`.
2. `AudioSpectrum.cs` catches this exception without crashing the app:
   ```csharp
   private void OnRecordingStopped(object? sender, StoppedEventArgs e)
   {
       if (_disposed) return;
       // Attempt non-blocking reconnection after short delay for device enumeration
       ScheduleAudioReconnect();
   }
   ```
3. The spectrum zero-fills the bands so the visualizer rests gracefully rather than freezing on the last frame.

---

## 3. Synchronized Lyrics Integration (`LRCLIB`)

In [LyricsService.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Services/LyricsService.cs):
* Queries the open-source [LRCLIB](https://lrclib.net/) database using track title, artist name, and duration fetched from WinRT GSMTC:
  ```text
  GET https://lrclib.net/api/get?track_name={title}&artist_name={artist}&duration={duration}
  ```
* Cleans extraneous remaster/feature tags from titles (e.g. `Song Name - Remastered 2021` -> `Song Name`).
* Parses standard LRC timestamp tags `[mm:ss.xx]` into sorted chronological lines.
* Computes active lyric index in $O(\log N)$ using binary search against the current media session playback position.

---

## 4. Vibrant Color Extraction

In [ColorExtractor.cs](file:///e:/Code%20&%20Development/DynamicIsland/DynamicIsland/Services/ColorExtractor.cs):
* Reads album artwork bytes from `IRandomAccessStreamWithContentType` provided by GSMTC.
* Downsamples the image to an $80 \times 80$ grid.
* Filters out muddy grays, pure whites, and deep blacks by inspecting HSV saturation ($S > 0.35$) and brightness ($0.20 < V < 0.90$).
* Identifies the dominant vibrant hue to tint the Dynamic Island backlight glow and seek progress bar.

---

## Detailed References

* [WASAPI & LRCLIB Deep Technical Reference](./references/wasapi-and-lyrics.md)
