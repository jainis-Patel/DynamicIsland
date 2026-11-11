using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Metadata;
using Windows.Storage;

namespace DynamicIsland.Services;

[ComImport]
[System.Runtime.InteropServices.Guid("3a3dcd6c-3eab-43dc-bcde-45671ce800c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDataTransferManagerInterop
{
    IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
    void ShowShareUIForWindow([In] IntPtr appWindow);
}

/// <summary>
/// Bridges Win32 HWND with Windows 10/11 modern Share Sheet (Nearby Share, Phone Link, Mail, WhatsApp).
/// Allows sharing any files from Drop Shelf or text from Clipboard to surrounding devices.
/// </summary>
internal sealed class WindowsShareService : IDisposable
{
    private static readonly Guid IDataTransferManagerGuid = new("a5caee9b-8708-49d1-8d36-67d25a8da00e");

    private readonly Func<IntPtr> _hwndProvider;
    private DataTransferManager? _transferManager;
    private IDataTransferManagerInterop? _interop;

    private string _shareTitle = "Share from Dynamic Island";
    private string? _shareText;
    private IReadOnlyList<string>? _shareFiles;
    private bool _disposed;

    public WindowsShareService(Func<IntPtr> hwndProvider)
    {
        _hwndProvider = hwndProvider;
    }

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        [In] ref Guid iid,
        out IntPtr factory);

    public void Initialize()
    {
        if (_disposed || _transferManager != null) return;

        try
        {
            IntPtr hwnd = _hwndProvider();
            if (hwnd == IntPtr.Zero) return;

            if (!ApiInformation.IsTypePresent("Windows.ApplicationModel.DataTransfer.DataTransferManager"))
            {
                return;
            }

            var interopGuid = typeof(IDataTransferManagerInterop).GUID;
            int hr = RoGetActivationFactory("Windows.ApplicationModel.DataTransfer.DataTransferManager", ref interopGuid, out IntPtr pFactory);
            if (hr == 0 && pFactory != IntPtr.Zero)
            {
                _interop = (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(pFactory);
                var dtmGuid = IDataTransferManagerGuid;
                IntPtr pDtm = _interop.GetForWindow(hwnd, ref dtmGuid);
                if (pDtm != IntPtr.Zero)
                {
                    _transferManager = Marshal.GetObjectForIUnknown(pDtm) as DataTransferManager;
                    if (_transferManager != null)
                    {
                        _transferManager.DataRequested += OnDataRequested;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[WindowsShareService] Initialize failed: {ex.Message}");
        }
    }

    private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        try
        {
            var req = args.Request;
            req.Data.Properties.Title = _shareTitle;

            if (!string.IsNullOrWhiteSpace(_shareText))
            {
                req.Data.SetText(_shareText);
            }

            if (_shareFiles != null && _shareFiles.Count > 0)
            {
                var items = new List<IStorageItem>();
                foreach (var path in _shareFiles)
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            var sf = StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask().GetAwaiter().GetResult();
                            if (sf != null) items.Add(sf);
                        }
                        catch { }
                    }
                }

                if (items.Count > 0)
                {
                    req.Data.SetStorageItems(items);
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[WindowsShareService] DataRequested error: {ex.Message}");
        }
    }

    public bool ShareFiles(IEnumerable<string> filePaths, string? title = null)
    {
        var list = filePaths.Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).ToList();
        if (list.Count == 0) return false;

        _shareFiles = list;
        _shareText = null;
        _shareTitle = title ?? (list.Count == 1 ? Path.GetFileName(list[0]) : $"{list.Count} Files from Shelf");

        return ShowShareUI();
    }

    public bool ShareText(string text, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        _shareFiles = null;
        _shareText = text;
        _shareTitle = title ?? "Text from Dynamic Island";

        return ShowShareUI();
    }

    private bool ShowShareUI()
    {
        IntPtr hwnd = _hwndProvider();
        if (hwnd == IntPtr.Zero) return false;

        Initialize();

        try
        {
            if (_interop != null)
            {
                _interop.ShowShareUIForWindow(hwnd);
                return true;
            }

            DataTransferManager.ShowShareUI();
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[WindowsShareService] ShowShareUI failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_transferManager != null)
        {
            _transferManager.DataRequested -= OnDataRequested;
            _transferManager = null;
        }
        _interop = null;
    }
}
