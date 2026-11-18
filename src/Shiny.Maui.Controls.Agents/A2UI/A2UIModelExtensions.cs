using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Maui.Controls.Agents;

/// <summary>
/// Typed access to components declared with <see cref="A2UIComponentAttribute"/>: the generated model's
/// component name and property reader, so a renderer never names a component or property in a string.
/// </summary>
public static class A2UIModelExtensions
{
    /// <summary>
    /// Evaluates this component into its generated model. Pass the scope from inside
    /// <c>Watch</c> to re-read it when the data model changes. The model's generated <c>Set…</c> methods
    /// write the user's input back to the bound path.
    /// </summary>
    public static T Read<T>(this A2UIRenderContext context, A2UIScope? scope = null) where T : IA2UIComponentModel<T>
    {
        ArgumentNullException.ThrowIfNull(context);
        // Through the context's own Write, so a generated Set… call counts as the user editing the input.
        return T.Read(new A2UIModelSource(context.Component.Properties, context.Surface, scope ?? context.Scope, context.Write));
    }
}
