namespace Shiny.Blazor.Controls.Theming;

/// <summary>Which colour scheme the app is asking for.</summary>
public enum ShinyThemeMode
{
    /// <summary>Follow the operating system. The default, and what an app gets with no theme host at all.</summary>
    System,

    /// <summary>Force light, whatever the OS prefers.</summary>
    Light,

    /// <summary>Force dark, whatever the OS prefers.</summary>
    Dark
}
