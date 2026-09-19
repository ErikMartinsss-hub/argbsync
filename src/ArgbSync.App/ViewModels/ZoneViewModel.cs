using CommunityToolkit.Mvvm.ComponentModel;

namespace ArgbSync.App.ViewModels;

public partial class ZoneViewModel : ObservableObject
{
    public ZoneViewModel(int zoneIndex, string name, int ledCount)
    {
        ZoneIndex = zoneIndex;
        Name = name;
        _ledCount = ledCount;
    }

    public int ZoneIndex { get; }

    public string Name { get; }

    [ObservableProperty]
    private int _ledCount;
}