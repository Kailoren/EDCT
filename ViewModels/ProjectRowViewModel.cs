using ColonyTracker.Models;

namespace ColonyTracker.ViewModels;

/// <summary>Editable wrapper around a ColonyProject for the project sidebar list.</summary>
public sealed class ProjectRowViewModel(ColonyProject model, Action onChanged) : ObservableObject
{
    public ColonyProject Model { get; } = model;

    public string SystemName
    {
        get => Model.SystemName;
        set { if (Model.SystemName != value) { Model.SystemName = value; OnPropertyChanged(); onChanged(); } }
    }
}
