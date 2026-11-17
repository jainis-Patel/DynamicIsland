using System;
using System.IO;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Core;

/// <summary>
/// Ultra-low latency synthesized UI micro-haptics.
/// Plays zero-allocation in-memory audio micro-ticks for fluid tactile feedback
/// on card flick-dismissal, dock snapping, volume steps, and pill expansion.
/// Uses Win32 PlaySound with SND_MEMORY | SND_ASYNC for sub-millisecond execution
/// with 0% idle CPU and zero audio engine initialization overhead.
/// </summary>
public static class TactileFeedback
{
    private const int SampleRate = 44100;

    private static readonly byte[] _tickWav;
    private static readonly byte[] _snapWav;
    private static readonly byte[] _dismissWav;

    /// <summary>Global toggle for tactile audio micro-ticks.</summary>
    public static bool IsEnabled { get; set; } = true;

    static TactileFeedback()
    {
        _tickWav = SynthesizeRiffWav(freqStart: 520, freqEnd: 460, decay: 110.0, durationMs: 11, amplitude: 0.35);
        _snapWav = SynthesizeRiffWav(freqStart: 320, freqEnd: 240, decay: 75.0, durationMs: 16, amplitude: 0.42);
        _dismissWav = SynthesizeRiffWav(freqStart: 380, freqEnd: 190, decay: 60.0, durationMs: 22, amplitude: 0.45);
    }

    /// <summary>Plays a subtle, crisp 11ms micro-tick (e.g. volume/brightness increment or chevron expand).</summary>
    public static void PlayTick()
    {
        if (!IsEnabled) return;
        try
        {
            PlaySound(_tickWav, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
        }
        catch { }
    }

    /// <summary>Plays an organic 16ms tactile snap (e.g. spring settling into dock or pill click).</summary>
    public static void PlaySnap()
    {
        if (!IsEnabled) return;
        try
        {
            PlaySound(_snapWav, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
        }
        catch { }
    }

    /// <summary>Plays a fluid descending 22ms dismissal flick sound (e.g. swiping a notification away).</summary>
    public static void PlayDismiss()
    {
        if (!IsEnabled) return;
        try
        {
            PlaySound(_dismissWav, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
        }
        catch { }
    }

    /// <summary>
    /// Synthesizes a valid, self-contained 16-bit mono RIFF WAVE byte stream with exponential decay.
    /// </summary>
    internal static byte[] SynthesizeRiffWav(double freqStart, double freqEnd, double decay, int durationMs, double amplitude)
    {
        int numSamples = (int)(SampleRate * (durationMs / 1000.0));
        int dataBytes = numSamples * 2; // 16-bit mono = 2 bytes per sample
        int totalFileSize = 44 + dataBytes;

        byte[] wav = new byte[totalFileSize];
        using var ms = new MemoryStream(wav);
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write(new[] { 'R', 'I', 'F', 'F' });
        bw.Write(totalFileSize - 8);
        bw.Write(new[] { 'W', 'A', 'V', 'E' });

        // fmt chunk
        bw.Write(new[] { 'f', 'm', 't', ' ' });
        bw.Write(16);                 // Subchunk1Size (16 for PCM)
        bw.Write((short)1);            // AudioFormat (1 = PCM)
        bw.Write((short)1);            // NumChannels (1 = Mono)
        bw.Write(SampleRate);          // SampleRate
        bw.Write(SampleRate * 2);      // ByteRate (SampleRate * NumChannels * BitsPerSample/8)
        bw.Write((short)2);            // BlockAlign (NumChannels * BitsPerSample/8)
        bw.Write((short)16);           // BitsPerSample

        // data chunk
        bw.Write(new[] { 'd', 'a', 't', 'a' });
        bw.Write(dataBytes);

        // Synthesize audio waveform
        double phase = 0.0;
        for (int i = 0; i < numSamples; i++)
        {
            double progress = (double)i / numSamples;
            double t = (double)i / SampleRate;

            // Frequency chirp from freqStart to freqEnd
            double currentFreq = freqStart + (freqEnd - freqStart) * progress;
            phase += 2.0 * Math.PI * currentFreq / SampleRate;

            // Exponential decay envelope
            double envelope = Math.Exp(-decay * t);
            double sample = Math.Sin(phase) * envelope * amplitude;
            sample = Math.Clamp(sample, -1.0, 1.0);

            short pcm = (short)(sample * short.MaxValue);
            bw.Write(pcm);
        }

        bw.Flush();
        return wav;
    }
}
