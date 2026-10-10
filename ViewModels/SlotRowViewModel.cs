using ColonyTracker.Models;

namespace ColonyTracker.ViewModels;

/// <summary>Editable wrapper around a ConstructionSlot for the layout grid; edits write straight through to the model.</summary>
public sealed class SlotRowViewModel : ObservableObject
{
    public ConstructionSlot Model { get; }
    private readonly Action _onChanged;
    private readonly IReadOnlyDictionary<string, List<string>> _layoutVariantCatalog;

    public SlotRowViewModel(ConstructionSlot model, Action onChanged, IReadOnlyDictionary<string, List<string>> layoutVariantCatalog)
    {
        Model = model;
        _onChanged = onChanged;
        _layoutVariantCatalog = layoutVariantCatalog;
    }

    /// <summary>Station Type options valid for this row's current StructureType - a station built as
    /// one structure type can only ever be one of that structure's named layout variants.</summary>
    public IReadOnlyList<string> StationTypeOptions =>
        _layoutVariantCatalog.TryGetValue(StructureType, out var variants) ? variants : [];

    public string? Body
    {
        get => Model.Body;
        set { if (Model.Body != value) { Model.Body = value; OnPropertyChanged(); _onChanged(); } }
    }

    public int? SlotNumber
    {
        get => Model.SlotNumber;
        set { if (Model.SlotNumber != value) { Model.SlotNumber = value; OnPropertyChanged(); _onChanged(); } }
    }

    public string? Name
    {
        get => Model.Name;
        set { if (Model.Name != value) { Model.Name = value; OnPropertyChanged(); _onChanged(); } }
    }

    public string StructureType
    {
        get => Model.StructureType;
        set
        {
            if (Model.StructureType == value) return;
            Model.StructureType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StationTypeOptions));
            _onChanged();
        }
    }

    public string? StationType
    {
        get => Model.StationType;
        set { if (Model.StationType != value) { Model.StationType = value; OnPropertyChanged(); _onChanged(); } }
    }

    public string? Notes
    {
        get => Model.Notes;
        set { if (Model.Notes != value) { Model.Notes = value; OnPropertyChanged(); _onChanged(); } }
    }

    public bool Active
    {
        get => Model.Active;
        set { if (Model.Active != value) { Model.Active = value; OnPropertyChanged(); _onChanged(); } }
    }

    public bool Completed
    {
        get => Model.Completed;
        set { if (Model.Completed != value) { Model.Completed = value; OnPropertyChanged(); _onChanged(); } }
    }

    private bool _isDragging;

    /// <summary>Transient UI-only flag (not persisted) - true only while this row is being dragged; drives the layout grid's "pop" row style.</summary>
    public bool IsDragging
    {
        get => _isDragging;
        set => SetProperty(ref _isDragging, value);
    }
}
