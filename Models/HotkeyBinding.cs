namespace ColonyTracker.Models;

/// <summary>A user-configured global keybind (Key/ModifierKeys stored as their WPF enum names,
/// e.g. "O"/"Control, Shift", rather than raw ints, so settings.json stays readable if opened
/// directly). A null value on AppSettings means unbound - the corresponding hotkey simply isn't
/// registered with Win32 at all.</summary>
public sealed class HotkeyBinding
{
    public string Key { get; set; } = "";
    public string Modifiers { get; set; } = "None";
}
