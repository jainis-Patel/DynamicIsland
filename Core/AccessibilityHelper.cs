using System.Windows;
using System.Windows.Automation.Peers;

namespace DynamicIsland.Core;

/// <summary>
/// Helpers for announcing dynamic status updates to Windows UI Automation / screen readers.
/// </summary>
internal static class AccessibilityHelper
{
    public static void AnnounceLiveRegion(UIElement? element)
    {
        if (element == null) return;
        try
        {
            var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch
        {
            // Ignore in headless or unparented visual trees
        }
    }
}
