# WASAPI Audio Spectrum & Synced Lyrics Technical Reference

This reference covers low-level audio engineering details, FFT windowing, and LRC parsing in Dynamic Island.

---

## 1. WASAPI Loopback Initialization

Dynamic Island uses WASAPI in loopback mode, capturing the final rendered audio stream sent to the default endpoint:

```csharp
var capture = new WasapiLoopbackCapture();
capture.ShareMode = AudioClientShareMode.Shared;
capture.DataAvailable += OnAudioDataAvailable;
capture.RecordingStopped += OnAudioRecordingStopped;
capture.StartRecording();
```

### Safety Requirements:
1. Always unsubscribe `DataAvailable` and `RecordingStopped` in `Dispose()`.
2. Do not attempt `capture.StopRecording()` if the device was invalidated (it will throw COMException); call `capture.Dispose()` directly within a `try/catch`.

---

## 2. FFT Windowing & Frequency Binning

Incoming 32-bit float stereo PCM samples are downmixed to mono and passed through a Hann window before Fast Fourier Transform:

$$w(n) = 0.5 \left(1 - \cos\left(\frac{2\pi n}{N - 1}\right)\right)$$

Windowing reduces spectral leakage across FFT bins. The magnitude spectrum is mapped to 4 logarithmic bands:
* Bass: Normalized by factor $1.2$
* Low-Mids: Normalized by factor $1.0$
* High-Mids: Normalized by factor $1.5$
* Treble: Normalized by factor $2.0$

A smoothing falloff factor (attack: $0.85$, decay: $0.35$) is applied per frame so bars bounce up instantly on snare/bass hits and glide downward smoothly.

---

## 3. LRCLIB Title Normalization Rules

In `LyricsService.cs`, track titles often contain noisy suffixes that prevent exact database matches on LRCLIB:

```csharp
public static string NormalizeTrackTitle(string title)
{
    // Remove " - Remastered 2011", " (feat. Artist)", " [Official Audio]", etc.
    string cleaned = Regex.Replace(title, @"\s*[\(\[](feat\.|remastered|official|live|deluxe).*?[\)\]]", "", RegexOptions.IgnoreCase);
    int dashIndex = cleaned.IndexOf(" - ");
    if (dashIndex > 0)
    {
        cleaned = cleaned.Substring(0, dashIndex);
    }
    return cleaned.Trim();
}
```
