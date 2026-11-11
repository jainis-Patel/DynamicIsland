# WPF Memory Leak Prevention & High-Performance Rendering Rules

This document outlines the hard rules for avoiding memory leaks, GC spikes, and thread stalls in Dynamic Island.

---

## 1. Event Handler Unsubscribing & Lifetime Management

WPF elements subscribed to long-lived singleton services will **never** be garbage collected if the subscription is not explicitly revoked.

### The Rule
* Any class that registers an event handler with a static service, `SettingsService`, `MediaSessionService`, or Win32 hook must implement `IDisposable` and detach in `Dispose()`.
* When using Lambda expressions:
  ```csharp
  // WRONG: Anonymous lambda cannot be unsubscribed!
  service.StateChanged += (_, _) => OnStateChanged();

  // CORRECT: Named method or stored action allows clean detachment
  service.StateChanged += OnServiceStateChanged;
  ```

---

## 2. WPF Freezable Freezing Rule

Every `Freezable` object in WPF (including `SolidColorBrush`, `LinearGradientBrush`, `TransformGroup`, `PathGeometry`, and `EasingFunctionBase`) exists in an unfrozen state by default. Unfrozen freezables:
1. Cannot be shared across threads.
2. Hook into WPF dependency property change notifications, consuming extra heap memory.
3. Cause thread lock contention during rendering.

### The Rule
* Call `.Freeze()` immediately after instantiating any brush, pen, or easing function that will not change.
* Example from `IslandWindow.Animation.cs`:
  ```csharp
  private static T CreateFrozenEase<T>(T ease) where T : Freezable
  {
      ease.Freeze();
      return ease;
  }
  ```

---

## 3. Image Bitmap Memory Bounding

Large screenshots or album artwork (such as 4K display captures or 3000x3000px Spotify art) will quickly consume hundreds of megabytes if loaded raw into memory.

### The Rule
* When storing image data in `ClipboardHistoryService.cs`, always downsample images:
  ```csharp
  var target = new TransformedBitmap(original, new ScaleTransform(scale, scale));
  target.Freeze();
  ```
* Cap maximum cached in-memory image dimensions to `512x512` pixels.

---

## 4. SkiaSharp / Lottie Memory Management

SkiaSharp surfaces allocate native (unmanaged) C++ Skia objects that .NET GC is unaware of.

### The Rule
* Dispose of `SKPaint`, `SKBitmap`, `SKCanvas`, and `SKManagedStream` instances explicitly with `using` blocks.
* In `VirtualPetControl.cs` and `ShriRamLottiePlayer.cs`, cache parsed animation structures rather than re-decoding JSON on every frame.
