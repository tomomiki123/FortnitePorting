using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FortnitePorting.Framework;

namespace FortnitePorting.ViewModels;

public partial class LoadingViewModel : ViewModelBase
{
    [ObservableProperty] private int loadingTiers;
    [ObservableProperty] private string loadingText = "Loading Application";
    [ObservableProperty] private float loadingPercentage = 0.0f;
    [ObservableProperty] private float loadingPercentageTarget = 0.0f;
    [ObservableProperty] private LinearGradientBrush loadingOpacityMask = new();

    public LoadingViewModel()
    {
        LoadingOpacityMask = new LinearGradientBrush // only way to get gradient stop offset to update
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1.0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(Colors.Transparent, 1),
                new(Colors.White, 1)
            }
        };
        
        Task.Run(async () =>
        {
            while (LoadingPercentage < 1.0f)
            {
                while (LoadingPercentage < LoadingPercentageTarget)
                {
                    LoadingPercentage += 0.005f;
                    var pos = 1 - LoadingPercentage;
                    
                    await Task.Delay(1);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        LoadingOpacityMask = new LinearGradientBrush // only way to get gradient stop offset to update
                        {
                            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                            EndPoint = new RelativePoint(0, 1.0, RelativeUnit.Relative),
                            GradientStops = new GradientStops
                            {
                                new(Colors.Transparent, pos),
                                new(Colors.White, pos)
                            }
                        };
                    });
                    
                }
            }
        });
    }

    public void Update(string text)
    {
        Update(text, LoadingPercentageTarget + 1.0f / LoadingTiers);
    }

    public void Update(string text, float percentage)
    {
        LoadingText = text;
        LoadingPercentageTarget = percentage;
    }
}