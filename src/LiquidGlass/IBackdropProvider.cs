using System;
using System.Threading.Tasks;
using Vortice.Direct3D11;

namespace DynamicIsland.LiquidGlass;

/// <summary>
/// Abstraction for sourcing GPU backdrop textures (Desktop Capture Path B or In-App RenderTarget Path A).
/// Keeps all texture memory directly on the GPU without CPU readback copies.
/// </summary>
public interface IBackdropProvider : IDisposable
{
    /// <summary>Starts capturing frames asynchronously.</summary>
    Task StartAsync();

    /// <summary>Stops active frame capture.</summary>
    void Stop();

    /// <summary>Shader resource view for the current sharp backdrop frame on the GPU.</summary>
    ID3D11ShaderResourceView? CurrentFrameSRV { get; }

    /// <summary>Shader resource view for the pre-blurred / downsampled backdrop frame on the GPU.</summary>
    ID3D11ShaderResourceView? BlurredFrameSRV { get; }

    /// <summary>Fired when a new backdrop frame has arrived on the GPU.</summary>
    event EventHandler? FrameReady;

    /// <summary>Whether capture is currently active.</summary>
    bool IsCapturing { get; }

    /// <summary>Native width of the captured frame in physical pixels.</summary>
    int FrameWidth { get; }

    /// <summary>Native height of the captured frame in physical pixels.</summary>
    int FrameHeight { get; }
}
