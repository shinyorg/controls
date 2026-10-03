using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.Controls.Keyboard;
using Shiny.Maui.Controls;

namespace Sample.Features.KeyboardShortcuts;

[ShellMap<KeyboardShortcutsPage>(registerRoute: false)]
public partial class KeyboardShortcutsViewModel : ObservableObject, IDisposable
{
    readonly IKeyboardShortcutService shortcuts;

    [ObservableProperty] int saveCount;
    [ObservableProperty] string chordStatus = String.Empty;
    [ObservableProperty] bool isTalking;
    [ObservableProperty] double boxX;
    [ObservableProperty] double boxY;
    [ObservableProperty] bool isBold;
    [ObservableProperty] bool isModalOpen;
    [ObservableProperty] bool isCheatSheetOpen;
    [ObservableProperty] bool canSave = true;
    [ObservableProperty] string notes = String.Empty;
    [ObservableProperty] string cheatSheet = String.Empty;

    public KeyboardShortcutsViewModel(IKeyboardShortcutService shortcuts)
    {
        this.shortcuts = shortcuts;
        this.shortcuts.ChordStateChanged += this.OnChordStateChanged;

        this.SaveText = shortcuts.Format("Primary+S");
        this.PaletteText = shortcuts.Format("Primary+Shift+P");
        this.ChordText = shortcuts.Format("Primary+K, Primary+C");
        this.BoldText = shortcuts.Format("Primary+B");
        this.PlatformName = shortcuts.Platform.ToString();
        this.SupportMessage = shortcuts.IsSupported
            ? $"Listening for keys on {shortcuts.Platform}."
            : "No key source on this platform. On macOS (AppKit), Linux and Mac Catalyst call UseDesktopKeyboardShortcuts() from Shiny.Maui.Controls.Desktop.";
    }

    public string SaveText { get; }
    public string PaletteText { get; }
    public string ChordText { get; }
    public string BoldText { get; }
    public string PlatformName { get; }
    public string SupportMessage { get; }

    public ObservableCollection<string> EventLog { get; } = new();

    [RelayCommand(CanExecute = nameof(CanSave))]
    void Save()
    {
        this.SaveCount++;
        this.Log($"Saved ({this.SaveCount})");
    }

    partial void OnCanSaveChanged(bool value) => this.SaveCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    void Palette() => this.Log("Command palette opened");

    [RelayCommand]
    void Comment() => this.Log("Commented the selection (chord)");

    [RelayCommand]
    void ToggleBold()
    {
        this.IsBold = !this.IsBold;
        this.Log($"Bold {(this.IsBold ? "on" : "off")} (editor scope)");
    }

    [RelayCommand]
    void Nudge(string direction)
    {
        switch (direction)
        {
            case "left": this.BoxX = Math.Max(0, this.BoxX - 8); break;
            case "right": this.BoxX = Math.Min(240, this.BoxX + 8); break;
            case "up": this.BoxY = Math.Max(0, this.BoxY - 8); break;
            case "down": this.BoxY = Math.Min(80, this.BoxY + 8); break;
        }
    }

    [RelayCommand]
    void StartTalking()
    {
        this.IsTalking = true;
        this.Log("Push-to-talk: pressed");
    }

    [RelayCommand]
    void StopTalking()
    {
        this.IsTalking = false;
        this.Log("Push-to-talk: released");
    }

    [RelayCommand]
    void OpenModal() => this.IsModalOpen = true;

    [RelayCommand]
    void CloseModal()
    {
        this.IsModalOpen = false;
        this.Log("Modal closed with Escape");
    }

    [RelayCommand]
    void ModalAction() => this.Log("Modal's own Enter shortcut");

    [RelayCommand]
    void ToggleCheatSheet()
    {
        this.IsCheatSheetOpen = !this.IsCheatSheetOpen;
        if (!this.IsCheatSheetOpen)
            return;

        // One label rather than a bound list: the AppKit head never paints views added after the
        // first layout, and a cheat sheet is built exactly then. Highest precedence first, so the
        // view's shortcuts lead the page's.
        var rows = this.shortcuts
            .GetActiveShortcuts()
            .Where(x => !x.IsShadowed)
            .Select(x => $"{x.Binding.Gesture.ToDisplayString(this.shortcuts.Platform),-12}{x.Binding.Description ?? x.Binding.Gesture.ToString()}");

        this.CheatSheet = String.Join(Environment.NewLine, rows);
    }

    void OnChordStateChanged(object? sender, EventArgs e)
    {
        this.ChordStatus = this.shortcuts.IsChordPending
            ? $"{String.Join(" ", this.shortcuts.PendingChords.Select(x => x.ToDisplayString(this.shortcuts.Platform)))} was pressed — waiting for the next key…"
            : String.Empty;
    }

    void Log(string message)
    {
        this.EventLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (this.EventLog.Count > 30)
            this.EventLog.RemoveAt(this.EventLog.Count - 1);
    }

    public void Dispose() => this.shortcuts.ChordStateChanged -= this.OnChordStateChanged;
}
