using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Pure, zero-allocation math engine that detects rapid horizontal mouse "jiggle" gestures
/// while the left mouse button is held down (e.g. during a file drag).
/// Requires at least <c>MinReversals</c> direction reversals within <c>WindowMs</c>, each exceeding <c>MinStrokePx</c>.
/// </summary>
public sealed class JiggleDetector
{
    public const int WindowMs = 520;
    public const int MinReversals = 3;
    public const int MinStrokePx = 38;
    public const int CooldownMs = 1500;

    private int _anchorX;
    private int _lastX;
    private int _direction; // -1 left, +1 right, 0 unknown
    private readonly long[] _reversalTimestamps = new long[8];
    private int _reversalCount;
    private long _lastTriggerMs = -10_000;

    public void Reset()
    {
        _direction = 0;
        _reversalCount = 0;
    }

    public bool ProcessMove(int x, int y, long timestampMs, bool isLeftButtonDown) =>
        FeedSample(x, y, timestampMs, isLeftButtonDown);

    public bool FeedSample(int x, int y, long timestampMs, bool isLeftButtonDown)
    {
        if (!isLeftButtonDown)
        {
            Reset();
            return false;
        }

        if (timestampMs - _lastTriggerMs < CooldownMs)
        {
            return false;
        }

        int prevX = _lastX;
        int dxFromLast = x - prevX;
        _lastX = x;

        if (_direction == 0)
        {
            _anchorX = x;
            if (Math.Abs(dxFromLast) >= 2)
            {
                _direction = dxFromLast > 0 ? 1 : -1;
                _anchorX = prevX;
            }
            return false;
        }

        if (Math.Abs(dxFromLast) < 2)
        {
            return false;
        }

        int currentDir = dxFromLast > 0 ? 1 : -1;

        if (currentDir != _direction)
        {
            int completedStrokeSpan = Math.Abs(prevX - _anchorX);
            if (completedStrokeSpan >= MinStrokePx)
            {
                _direction = currentDir;
                _anchorX = prevX;
                RecordReversal(timestampMs);

                int valid = 0;
                for (int i = 0; i < _reversalCount; i++)
                {
                    if (timestampMs - _reversalTimestamps[i] <= WindowMs)
                    {
                        valid++;
                    }
                }

                if (valid >= MinReversals)
                {
                    _lastTriggerMs = timestampMs;
                    Reset();
                    return true;
                }
            }
            else
            {
                _direction = currentDir;
                _anchorX = prevX;
            }
        }

        return false;
    }

    private void RecordReversal(long timestampMs)
    {
        if (_reversalCount < _reversalTimestamps.Length)
        {
            _reversalTimestamps[_reversalCount++] = timestampMs;
        }
        else
        {
            Array.Copy(_reversalTimestamps, 1, _reversalTimestamps, 0, _reversalTimestamps.Length - 1);
            _reversalTimestamps[^1] = timestampMs;
        }
    }
}

/// <summary>
/// Manages the low-level mouse jiggle hook and floating drop baskets across all monitors.
/// Hook is strictly installed ONLY when the "jiggle_basket" droplet is enabled, and the callback
/// exits in nanoseconds whenever <c>VK_LBUTTON</c> is not pressed.
/// </summary>
internal sealed class JiggleBasketService : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly DropShelfService _shelfService;
    private readonly Func<bool>? _isEnabledPredicate;
    private readonly JiggleDetector _detector = new();
    private readonly List<Views.BasketWindow> _openBaskets = new();
    private static readonly uint CurrentProcessId = (uint)Environment.ProcessId;
    private IntPtr _mouseHook;
    private LowLevelMouseProc? _mouseProc;
    private bool _isEnabled;
    private bool _disposed;

    public event Action<POINT>? JiggleDetected;
    public event Action<int>? BasketsCountChanged;

    public int OpenBasketCount => _openBaskets.Count;

    public JiggleBasketService(Dispatcher ui, DropShelfService shelfService, Func<bool>? isEnabledPredicate = null)
    {
        _ui = ui;
        _shelfService = shelfService;
        _isEnabledPredicate = isEnabledPredicate;
    }

    public bool IsEnabled => _isEnabled;

    public void Start() => SetEnabled(_isEnabledPredicate?.Invoke() ?? true);

    public void SetEnabled(bool enabled)
    {
        if (_disposed || _isEnabled == enabled) return;
        _isEnabled = enabled;

        if (enabled)
        {
            InstallHook();
        }
        else
        {
            UninstallHook();
            CloseAllBaskets();
        }
    }

    private void InstallHook()
    {
        if (_mouseHook != IntPtr.Zero) return;
        try
        {
            _mouseProc = MouseHookCallback;
            IntPtr hMod = GetModuleHandle(null);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        }
        catch (Exception ex)
        {
            App.Log($"[JiggleBasket] Failed to install WH_MOUSE_LL hook: {ex.Message}");
        }
    }

    private void UninstallHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(_mouseHook); } catch { }
            _mouseHook = IntPtr.Zero;
        }
        _detector.Reset();
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isEnabled)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_MOUSEMOVE)
            {
                // Fast exit when Left Mouse Button is not physically held down (zero overhead during normal mouse movement)
                bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                if (!leftDown)
                {
                    _detector.Reset();
                    return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                long nowMs = Environment.TickCount64;

                if (_detector.FeedSample(info.pt.X, info.pt.Y, nowMs, isLeftButtonDown: true))
                {
                    // Check window ownership only once when a full jiggle gesture completes
                    IntPtr targetHwnd = WindowFromPoint(info.pt);
                    if (targetHwnd != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(targetHwnd, out uint targetProcId);
                        if (targetProcId == CurrentProcessId)
                        {
                            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                        }
                    }

                    var pt = info.pt;
                    _ui.InvokeAsync(() =>
                    {
                        SummonBasketAt(pt.X, pt.Y);
                        JiggleDetected?.Invoke(pt);
                    }, DispatcherPriority.Input);
                }
            }
            else if (msg == WM_LBUTTONUP)
            {
                _detector.Reset();
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    public Views.BasketWindow? SummonBasketAtCursor(bool force = false)
    {
        if (GetCursorPos(out var pt))
        {
            return SummonBasketAt(pt.X, pt.Y);
        }
        return null;
    }

    public Views.BasketWindow SummonBasketAt(int physicalX, int physicalY)
    {
        // Reuse an existing empty basket if one is already open nearby, or spawn a new multi-basket instance (up to 4)
        var existingEmpty = _openBaskets.FirstOrDefault(b => b.IsLoaded && b.ItemCount == 0);
        if (existingEmpty != null)
        {
            existingEmpty.MoveNearPhysicalPoint(physicalX, physicalY);
            existingEmpty.ActivatePulse();
            return existingEmpty;
        }

        if (_openBaskets.Count >= 4)
        {
            var top = _openBaskets[0];
            top.ActivatePulse();
            return top;
        }

        var basket = new Views.BasketWindow(_shelfService);
        basket.Closed += (_, _) =>
        {
            _openBaskets.Remove(basket);
            BasketsCountChanged?.Invoke(_openBaskets.Count);
        };
        _openBaskets.Add(basket);
        basket.ShowAtPhysicalPoint(physicalX, physicalY);
        BasketsCountChanged?.Invoke(_openBaskets.Count);
        return basket;
    }

    public void CloseAllBaskets()
    {
        foreach (var b in _openBaskets.ToList())
        {
            try { b.Close(); } catch { }
        }
        _openBaskets.Clear();
        BasketsCountChanged?.Invoke(0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UninstallHook();
        CloseAllBaskets();
    }
}
