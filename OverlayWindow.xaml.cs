using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ColonyTracker.Models;
using ColonyTracker.ViewModels;

namespace ColonyTracker;

/// <summary>
/// Transparent, always-on-top HUD showing live "still needed" amounts, meant to sit over the
/// game window while hauling. Drag-to-move and the corner resize grip are implemented manually
/// (AllowsTransparency=True disables the normal WindowChrome resize border), and locking makes
/// the window click-through via the WS_EX_TRANSPARENT extended style so it doesn't block mouse
/// input to the game underneath - since a click-through window can't be clicked, unlocking has
/// to happen from the main window instead of a button on the overlay itself.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private readonly AppSettings _settings;
    private readonly MainViewModel _viewModel;
    private readonly List<CommodityRowViewModel> _subscribedCommodityRows = [];
    private Point? _resizeStart;
    private Size _resizeStartSize;
    private double _scale = 1.0;

    public bool IsLocked { get; private set; }

    public OverlayWindow(MainViewModel viewModel, AppSettings settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _settings = settings;

        Width = settings.OverlayWidth;
        Height = settings.OverlayHeight;
        if (settings.OverlayLeft is { } left && settings.OverlayTop is { } top)
        {
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        SetScale(settings.OverlayScale);
        IsLocked = settings.OverlayLocked;
        UpdateControlsVisibility();
        ApplyAppearance(settings);

        RebuildCommodityView();

        Closed += (_, _) =>
        {
            SaveBounds();
            TeardownCommodityViewSubscriptions();
        };
    }

    /// <summary>
    /// (Re)builds the grouped commodity view from scratch and reassigns it as CommodityList's
    /// ItemsSource, discarding whatever CollectionViewSource/ICollectionView existed before.
    ///
    /// Two-level grouping: hub system as the outer header, station within that system as the
    /// inner header (so e.g. Celsius Gateway and Hadid City both nest under one "LTT 1873" header
    /// rather than each getting their own top-level entry), showing only rows that are still
    /// needed and actually have a known hub source - a commodity nobody's tracked hub stock
    /// currently covers has no station to tell the user to visit, so it's filtered out entirely
    /// rather than shown grouped under "unknown source".
    ///
    /// Called again (full rebuild, not just Refresh()) whenever a row's IsNeeded/BestSource*
    /// changes, or CommodityRows itself gains/loses rows. Two lighter approaches were tried and
    /// both failed real playtesting: (1) relying on WPF's ICollectionViewLiveShaping
    /// (IsLiveFiltering/IsLiveGrouping) alone didn't remove a zeroed-out row from this GROUPED view
    /// at all - it lingered until the overlay was closed and reopened, even though plain property
    /// bindings (the numbers) kept updating live. (2) Calling view.Refresh() on the SAME
    /// ICollectionView instance instead - while removing live shaping to avoid the two mechanisms
    /// fighting each other - still didn't reliably work: real testing showed reducing any commodity
    /// to zero would wrongly un-hide every OTHER already-zero row, i.e. Refresh() alone doesn't
    /// correctly re-settle this view's internal group membership either. The one thing confirmed to
    /// reliably work was manually closing and reopening the overlay - which recreates the
    /// CollectionViewSource/ItemsSource from nothing - so this reproduces exactly that, minus
    /// actually closing the window.
    /// </summary>
    private void RebuildCommodityView()
    {
        TeardownCommodityViewSubscriptions();

        var cvs = new CollectionViewSource { Source = _viewModel.CommodityRows };
        cvs.Filter += (_, e) => e.Accepted = e.Item is CommodityRowViewModel { IsNeeded: true, BestSourceStation: not null };
        cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommodityRowViewModel.BestSourceSystem)));
        cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommodityRowViewModel.BestSourceStation)));
        cvs.SortDescriptions.Add(new SortDescription(nameof(CommodityRowViewModel.BestSourceSystem), ListSortDirection.Ascending));
        cvs.SortDescriptions.Add(new SortDescription(nameof(CommodityRowViewModel.BestSourceStation), ListSortDirection.Ascending));
        cvs.SortDescriptions.Add(new SortDescription(nameof(CommodityRowViewModel.Name), ListSortDirection.Ascending));

        CommodityList.ItemsSource = cvs.View;

        foreach (var row in _viewModel.CommodityRows)
        {
            row.PropertyChanged += OnCommodityRowChanged;
            _subscribedCommodityRows.Add(row);
        }
        _viewModel.CommodityRows.CollectionChanged += OnCommodityRowsCollectionChanged;
    }

    private void OnCommodityRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CommodityRowViewModel.IsNeeded)
            or nameof(CommodityRowViewModel.BestSourceStation)
            or nameof(CommodityRowViewModel.BestSourceSystem))
            RebuildCommodityView();
    }

    private void OnCommodityRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildCommodityView(); // membership itself changed (project switch, slot add/delete) - same full rebuild covers it.

    /// <summary>Unsubscribes from every currently-tracked row plus the collection itself - called
    /// before every rebuild (so a row's old subscription isn't duplicated) and once more when this
    /// overlay instance closes (it's recreated fresh each time the overlay is shown, and
    /// CommodityRows' own rows outlive any single overlay instance, so without this every past
    /// overlay would stay pinned in memory by its old subscriptions).</summary>
    private void TeardownCommodityViewSubscriptions()
    {
        foreach (var row in _subscribedCommodityRows)
            row.PropertyChanged -= OnCommodityRowChanged;
        _subscribedCommodityRows.Clear();
        _viewModel.CommodityRows.CollectionChanged -= OnCommodityRowsCollectionChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyClickThrough(IsLocked);
    }

    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        _settings.OverlayLocked = locked;
        UpdateControlsVisibility();
        ApplyClickThrough(locked);
    }

    private void ApplyClickThrough(bool enabled)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return; // window not yet shown; OnSourceInitialized re-applies once it is

        var style = GetWindowLong(handle, GwlExStyle);
        style = enabled ? style | WsExTransparent | WsExLayered : style & ~WsExTransparent;
        SetWindowLong(handle, GwlExStyle, style);
    }

    private void UpdateControlsVisibility()
    {
        var visibility = IsLocked ? Visibility.Collapsed : Visibility.Visible;
        Controls.Visibility = visibility;
        ResizeGrip.Visibility = visibility;
    }

    private void SetScale(double scale)
    {
        _scale = Math.Clamp(scale, 0.5, 2.5);
        ContentScale.ScaleX = _scale;
        ContentScale.ScaleY = _scale;
        _settings.OverlayScale = _scale;
    }

    /// <summary>Applies the overlay's own customizable colors/background-transparency - separate
    /// from the rest of the app's fixed theme, since the overlay sits over the game and users may
    /// want it to stand out (or blend in) differently. Callable again after Settings is saved, not
    /// just at startup, so an already-open overlay updates live instead of needing a reopen.
    /// OverlayOpacity intentionally never touches Window.Opacity - that would fade the text/border/
    /// buttons along with the background, which is exactly what this is meant to avoid. Instead it
    /// only sets the alpha channel of the background brush itself; every other brush stays fully
    /// opaque regardless of the opacity setting.</summary>
    public void ApplyAppearance(AppSettings settings)
    {
        Resources["OverlayBackground"] = ParseBrush(settings.OverlayBackgroundColor, "#0A0705", settings.OverlayOpacity);
        Resources["OverlayBorder"] = ParseBrush(settings.OverlayBorderColor, "#FF7500");
        Resources["OverlayHeader"] = ParseBrush(settings.OverlayHeaderColor, "#FF7500");
        Resources["OverlayText"] = ParseBrush(settings.OverlayTextColor, "#EAE4DA");
        Resources["OverlayMuted"] = ParseBrush(settings.OverlayMutedColor, "#9C7A55");
        Resources["OverlayButton"] = ParseBrush(settings.OverlayButtonColor, "#FF7500");
        Resources["OverlayInactiveButton"] = ParseBrush(settings.OverlayInactiveButtonColor, "#5A3A15");
        Resources["OverlayButtonText"] = ParseBrush(settings.OverlayButtonTextColor, "#000000");
    }

    /// <param name="opacity">If given, overrides the parsed color's own alpha with this fraction
    /// (0.2-1.0) instead of using whatever alpha the hex string itself carries.</param>
    private static SolidColorBrush ParseBrush(string hex, string fallbackHex, double? opacity = null)
    {
        Color color;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            color = (Color)ColorConverter.ConvertFromString(fallbackHex);
        }
        if (opacity is { } o)
            color.A = (byte)Math.Round(Math.Clamp(o, 0.2, 1.0) * 255);
        return new SolidColorBrush(color);
    }

    private void ScaleUp_Click(object sender, RoutedEventArgs e) => SetScale(_scale + 0.1);
    private void ScaleDown_Click(object sender, RoutedEventArgs e) => SetScale(_scale - 0.1);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void LoadingModeButton_Click(object sender, RoutedEventArgs e) => _viewModel.ShowUnloadingOverlay = false;
    private void UnloadingModeButton_Click(object sender, RoutedEventArgs e) => _viewModel.ShowUnloadingOverlay = true;

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsLocked) DragMove();
    }

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsLocked) return;
        _resizeStart = PointToScreen(Mouse.GetPosition(this));
        _resizeStartSize = new Size(Width, Height);
        ((UIElement)sender).CaptureMouse();
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_resizeStart is not { } start) return;
        var current = PointToScreen(Mouse.GetPosition(this));
        Width = Math.Max(MinWidth, _resizeStartSize.Width + (current.X - start.X));
        Height = Math.Max(MinHeight, _resizeStartSize.Height + (current.Y - start.Y));
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _resizeStart = null;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void SaveBounds()
    {
        _settings.OverlayLeft = Left;
        _settings.OverlayTop = Top;
        _settings.OverlayWidth = Width;
        _settings.OverlayHeight = Height;
    }
}
