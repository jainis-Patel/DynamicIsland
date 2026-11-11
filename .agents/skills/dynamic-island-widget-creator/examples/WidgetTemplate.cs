using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

/// <summary>
/// Reference template for creating high-performance Dynamic Island widgets.
/// Enforces zero-idle-CPU patterns, IDisposable lifecycle, and atomic JSON persistence.
/// </summary>
public sealed class WidgetTemplateService : IDisposable
{
    private readonly DispatcherTimer _pollTimer;
    private readonly string _storagePath;
    private bool _isExpanded;
    private bool _disposed;

    public ObservableCollection<string> Items { get; } = new();
    public event Action? StateUpdated;

    public WidgetTemplateService()
    {
        _storagePath = AppPaths.GetDataFilePath("widget_template.json");
        
        // Zero-idle-CPU rule: Background timer uses DispatcherPriority.Background
        // and only runs when _isExpanded is true.
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _pollTimer.Tick += OnPollTick;

        LoadPersistedData();
    }

    /// <summary>
    /// Invoked by IslandWindow.Widgets.cs when the user navigates into or away from this view.
    /// Halts background timers when the view is collapsed to keep idle CPU at 0.0%.
    /// </summary>
    public void SetExpanded(bool expanded)
    {
        if (_isExpanded == expanded) return;
        _isExpanded = expanded;

        if (_isExpanded)
        {
            _pollTimer.Start();
            RefreshNow();
        }
        else
        {
            _pollTimer.Stop();
        }
    }

    public void RefreshNow()
    {
        // Fetch or recalculate widget data safely on background thread or dispatcher
        StateUpdated?.Invoke();
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (!_isExpanded)
        {
            _pollTimer.Stop();
            return;
        }

        RefreshNow();
    }

    private void LoadPersistedData()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                // Load using AtomicJsonStore or System.Text.Json
            }
        }
        catch (Exception ex)
        {
            App.Log($"[WidgetTemplateService] Failed to load data: {ex.Message}");
        }
    }

    public void SavePersistedData()
    {
        try
        {
            // Use atomic write to avoid corruption during unexpected shutdowns
            AtomicJsonStore.SaveAtomic(_storagePath, Items);
        }
        catch (Exception ex)
        {
            App.Log($"[WidgetTemplateService] Failed to save data: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pollTimer.Stop();
        _pollTimer.Tick -= OnPollTick;
        SavePersistedData();
    }
}
