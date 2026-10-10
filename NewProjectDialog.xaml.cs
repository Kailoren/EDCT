using System.IO;
using System.Windows;
using ColonyTracker.Models;
using ColonyTracker.Services;
using Microsoft.Win32;

namespace ColonyTracker;

public partial class NewProjectDialog : Window
{
    // Generous for a real Raven Colonial system export - guards against a pathologically large
    // (or maliciously inflated) shared file, since these are the kind of thing players pass
    // around to each other.
    private const long MaxRavenColonialFileBytes = 2 * 1024 * 1024;

    private readonly IReadOnlyList<string> _structureTypeNames;
    private readonly IReadOnlyDictionary<string, List<string>> _layoutVariantCatalog;
    private readonly IReadOnlyDictionary<string, RavenColonialBuildType> _ravenColonialBuildTypes;

    public ColonyProject? Result { get; private set; }

    public NewProjectDialog(
        IReadOnlyList<string> structureTypeNames,
        IReadOnlyDictionary<string, List<string>> layoutVariantCatalog,
        IReadOnlyDictionary<string, RavenColonialBuildType> ravenColonialBuildTypes)
    {
        InitializeComponent();
        _structureTypeNames = structureTypeNames;
        _layoutVariantCatalog = layoutVariantCatalog;
        _ravenColonialBuildTypes = ravenColonialBuildTypes;
    }

    private void ModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        // The paste panels may not exist yet the first time this fires - Checked="True" on
        // FromScratchRadio in XAML raises this during InitializeComponent, before the panel fields are set.
        if (SheetPastePanel is null || RavenColonialPastePanel is null) return;
        SheetPastePanel.Visibility = FromSheetRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RavenColonialPastePanel.Visibility = FromRavenColonialRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseRavenColonialFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a Raven Colonial system export",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            if (new FileInfo(dialog.FileName).Length > MaxRavenColonialFileBytes)
            {
                ShowMessage("That file is too large to be a real Raven Colonial export - couldn't load it.", MessageBoxImage.Warning);
                return;
            }
            RavenColonialPasteBox.Text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowMessage($"Couldn't read that file:\n\n{ex.Message}", MessageBoxImage.Error);
        }
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        ColonyProject project;

        if (FromSheetRadio.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(SheetLinkBox.Text))
            {
                ShowMessage("Paste a Google Sheets share link first.", MessageBoxImage.Warning);
                return;
            }

            CreateButton.IsEnabled = false;
            var (text, error) = await GoogleSheetImportService.FetchTabAsync(SheetLinkBox.Text.Trim());
            CreateButton.IsEnabled = true;
            if (error is not null)
            {
                ShowMessage(error, MessageBoxImage.Error);
                return;
            }

            var sheetSystemName = SheetPasteParser.ExtractSystemName(text!);
            var (slots, warnings) = SheetPasteParser.Parse(text!, _structureTypeNames, _layoutVariantCatalog);
            if (slots.Count == 0)
            {
                ShowMessage(
                    "Couldn't find any slot rows in that sheet tab. Make sure the link points at the right tab, or switch to \"Create from scratch\".",
                    MessageBoxImage.Warning);
                return;
            }

            project = new ColonyProject { SystemName = ResolveSystemName(fallback: sheetSystemName ?? "New Colony"), Slots = slots };
            ShowImportWarnings(slots.Count, warnings);
        }
        else if (FromRavenColonialRadio.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(RavenColonialPasteBox.Text))
            {
                ShowMessage("Load or paste a Raven Colonial JSON export first.", MessageBoxImage.Warning);
                return;
            }

            var (rcSystemName, slots, warnings) = RavenColonialImportService.Parse(RavenColonialPasteBox.Text, _ravenColonialBuildTypes);
            if (slots.Count == 0)
            {
                ShowMessage(
                    "Couldn't find any sites in that file. Make sure it's a Raven Colonial system export (backup_*.json), or switch to \"Create from scratch\".",
                    MessageBoxImage.Warning);
                return;
            }

            project = new ColonyProject { SystemName = ResolveSystemName(fallback: rcSystemName), Slots = slots };
            ShowImportWarnings(slots.Count, warnings);
        }
        else
        {
            project = new ColonyProject { SystemName = ResolveSystemName(fallback: "New Colony") };
        }

        Result = project;
        DialogResult = true;
    }

    private string ResolveSystemName(string fallback) =>
        string.IsNullOrWhiteSpace(SystemNameBox.Text) ? fallback : SystemNameBox.Text.Trim();

    private void ShowImportWarnings(int slotCount, List<string> warnings)
    {
        if (warnings.Count == 0) return;
        var preview = string.Join("\n", warnings.Take(10));
        var more = warnings.Count > 10 ? $"\n...and {warnings.Count - 10} more." : "";
        ShowMessage(
            $"Imported {slotCount} slot(s). {warnings.Count} value(s) didn't match a known catalog entry and were kept as typed - fix them via the Structure Type/Station Type columns afterward.\n\n{preview}{more}",
            MessageBoxImage.Information);
    }

    private void ShowMessage(string text, MessageBoxImage icon) =>
        MessageBox.Show(this, text, "New Colony Project", MessageBoxButton.OK, icon);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
