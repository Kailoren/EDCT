using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ColonyTracker.Models;

namespace ColonyTracker;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    private string _overlayBackgroundColor;
    private string _overlayBorderColor;
    private string _overlayHeaderColor;
    private string _overlayTextColor;
    private string _overlayMutedColor;
    private string _overlayButtonColor;
    private string _overlayInactiveButtonColor;
    private string _overlayButtonTextColor;
    private HotkeyBinding? _overlayToggleHotkey;
    private HotkeyBinding? _overlayLockHotkey;
    private HotkeyBinding? _overlayModeToggleHotkey;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        LargeShipBox.Text = settings.LargeShipCapacity.ToString();
        MediumShipBox.Text = settings.MediumShipCapacity.ToString();
        CarrierBox.Text = settings.FleetCarrierCapacity.ToString();
        JournalDirBox.Text = settings.JournalDirectoryOverride ?? "";

        _overlayBackgroundColor = settings.OverlayBackgroundColor;
        _overlayBorderColor = settings.OverlayBorderColor;
        _overlayHeaderColor = settings.OverlayHeaderColor;
        _overlayTextColor = settings.OverlayTextColor;
        _overlayMutedColor = settings.OverlayMutedColor;
        _overlayButtonColor = settings.OverlayButtonColor;
        _overlayInactiveButtonColor = settings.OverlayInactiveButtonColor;
        _overlayButtonTextColor = settings.OverlayButtonTextColor;
        UpdateSwatch(OverlayBackgroundSwatch, _overlayBackgroundColor);
        UpdateSwatch(OverlayBorderSwatch, _overlayBorderColor);
        UpdateSwatch(OverlayHeaderSwatch, _overlayHeaderColor);
        UpdateSwatch(OverlayTextSwatch, _overlayTextColor);
        UpdateSwatch(OverlayMutedSwatch, _overlayMutedColor);
        UpdateSwatch(OverlayButtonSwatch, _overlayButtonColor);
        UpdateSwatch(OverlayInactiveButtonSwatch, _overlayInactiveButtonColor);
        UpdateSwatch(OverlayButtonTextSwatch, _overlayButtonTextColor);
        OverlayOpacitySlider.Value = settings.OverlayOpacity * 100;

        _overlayToggleHotkey = settings.OverlayToggleHotkey;
        _overlayLockHotkey = settings.OverlayLockToggleHotkey;
        _overlayModeToggleHotkey = settings.OverlayModeToggleHotkey;
        RefreshHotkeyBoxText(OverlayToggleHotkeyBox);
        RefreshHotkeyBoxText(OverlayLockHotkeyBox);
        RefreshHotkeyBoxText(OverlayModeToggleHotkeyBox);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateCapacities()) return;
        ApplyCargoAndJournalFields();
        ApplyOverlayFields();
        (Owner as MainWindow)?.RegisterConfiguredHotkeys();
        DialogResult = true;
    }

    /// <summary>Commits the overlay-appearance fields (only - not cargo capacities/journal dir,
    /// which Apply intentionally leaves alone) into the live AppSettings and pushes them to
    /// whichever OverlayWindow the owning MainWindow currently has open, without closing this
    /// dialog - lets the user iterate on colors/opacity against the real overlay directly.</summary>
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ApplyOverlayFields();
        (Owner as MainWindow)?.ApplyOverlayAppearance();
        (Owner as MainWindow)?.RegisterConfiguredHotkeys();
    }

    private void ToggleOverlay_Click(object sender, RoutedEventArgs e) => (Owner as MainWindow)?.ToggleOverlay();

    private bool ValidateCapacities()
    {
        if (int.TryParse(LargeShipBox.Text, out var largeShip) && largeShip > 0 &&
            int.TryParse(MediumShipBox.Text, out var mediumShip) && mediumShip > 0 &&
            int.TryParse(CarrierBox.Text, out var carrier) && carrier > 0)
            return true;

        MessageBox.Show(this, "Cargo capacities must be positive whole numbers.", "Settings",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void ApplyCargoAndJournalFields()
    {
        _settings.LargeShipCapacity = int.Parse(LargeShipBox.Text);
        _settings.MediumShipCapacity = int.Parse(MediumShipBox.Text);
        _settings.FleetCarrierCapacity = int.Parse(CarrierBox.Text);
        _settings.JournalDirectoryOverride = string.IsNullOrWhiteSpace(JournalDirBox.Text) ? null : JournalDirBox.Text.Trim();
    }

    private void ApplyOverlayFields()
    {
        _settings.OverlayOpacity = OverlayOpacitySlider.Value / 100;
        _settings.OverlayBackgroundColor = _overlayBackgroundColor;
        _settings.OverlayBorderColor = _overlayBorderColor;
        _settings.OverlayHeaderColor = _overlayHeaderColor;
        _settings.OverlayTextColor = _overlayTextColor;
        _settings.OverlayMutedColor = _overlayMutedColor;
        _settings.OverlayButtonColor = _overlayButtonColor;
        _settings.OverlayInactiveButtonColor = _overlayInactiveButtonColor;
        _settings.OverlayButtonTextColor = _overlayButtonTextColor;
        _settings.OverlayToggleHotkey = _overlayToggleHotkey;
        _settings.OverlayLockToggleHotkey = _overlayLockHotkey;
        _settings.OverlayModeToggleHotkey = _overlayModeToggleHotkey;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>Resets the in-dialog overlay fields (swatches/slider) back to the app's built-in
    /// defaults, read from a fresh AppSettings rather than repeated here, so this never drifts out
    /// of sync with whatever the real defaults are. Doesn't touch the live AppSettings or the open
    /// overlay by itself - click Apply or Save afterward the same as any other change here.</summary>
    private void ResetOverlayDefaults_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        _overlayBackgroundColor = defaults.OverlayBackgroundColor;
        _overlayBorderColor = defaults.OverlayBorderColor;
        _overlayHeaderColor = defaults.OverlayHeaderColor;
        _overlayTextColor = defaults.OverlayTextColor;
        _overlayMutedColor = defaults.OverlayMutedColor;
        _overlayButtonColor = defaults.OverlayButtonColor;
        _overlayInactiveButtonColor = defaults.OverlayInactiveButtonColor;
        _overlayButtonTextColor = defaults.OverlayButtonTextColor;
        UpdateSwatch(OverlayBackgroundSwatch, _overlayBackgroundColor);
        UpdateSwatch(OverlayBorderSwatch, _overlayBorderColor);
        UpdateSwatch(OverlayHeaderSwatch, _overlayHeaderColor);
        UpdateSwatch(OverlayTextSwatch, _overlayTextColor);
        UpdateSwatch(OverlayMutedSwatch, _overlayMutedColor);
        UpdateSwatch(OverlayButtonSwatch, _overlayButtonColor);
        UpdateSwatch(OverlayInactiveButtonSwatch, _overlayInactiveButtonColor);
        UpdateSwatch(OverlayButtonTextSwatch, _overlayButtonTextColor);
        OverlayOpacitySlider.Value = defaults.OverlayOpacity * 100;
    }

    private void OverlayOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OverlayOpacityLabel is null) return; // fires once during InitializeComponent, before named fields are all set
        OverlayOpacityLabel.Text = $"{(int)OverlayOpacitySlider.Value}%";
    }

    private void ChooseOverlayBackground_Click(object sender, RoutedEventArgs e) => PickColor(OverlayBackgroundSwatch, ref _overlayBackgroundColor);
    private void ChooseOverlayBorder_Click(object sender, RoutedEventArgs e) => PickColor(OverlayBorderSwatch, ref _overlayBorderColor);
    private void ChooseOverlayHeader_Click(object sender, RoutedEventArgs e) => PickColor(OverlayHeaderSwatch, ref _overlayHeaderColor);
    private void ChooseOverlayText_Click(object sender, RoutedEventArgs e) => PickColor(OverlayTextSwatch, ref _overlayTextColor);
    private void ChooseOverlayMuted_Click(object sender, RoutedEventArgs e) => PickColor(OverlayMutedSwatch, ref _overlayMutedColor);
    private void ChooseOverlayButton_Click(object sender, RoutedEventArgs e) => PickColor(OverlayButtonSwatch, ref _overlayButtonColor);
    private void ChooseOverlayInactiveButton_Click(object sender, RoutedEventArgs e) => PickColor(OverlayInactiveButtonSwatch, ref _overlayInactiveButtonColor);
    private void ChooseOverlayButtonText_Click(object sender, RoutedEventArgs e) => PickColor(OverlayButtonTextSwatch, ref _overlayButtonTextColor);

    /// <summary>Opens the app's own themed color picker (ColorPickerWindow) seeded with the
    /// swatch's current color, and writes the result back into both the swatch and the tracked hex
    /// string (committed to AppSettings on Save/Apply, not immediately - matches every other field
    /// in this dialog).</summary>
    private void PickColor(Border swatch, ref string colorHex)
    {
        var current = (Color)ColorConverter.ConvertFromString(colorHex);
        var picker = new ColorPickerWindow(current) { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is not { } chosen) return;

        colorHex = $"#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}";
        UpdateSwatch(swatch, colorHex);
    }

    private static void UpdateSwatch(Border swatch, string hex)
    {
        try
        {
            swatch.Background = (Brush)new BrushConverter().ConvertFromString(hex)!;
        }
        catch
        {
            // leave whatever was already shown if the stored value somehow doesn't parse
        }
    }

    private void ClearOverlayToggleHotkey_Click(object sender, RoutedEventArgs e)
    {
        _overlayToggleHotkey = null;
        RefreshHotkeyBoxText(OverlayToggleHotkeyBox);
    }

    private void ClearOverlayLockHotkey_Click(object sender, RoutedEventArgs e)
    {
        _overlayLockHotkey = null;
        RefreshHotkeyBoxText(OverlayLockHotkeyBox);
    }

    private void ClearOverlayModeToggleHotkey_Click(object sender, RoutedEventArgs e)
    {
        _overlayModeToggleHotkey = null;
        RefreshHotkeyBoxText(OverlayModeToggleHotkeyBox);
    }

    /// <summary>Same fix as MainWindow's system-name box: a hotkey box left "listening" and then
    /// clicked away from (onto blank space, not another focusable control) would otherwise stay
    /// stuck showing "Press a key combination..." forever, since blank areas aren't focusable and
    /// don't move focus away on their own.</summary>
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) => Keyboard.ClearFocus();

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e) =>
        ((TextBox)sender).Text = "Press a key combination... (Esc to cancel)";

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e) => RefreshHotkeyBoxText((TextBox)sender);

    /// <summary>Captures the box's next real key press (Ctrl/Shift/Alt held alone don't count - they
    /// keep waiting for the actual key) as a candidate hotkey, validates it, and either commits it
    /// or shows why it didn't. Marks every key handled while listening so nothing else (Tab
    /// navigation, Alt's menu-focus behavior, ...) interferes with the capture.</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;

        if (e.Key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key)) return; // still waiting for the real key

        TryCaptureHotkey(box, Keyboard.Modifiers, key);
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    /// <summary>Each hotkey box paired with getter/setter access to its backing field, so capture/
    /// refresh/conflict-checking below can loop over all of them instead of hardcoding pairwise
    /// comparisons - built fresh per call rather than cached since it's just closures over fields.</summary>
    private (TextBox Box, Func<HotkeyBinding?> Get, Action<HotkeyBinding?> Set)[] HotkeySlots() =>
    [
        (OverlayToggleHotkeyBox, () => _overlayToggleHotkey, v => _overlayToggleHotkey = v),
        (OverlayLockHotkeyBox, () => _overlayLockHotkey, v => _overlayLockHotkey = v),
        (OverlayModeToggleHotkeyBox, () => _overlayModeToggleHotkey, v => _overlayModeToggleHotkey = v)
    ];

    /// <summary>Validates a freshly-captured combo before committing it: rejects it if one of the
    /// app's *other* hotkeys is already pending that same combo (checked here rather than relying
    /// on the OS, since the other hotkey's real registration - if changed in this same Settings
    /// session - hasn't actually been re-registered yet), skips the OS probe entirely if it's just
    /// the field's own unchanged value (a harmless no-op re-press would otherwise "conflict" with
    /// itself, since that combo is still genuinely registered under this field's own id), and
    /// otherwise asks MainWindow to test-register it for real so a conflict with another app/the
    /// game surfaces immediately instead of silently failing on next launch.</summary>
    private void TryCaptureHotkey(TextBox box, ModifierKeys modifiers, Key key)
    {
        var slots = HotkeySlots();
        if (slots.Any(s => s.Box != box && Matches(s.Get(), modifiers, key)))
        {
            MessageBox.Show(this, "That combination is already assigned to another hotkey.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            Keyboard.ClearFocus();
            return;
        }

        var thisSlot = slots.First(s => s.Box == box);
        var unchanged = Matches(thisSlot.Get(), modifiers, key);
        if (!unchanged && Owner is MainWindow mainWindow && !mainWindow.ProbeHotkey(modifiers, key))
        {
            MessageBox.Show(this, "That combination couldn't be registered - it may already be in use by another application.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            Keyboard.ClearFocus();
            return;
        }

        var binding = new HotkeyBinding { Modifiers = modifiers.ToString(), Key = key.ToString() };
        thisSlot.Set(binding);
        box.Text = FormatHotkey(binding);
        Keyboard.ClearFocus();
    }

    private static bool Matches(HotkeyBinding? binding, ModifierKeys modifiers, Key key) =>
        binding is not null && binding.Modifiers == modifiers.ToString() && binding.Key == key.ToString();

    private void RefreshHotkeyBoxText(TextBox box) =>
        box.Text = FormatHotkey(HotkeySlots().First(s => s.Box == box).Get());

    private static string FormatHotkey(HotkeyBinding? binding)
    {
        if (binding is null) return "Not set";
        if (!Enum.TryParse<ModifierKeys>(binding.Modifiers, out var modifiers) || !Enum.TryParse<Key>(binding.Key, out var key))
            return "Not set";

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(FriendlyKeyName(key));
        return string.Join("+", parts);
    }

    private static string FriendlyKeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + ((int)key - (int)Key.NumPad0),
        _ => key.ToString()
    };
}
