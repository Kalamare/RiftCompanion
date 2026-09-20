using System.ComponentModel;
using System.Windows.Media;

namespace Rift.Desktop;

public sealed record ItemComponent(string Name, ImageSource? Icon);

// Shared by champion summaries and match rows. Updating an icon never replaces the grids.
public sealed class ProfileVisual(int id, string code, bool champion) : INotifyPropertyChanged
{
    public int Id { get; } = id;
    public string Code { get; } = code;
    public bool IsChampion { get; } = champion;
    public bool IsSpell { get; init; }
    public bool IsRune { get; init; }
    public bool IsRoleQuest { get; init; }
    public string Name { get; private set; } = champion ? (code == "MonkeyKing" ? "Wukong" : code) : id == 0 ? "Emplacement vide" : $"Objet {id}";
    public bool IsEmpty => !IsChampion && !IsSpell && !IsRune && !IsRoleQuest && Id == 0;
    public string Fallback => IsEmpty ? "" : (IsRune || IsRoleQuest) && Id == 0 ? "—" : "…";
    public ImageSource? Icon { get; private set; }
    public string Description { get; private set; } = "";
    public IReadOnlyList<ItemComponent> Components { get; private set; } = [];
    public bool HasComponents => Components.Count > 0;
    public string PriceLabel { get; private set; } = "";
    public void UpdateRecipe(int? price, IReadOnlyList<ItemComponent> components)
    {
        var label = price is { } amount ? $"{amount:N0} or" : "";
        if (PriceLabel != label) { PriceLabel = label; PropertyChanged?.Invoke(this, new(nameof(PriceLabel))); }
        if (!Components.SequenceEqual(components))
        {
            Components = components; PropertyChanged?.Invoke(this, new(nameof(Components))); PropertyChanged?.Invoke(this, new(nameof(HasComponents)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(string name, ImageSource? icon, string description = "")
    {
        if (Name != name) { Name = name; PropertyChanged?.Invoke(this, new(nameof(Name))); }
        if (!ReferenceEquals(Icon, icon)) { Icon = icon; PropertyChanged?.Invoke(this, new(nameof(Icon))); }
        if (Description != description) { Description = description; PropertyChanged?.Invoke(this, new(nameof(Description))); }
    }
}
