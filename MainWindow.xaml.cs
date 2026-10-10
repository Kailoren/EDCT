using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ColonyTracker.Models;
using ColonyTracker.Services;
using ColonyTracker.ViewModels;

namespace ColonyTracker;

public partial class MainWindow : Window
{
    private const int HotkeyIdOverlayToggle = 1;
    private const int HotkeyIdOverlayLock = 2;
    private const int HotkeyIdOverlayModeToggle = 3;

    private MainViewModel? _viewModel;
    private OverlayWindow? _overlayWindow;
    private Point _slotDragStartPoint;
    private SlotRowViewModel? _slotDragCandidate;
    private SlotRowViewModel? _lastSlotDragOverTarget;
    private IReadOnlyDictionary<string, RavenColonialBuildType> _ravenColonialBuildTypes = new Dictionary<string, RavenColonialBuildType>();
    private readonly GlobalHotkeyService _hotkeys;

    // DataGridColumn has no Name property of its own (it's not a FrameworkElement), so each
    // column's x:Name only exists as a code-behind field - a settings key has to be paired with
    // it explicitly here to persist widths.
    private readonly (string Key, DataGridColumn Column)[] _resizableColumns;

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateMaximizeRestoreGlyph();
        UpdateMaximizeRestoreGlyph();
        _hotkeys = new GlobalHotkeyService(this);

        _resizableColumns =
        [
            ("Layout.Body", LayoutBodyColumn),
            ("Layout.SlotNumber", LayoutSlotNumberColumn),
            ("Layout.Name", LayoutNameColumn),
            ("Layout.StructureType", LayoutStructureTypeColumn),
            ("Layout.StationType", LayoutStationTypeColumn),
            ("Layout.Notes", LayoutNotesColumn),
            ("Layout.Active", LayoutActiveColumn),
            ("Layout.Completed", LayoutCompletedColumn),
            ("Cargo.Category", CargoCategoryColumn),
            ("Cargo.Commodity", CargoCommodityColumn),
            ("Cargo.Required", CargoRequiredColumn),
            ("Cargo.Carrier", CargoCarrierColumn),
            ("Cargo.Ship", CargoShipColumn),
            ("Cargo.Delivered", CargoDeliveredColumn),
            ("Cargo.StillNeeded", CargoStillNeededColumn),
            ("Cargo.BestSource", CargoBestSourceColumn),
            ("Cargo.HubSystem", CargoHubSystemColumn),
            ("Cargo.HubStock", CargoHubStockColumn),
            ("Cargo.HubPrice", CargoHubPriceColumn)
        ];
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppSettings settings;
        StructureCatalog catalog;
        IReadOnlyDictionary<string, List<string>> layoutVariantCatalog;
        List<ColonyProject> projects;
        try
        {
            settings = SettingsStore.Load();
            catalog = StructureCatalogService.Load();
            layoutVariantCatalog = LayoutVariantCatalogService.Load();
            _ravenColonialBuildTypes = RavenColonialBuildTypeCatalogService.Load();
            projects = ProjectStore.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't load ColonyTracker's data files:\n\n{ex.Message}\n\nMake sure the Data folder was published alongside the exe.",
                "ColonyTracker", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        if (SettingsStore.LastLoadFailed || ProjectStore.LastLoadFailed)
        {
            var what = SettingsStore.LastLoadFailed && ProjectStore.LastLoadFailed ? "settings.json and projects.json"
                : SettingsStore.LastLoadFailed ? "settings.json" : "projects.json";
            MessageBox.Show(this,
                $"Couldn't read your saved {what} - it looked corrupted, so ColonyTracker started fresh instead of overwriting it. The original file was kept alongside it with a \".corrupt-<timestamp>\" suffix in case you want to recover anything from it.",
                "ColonyTracker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (settings.WindowWidth is > 0 && settings.WindowHeight is > 0)
        {
            Left = settings.WindowLeft ?? Left;
            Top = settings.WindowTop ?? Top;
            Width = settings.WindowWidth.Value;
            Height = settings.WindowHeight.Value;
        }
        if (settings.WindowMaximized)
            WindowState = WindowState.Maximized;

        RestoreColumnWidths(settings);

        _viewModel = new MainViewModel(settings, catalog, layoutVariantCatalog, projects);
        DataContext = _viewModel;

        RegisterConfiguredHotkeys();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeys.Dispose();
        if (_viewModel is null) return;

        // Not owned by this window (it must stay visible even if MainWindow gets minimized while
        // the user is in-game), so it needs to be closed explicitly rather than relying on
        // WPF's owned-window auto-close.
        _overlayWindow?.Close();

        _viewModel.BakeInCarrierDeltasBeforeExit();
        ProjectStore.Save(_viewModel.Projects.Select(p => p.Model).ToList());

        var settings = _viewModel.Settings;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
        }
        SaveColumnWidths(settings);
        SettingsStore.Save(settings);

        _viewModel.Dispose();
    }

    /// <summary>Clicking blank space anywhere (a Border, a TextBlock, empty Grid area, ...) doesn't
    /// move keyboard focus by itself, since those elements aren't focusable - so a focused TextBox
    /// (e.g. the System name box) kept its caret/highlight until the user happened to click another
    /// focusable control. Real user testing showed Keyboard.Focus(this) (the Window) doesn't actually
    /// dislodge focus from the descendant TextBox - Window isn't a reliable focus sink the way a
    /// plain FrameworkElement is - so this clears focus outright instead, which does. Runs on every
    /// PreviewMouseDown, tunnelling in before the actual click target's own focus handling, so
    /// clicking a real input control (another TextBox, a Button, the DataGrid, ...) still re-focuses
    /// that control immediately after - this only "sticks" on clicks that don't land on anything
    /// focusable.</summary>
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) => Keyboard.ClearFocus();

    private void RestoreColumnWidths(AppSettings settings)
    {
        foreach (var (key, column) in _resizableColumns)
        {
            if (settings.ColumnWidths.TryGetValue(key, out var width) && width > 0)
                column.Width = new DataGridLength(width);
        }
    }

    private void SaveColumnWidths(AppSettings settings)
    {
        foreach (var (key, column) in _resizableColumns)
            settings.ColumnWidths[key] = column.ActualWidth;
    }

    private void AddProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var dialog = new NewProjectDialog(_viewModel.StructureTypeNames, _viewModel.LayoutVariantCatalog, _ravenColonialBuildTypes) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
            _viewModel.AddProject(dialog.Result);
    }

    /// <summary>Click-to-copy the colony project's own system name, shown once in the Cargo Hauling
    /// tab's header bar.</summary>
    private void SystemNameCopyText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _ = CopyToClipboardWithFlash(_viewModel?.SelectedProject?.SystemName);
    }

    /// <summary>Click-to-copy the hub system a "Best Hub Source" station belongs to, shown per row
    /// in the Cargo Hauling grid's "Hub System" column - a plain (non-template) column here would
    /// let the click just select the DataGridRow instead, same idea as FleetFinder's but as a
    /// TextBlock with direct event handlers rather than a re-templated Button, to keep this free of
    /// Command/CanExecute/ControlTemplate indirection that could silently break.</summary>
    private void HubSystemText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // otherwise the click also selects the row underneath
        if ((sender as FrameworkElement)?.DataContext is not CommodityRowViewModel row) return;
        _ = CopyToClipboardWithFlash(row.BestSourceSystem);
    }

    private async Task CopyToClipboardWithFlash(string? value)
    {
        if (_viewModel is null || string.IsNullOrWhiteSpace(value)) return;
        try
        {
            Clipboard.SetText(value);
        }
        catch
        {
            return; // best effort - no clipboard, no confirmation flash either
        }

        _viewModel.SystemNameCopied = true;
        await Task.Delay(1500);
        _viewModel.SystemNameCopied = false;
    }

    private void CopyableText_MouseEnter(object sender, MouseEventArgs e) =>
        ((TextBlock)sender).TextDecorations = TextDecorations.Underline;

    private void CopyableText_MouseLeave(object sender, MouseEventArgs e) =>
        ((TextBlock)sender).TextDecorations = null;

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var window = new SettingsWindow(_viewModel.Settings) { Owner = this };
        if (window.ShowDialog() == true)
        {
            SettingsStore.Save(_viewModel.Settings);
            _viewModel.OnSettingsChanged();
            ApplyOverlayAppearance();
        }
    }

    /// <summary>Swaps the maximize/restore button's glyph to match WindowState - there's one button
    /// (double-click-to-maximize convention), not two.</summary>
    private void UpdateMaximizeRestoreGlyph()
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        MaximizeRestoreButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    private void OverlayToggleButton_Click(object sender, RoutedEventArgs e) => ToggleOverlay();

    /// <summary>Shows or hides the overlay - also called from SettingsWindow's "Toggle Overlay"
    /// button, so the user can preview appearance changes without leaving Settings.</summary>
    public void ToggleOverlay()
    {
        if (_viewModel is null) return;

        if (_overlayWindow is not null)
        {
            _overlayWindow.Close();
            return;
        }

        _overlayWindow = new OverlayWindow(_viewModel, _viewModel.Settings);
        _overlayWindow.Closed += (_, _) =>
        {
            _overlayWindow = null;
            OverlayToggleButton.Content = "Show Overlay";
            OverlayLockCheckBox.Visibility = Visibility.Collapsed;
        };
        _overlayWindow.Show();

        OverlayToggleButton.Content = "Hide Overlay";
        OverlayLockCheckBox.IsChecked = _viewModel.Settings.OverlayLocked;
        OverlayLockCheckBox.Visibility = Visibility.Visible;
    }

    /// <summary>Pushes the current (possibly not-yet-saved-to-disk) overlay appearance settings
    /// into the overlay if it's currently open - called from SettingsWindow's "Apply" button for a
    /// live preview loop, and again after a real Save.</summary>
    public void ApplyOverlayAppearance()
    {
        if (_viewModel is not null) _overlayWindow?.ApplyAppearance(_viewModel.Settings);
    }

    private void OverlayLockCheckBox_Click(object sender, RoutedEventArgs e) =>
        _overlayWindow?.SetLocked(OverlayLockCheckBox.IsChecked == true);

    /// <summary>Flips the overlay's click-through lock the same way the checkbox does - exposed so
    /// the global lock/unlock hotkey can trigger it too. No-ops if the overlay isn't currently open
    /// (nothing to lock/unlock).</summary>
    public void ToggleOverlayLock()
    {
        if (_overlayWindow is null) return;
        var locked = !_overlayWindow.IsLocked;
        _overlayWindow.SetLocked(locked);
        OverlayLockCheckBox.IsChecked = locked;
    }

    /// <summary>Flips the overlay between Loading and Unloading mode the same way its own toggle
    /// buttons do - exposed so a global hotkey can switch it without having to unlock, click, and
    /// relock the overlay. Works even while the overlay is hidden (it's just shared view-model
    /// state, same as the buttons), and no-ops before a project is loaded.</summary>
    public void ToggleOverlayMode()
    {
        if (_viewModel is null) return;
        _viewModel.ShowUnloadingOverlay = !_viewModel.ShowUnloadingOverlay;
    }

    /// <summary>(Re-)registers all global hotkeys from the current AppSettings - called once at
    /// startup and again from Settings after a binding changes. A null binding means unbound, so
    /// it's simply left unregistered rather than registering some sentinel combo.</summary>
    public void RegisterConfiguredHotkeys()
    {
        if (_viewModel is null) return;
        RegisterHotkey(HotkeyIdOverlayToggle, _viewModel.Settings.OverlayToggleHotkey, ToggleOverlay);
        RegisterHotkey(HotkeyIdOverlayLock, _viewModel.Settings.OverlayLockToggleHotkey, ToggleOverlayLock);
        RegisterHotkey(HotkeyIdOverlayModeToggle, _viewModel.Settings.OverlayModeToggleHotkey, ToggleOverlayMode);
    }

    private void RegisterHotkey(int id, HotkeyBinding? binding, Action action)
    {
        _hotkeys.Unregister(id);
        if (binding is null) return;
        // Corrupt/unparsable settings.json value (e.g. hand-edited) - treat as unbound rather than crashing.
        if (!Enum.TryParse<ModifierKeys>(binding.Modifiers, out var modifiers) || !Enum.TryParse<Key>(binding.Key, out var key))
            return;
        _hotkeys.Register(id, modifiers, key, action);
    }

    /// <summary>Tests whether a candidate global hotkey combo is currently available, without
    /// leaving it registered - called from Settings to validate a freshly-captured keybind before
    /// committing it, so a conflict (another app or the game already owns that combo) surfaces
    /// immediately instead of silently failing on next launch.</summary>
    public bool ProbeHotkey(ModifierKeys modifiers, Key key) => _hotkeys.Probe(modifiers, key);

    /// <summary>Grid-level double-click-to-edit for ordinary DataGridTextColumn cells (Body/Slot/
    /// Name/Notes, Carrier). DataGridCell consumes a double-click during its own tunneling
    /// Preview-phase handling before it ever reaches a bubbling handler on a child element (a
    /// same-pattern MouseLeftButtonDown handler on the cell's own TextBlock never fired at all,
    /// confirmed via diagnostic logging 2026-07-31) - attaching PreviewMouseLeftButtonDown here on
    /// the DataGrid itself instead runs BEFORE that consumption, since Preview events tunnel from
    /// the root down through ancestors first.</summary>
    private void DataGridCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not DataGrid grid) return;
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null || cell.IsReadOnly) return;
        grid.BeginEdit(e);
    }

    /// <summary>Explicit double-click-to-edit for the Structure Type / Station Type template
    /// columns - DataGridTemplateColumn's built-in double-click handling doesn't reliably fire
    /// (a known WPF quirk), so this drives BeginEdit directly rather than relying on it. The first
    /// click of the pair already selected/made this cell current via normal DataGrid handling, so
    /// BeginEdit needs no target argument here.</summary>
    private void TemplateColumnCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        SlotsGrid.BeginEdit(e);
    }

    /// <summary>Deletes the selected slot on Delete - the row's own Remove button was replaced with
    /// the Completed checkbox column. Skipped while a cell's own TextBox is focused so Delete still
    /// deletes a character mid-edit instead of the whole row.</summary>
    private void SlotsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        if (Keyboard.FocusedElement is TextBox) return;
        if (SlotsGrid.SelectedItem is not SlotRowViewModel row) return;
        e.Handled = true;
        _viewModel?.DeleteSlotCommand.Execute(row);
    }

    private void SlotDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _slotDragStartPoint = e.GetPosition(null);
        _slotDragCandidate = (sender as FrameworkElement)?.DataContext as SlotRowViewModel;
    }

    private void SlotDragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_slotDragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _slotDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _slotDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var dragged = _slotDragCandidate;
        _slotDragCandidate = null;
        dragged.IsDragging = true;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, dragged, DragDropEffects.Move);
        }
        finally
        {
            dragged.IsDragging = false;
            _lastSlotDragOverTarget = null;
        }
    }

    private void SlotsGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(SlotRowViewModel)) is not SlotRowViewModel dragged)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        // Only reorder live when hovering an actual row - ignore header/gap hits so a fast drag
        // toward the bottom doesn't get misread as "past the last row" early.
        var target = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource)?.Item as SlotRowViewModel;
        if (target is null || ReferenceEquals(target, dragged) || ReferenceEquals(target, _lastSlotDragOverTarget)) return;

        _lastSlotDragOverTarget = target;
        ReorderSlotsAnimated(dragged, target);
    }

    private void SlotsGrid_Drop(object sender, DragEventArgs e)
    {
        _lastSlotDragOverTarget = null;
        if (e.Data.GetData(typeof(SlotRowViewModel)) is not SlotRowViewModel dragged) return;

        // Dropping past the last row lands on the DataGrid itself rather than a row - treat that
        // as "move to bottom" instead of silently ignoring the drop.
        var targetRow = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
        ReorderSlotsAnimated(dragged, targetRow?.Item as SlotRowViewModel);
    }

    /// <summary>Moves a slot in the view model, then FLIP-animates every row whose on-screen position
    /// shifted as a result, so the rest of the grid visibly slides out of the way instead of
    /// snapping instantly. Runs on every drag-over crossing, not just on drop.</summary>
    private void ReorderSlotsAnimated(SlotRowViewModel dragged, SlotRowViewModel? target)
    {
        if (_viewModel is null) return;

        var before = CaptureSlotRowPositions();
        if (!_viewModel.MoveSlot(dragged, target)) return;

        // Reset any transform left over from a still-playing prior animation before measuring the
        // new layout, so "after" reflects the true new slot rather than a mid-slide offset. Safe to
        // do here - WPF coalesces synchronous changes into a single repaint, so nothing flickers.
        foreach (var vm in _viewModel.SlotRows)
            if (SlotsGrid.ItemContainerGenerator.ContainerFromItem(vm) is DataGridRow row)
                row.RenderTransform = Transform.Identity;

        SlotsGrid.UpdateLayout();
        var after = CaptureSlotRowPositions();

        foreach (var (vm, newY) in after)
        {
            if (!before.TryGetValue(vm, out var oldY)) continue;
            var delta = oldY - newY;
            if (Math.Abs(delta) < 0.5) continue;
            if (SlotsGrid.ItemContainerGenerator.ContainerFromItem(vm) is not DataGridRow row) continue;

            var transform = new TranslateTransform(0, delta);
            row.RenderTransform = transform;
            var animation = new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            transform.BeginAnimation(TranslateTransform.YProperty, animation);
        }
    }

    private Dictionary<SlotRowViewModel, double> CaptureSlotRowPositions()
    {
        var positions = new Dictionary<SlotRowViewModel, double>();
        if (_viewModel is null) return positions;
        foreach (var vm in _viewModel.SlotRows)
        {
            if (SlotsGrid.ItemContainerGenerator.ContainerFromItem(vm) is DataGridRow row)
                positions[vm] = row.TransformToAncestor(SlotsGrid).Transform(new Point(0, 0)).Y;
        }
        return positions;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
