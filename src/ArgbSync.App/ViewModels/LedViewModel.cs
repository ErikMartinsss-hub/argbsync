using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgbSync.App.ViewModels;

public partial class LedViewModel : ObservableObject
{
    public LedViewModel(int index, string name, Color color)
    {
        Index = index;
        Name = name;
        _color = color;
    }

    public int Index { get; }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Brush))]
    private Color _color;

    public Brush Brush => new SolidColorBrush(Color);
}