using System.Windows.Threading;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Core;

/// <summary>
/// Tracks the global cursor position and reports when it enters/leaves the island's hot zone
/// (in physical screen pixels). Uses distance-adaptive polling: ~60 Hz (16 ms) when near the pill
/// for instant responsiveness, and ~13 Hz (75 ms) when far away to cut idle CPU wakeups by ~80%.
/// </summary>
internal sealed class CursorTracker : IDisposable
{
    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SlowInterval = TimeSpan.FromMilliseconds(75);
    private const int ProximityXMarginPx = 220;
    private const int ProximityYMarginPx = 150;

    private readonly DispatcherTimer _timer;
    private readonly Func<RECT?> _getZone;
    private bool _inside;
    private bool _fastMode = true;
    private IntPtr _lastMonitor;
    private IntPtr _candidateMonitor;
    private long _candidateSinceTick;
    private POINT _lastPos;
    private long _lastPosTick;
    private double _velocityX;

    public bool EnableFastSwipeGuard { get; set; } = true;
    public double CurrentVelocityX => _velocityX;

    public event Action<bool>? InsideChanged;
    public event Action<IntPtr>? MonitorChanged;

    public CursorTracker(Func<RECT?> getZone)
    {
        _getZone = getZone;
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = FastInterval };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();

    public void SetSuspended(bool suspended)
    {
        if (suspended)
        {
            _timer.Stop();
            if (_inside)
            {
                _inside = false;
                InsideChanged?.Invoke(false);
            }
        }
        else if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out var p)) return;

        long now = Environment.TickCount64;
        long dtMs = now - _lastPosTick;
        if (dtMs > 0 && dtMs < 500)
        {
            double dx = Math.Abs(p.X - _lastPos.X);
            _velocityX = (dx / dtMs) * 1000.0;
        }
        else
        {
            _velocityX = 0.0;
        }
        _lastPos = p;
        _lastPosTick = now;

        bool inside = false;
        bool near = false;

        if (_getZone() is { } zone)
        {
            inside = zone.Contains(p);
            near = inside ||
                   (p.X >= zone.Left - ProximityXMarginPx &&
                    p.X <= zone.Right + ProximityXMarginPx &&
                    p.Y >= zone.Top - 32 &&
                    p.Y <= zone.Bottom + ProximityYMarginPx);
        }

        // Ignore transient fast swipe across top bezel (e.g. flicking across browser tabs)
        if (inside && !_inside && EnableFastSwipeGuard && _velocityX > 350.0)
        {
            inside = false;
        }

        if (near != _fastMode)
        {
            _fastMode = near;
            _timer.Interval = near ? FastInterval : SlowInterval;
        }

        IntPtr currentMon = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
        if (currentMon != IntPtr.Zero)
        {
            now = Environment.TickCount64;
            if (_lastMonitor == IntPtr.Zero)
            {
                _lastMonitor = currentMon;
                _candidateMonitor = currentMon;
                _candidateSinceTick = now;
            }
            else if (currentMon != _lastMonitor)
            {
                if (currentMon != _candidateMonitor)
                {
                    _candidateMonitor = currentMon;
                    _candidateSinceTick = now;
                }
                else if (now - _candidateSinceTick > 300)
                {
                    _lastMonitor = currentMon;
                    _candidateSinceTick = now;
                    MonitorChanged?.Invoke(currentMon);
                }
            }
            else
            {
                _candidateMonitor = currentMon;
                _candidateSinceTick = now;
            }
        }

        if (inside == _inside) return;
        _inside = inside;
        InsideChanged?.Invoke(inside);
    }

    public void Dispose() => _timer.Stop();
}
