namespace Shiny.Maui.Controls.Keyframe.Tests;

/// <summary>
/// Just enough handler for an element to count as "on screen": transitions skip changes made before
/// an element has one, the way they skip a page's initial values.
/// </summary>
sealed class StubHandler : IViewHandler
{
    public IView? VirtualView { get; private set; }
    IElement? IElementHandler.VirtualView => VirtualView;
    public object? PlatformView => null;
    public object? ContainerView => null;
    public bool HasContainer { get; set; }
    public IMauiContext? MauiContext => null;

    public void SetMauiContext(IMauiContext mauiContext) { }
    public void SetVirtualView(IElement view) => VirtualView = view as IView;
    public void UpdateValue(string property) { }
    public void Invoke(string command, object? args = null) { }
    public void DisconnectHandler() => VirtualView = null;
    public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;
    public void PlatformArrange(Rect frame) { }

    public static T Attach<T>(T view) where T : VisualElement
    {
        view.Handler = new StubHandler();
        return view;
    }
}
