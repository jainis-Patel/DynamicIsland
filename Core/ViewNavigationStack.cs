namespace DynamicIsland.Core;

/// <summary>
/// Bounded navigation back-stack for sub-views inside the Expanded Dynamic Island surface.
/// </summary>
internal sealed class ViewNavigationStack
{
    public const int MaxDepth = 8;
    private readonly List<ExpandedView> _items = new(MaxDepth);

    public int Count => _items.Count;

    public void Push(ExpandedView view)
    {
        if (_items.Count > 0 && _items[^1] == view) return;
        if (_items.Count >= MaxDepth)
        {
            _items.RemoveAt(0);
        }
        _items.Add(view);
    }

    public bool TryPop(out ExpandedView view)
    {
        if (_items.Count == 0)
        {
            view = ExpandedView.Media;
            return false;
        }
        int last = _items.Count - 1;
        view = _items[last];
        _items.RemoveAt(last);
        return true;
    }

    public void Clear() => _items.Clear();
}
