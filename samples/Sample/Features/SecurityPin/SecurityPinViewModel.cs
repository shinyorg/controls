using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Shiny;

namespace Sample.Features.SecurityPin;

[ShellMap<SecurityPinPage>(registerRoute: false)]
public partial class SecurityPinViewModel(AppSettings appSettings) : ObservableObject
{
    public bool IsSecurityBeepEnabled
    {
        get => appSettings.IsSecurityBeepEnabled;
        set
        {
            appSettings.IsSecurityBeepEnabled = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    string hiddenPin = string.Empty;

    [ObservableProperty]
    string visiblePin = string.Empty;

    [ObservableProperty]
    string alphaPin = string.Empty;

    [ObservableProperty]
    string highlightPin = string.Empty;

    [ObservableProperty]
    string statusMessage = "Enter a PIN to get started";

    [RelayCommand]
    void Clear()
    {
        HiddenPin = string.Empty;
        VisiblePin = string.Empty;
        AlphaPin = string.Empty;
        HighlightPin = string.Empty;
        StatusMessage = "Cleared";
    }

    public void OnHiddenPinCompleted(string value)
        => StatusMessage = $"Hidden PIN entered: {new string('*', value.Length)}";

    public void OnVisiblePinCompleted(string value)
        => StatusMessage = $"Visible PIN entered: {value}";

    public void OnAlphaPinCompleted(string value)
        => StatusMessage = $"Alpha PIN entered: {value}";
}
