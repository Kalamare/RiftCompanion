using System.ComponentModel;
using System.Windows.Media;

namespace Rift.Desktop;

// Shared by champion summaries and match rows. Updating an icon never replaces the grids.
public sealed class ProfileVisual(int id, string code, bool champion) : INotifyPropertyChanged
{
    public int Id { get; } = id;
    public string Code { get; } = code;
    public bool IsChampion { get; } = champion;
    public string Name { get; private set; } = champion ? (code == "MonkeyKing" ? "Wukong" : code) : id == 0 ? "Emplacement vide" : $"Objet {id}";
    public bool IsEmpty => !IsChampion && Id == 0;
    public string Fallback => IsEmpty ? "" : "…";
    public ImageSource? Icon { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(string name, ImageSource? icon)
    {
        if (Name != name) { Name = name; PropertyChanged?.Invoke(this, new(nameof(Name))); }
        if (!ReferenceEquals(Icon, icon)) { Icon = icon; PropertyChanged?.Invoke(this, new(nameof(Icon))); }
    }
}
