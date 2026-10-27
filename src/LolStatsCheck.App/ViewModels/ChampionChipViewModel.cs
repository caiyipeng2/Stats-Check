using System.Windows.Media;
using LolStatsCheck.Core.Models;

namespace LolStatsCheck.App.ViewModels;

public sealed class ChampionChipViewModel : ObservableObject
{
    private ImageSource? _image;

    public ChampionChipViewModel(ChampionAggregate aggregate, string name, Brush accent)
    {
        ChampionId = aggregate.ChampionId;
        Name = string.IsNullOrWhiteSpace(name) ? $"#{aggregate.ChampionId}" : name;
        Accent = accent;
        Initial = Palette.Initial(Name);
        Caption = $"{aggregate.Games} 场 · {aggregate.WinRate:0}% 胜率";
        Tooltip = $"{Name}\n{Caption}";
        WinRateBrush = Palette.WinRate(aggregate.WinRate, aggregate.Games > 0);
    }

    public int ChampionId { get; }
    public string Name { get; }
    public Brush Accent { get; }
    public Brush WinRateBrush { get; }
    public string Initial { get; }
    public string Caption { get; }
    public string Tooltip { get; }

    public ImageSource? Image
    {
        get => _image;
        private set
        {
            if (Set(ref _image, value))
            {
                Raise(nameof(HasImage));
            }
        }
    }

    public bool HasImage => _image is not null;

    public void SetImage(ImageSource image) => Image = image;
}
