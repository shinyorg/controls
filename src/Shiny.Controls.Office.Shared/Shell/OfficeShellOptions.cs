using System.ComponentModel;
using System.Runtime.CompilerServices;
using Shiny.Controls.Office.Icons;

namespace Shiny.Controls.Office.Shell;

/// <summary>The appearance the Options page offers.</summary>
public enum OfficeThemeChoice
{
    /// <summary>Follow the operating system.</summary>
    System,
    Light,
    Dark
}


/// <summary>
/// The settings the backstage's Options page edits — who the user is, how the app looks, whether it
/// saves on its own.
/// </summary>
/// <remarks>
/// Observable and edited in place: the backstage writes the user's changes straight into the instance
/// it was given and the host listens to <see cref="PropertyChanged"/> (or the backstage's
/// <c>OptionsChanged</c>) to persist and apply them. The shell applies none of them itself — theming and
/// saving are the host's business.
/// </remarks>
public sealed class OfficeShellOptions : INotifyPropertyChanged
{
    string? userName;
    string? initials;
    OfficeThemeChoice theme;
    bool autoSave = true;

    /// <summary>The name shown on the title bar's avatar and recorded as the author.</summary>
    public string? UserName
    {
        get => this.userName;
        set
        {
            if (this.Set(ref this.userName, value))
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.EffectiveInitials)));
        }
    }

    /// <summary>Initials for the avatar. Null derives them from <see cref="UserName"/>.</summary>
    public string? Initials
    {
        get => this.initials;
        set
        {
            if (this.Set(ref this.initials, value))
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.EffectiveInitials)));
        }
    }

    /// <summary>What the avatar shows.</summary>
    public string EffectiveInitials
        => string.IsNullOrWhiteSpace(this.Initials) ? OfficeColorText.Initials(this.UserName) : this.Initials.Trim().ToUpperInvariant();

    public OfficeThemeChoice Theme
    {
        get => this.theme;
        set => this.Set(ref this.theme, value);
    }

    /// <summary>Whether the title bar's AutoSave switch is on.</summary>
    public bool AutoSave
    {
        get => this.autoSave;
        set => this.Set(ref this.autoSave, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}


/// <summary>
/// One button in the title bar's quick access row, beyond the built-in Save / Undo / Redo.
/// </summary>
/// <remarks>Observable, so an editor can grey one out as its state changes without rebuilding the row.</remarks>
public sealed class OfficeQuickAccessItem : INotifyPropertyChanged
{
    bool isEnabled = true;
    bool isVisible = true;

    public OfficeQuickAccessItem(string id, string text, OfficeShellIcon icon, Func<Task> execute)
    {
        this.Id = id;
        this.Text = text;
        this.Icon = icon;
        this.Execute = execute;
    }

    public OfficeQuickAccessItem(string id, string text, OfficeShellIcon icon, Action execute)
        : this(id, text, icon, () => { execute(); return Task.CompletedTask; }) { }

    public string Id { get; }

    /// <summary>The tooltip.</summary>
    public string Text { get; }

    public OfficeShellIcon Icon { get; }

    public string? Shortcut { get; init; }

    public Func<Task> Execute { get; }

    public bool IsEnabled
    {
        get => this.isEnabled;
        set
        {
            if (this.isEnabled == value) return;
            this.isEnabled = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.IsEnabled)));
        }
    }

    public bool IsVisible
    {
        get => this.isVisible;
        set
        {
            if (this.isVisible == value) return;
            this.isVisible = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.IsVisible)));
        }
    }

    /// <summary>"Save (Ctrl+S)".</summary>
    public string Tooltip => string.IsNullOrWhiteSpace(this.Shortcut) ? this.Text : $"{this.Text} ({this.Shortcut})";

    public event PropertyChangedEventHandler? PropertyChanged;
}
