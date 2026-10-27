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

    public event Action<bool>? InsideChanged;

    public CursorTracker(Func<RECT?> getZone)
    {
        _getZone = getZone;
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = FastInterval };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();

    private void OnTick(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out var p)) return;

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

        if (near != _fastMode)
        {
            _fastMode = near;
            _timer.Interval = near ? FastInterval : SlowInterval;
        }

        if (inside == _inside) return;
        _inside = inside;
        InsideChanged?.Invoke(inside);
    }

    public void Dispose() => _timer.Stop();
}
