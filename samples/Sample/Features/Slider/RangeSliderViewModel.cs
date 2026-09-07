using CommunityToolkit.Mvvm.ComponentModel;
using Shiny;

namespace Sample.Features.Slider;

[ShellMap<RangeSliderPage>(registerRoute: false)]
public partial class RangeSliderViewModel : ObservableObject
{
    [ObservableProperty] double priceLower = 250;
    [ObservableProperty] double priceUpper = 750;

    [ObservableProperty] double tempLower = 18;
    [ObservableProperty] double tempUpper = 24;

    [ObservableProperty] double bookingLower = 2;
    [ObservableProperty] double bookingUpper = 5;

    [ObservableProperty] double ageLower = 21;
    [ObservableProperty] double ageUpper = 65;

    [ObservableProperty] double shiftStart = 9;
    [ObservableProperty] double shiftEnd = 17;
}
