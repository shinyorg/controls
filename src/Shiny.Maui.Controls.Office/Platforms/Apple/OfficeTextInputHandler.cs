using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// <see cref="EntryHandler"/> for <see cref="OfficeTextInput"/>: a text field that notices Backspace in
/// an empty field, which UIKit otherwise swallows without a trace.
/// </summary>
public class OfficeTextInputHandler : EntryHandler
{
    protected override MauiTextField CreatePlatformView() => new BackspaceTextField(this);

    sealed class BackspaceTextField(OfficeTextInputHandler owner) : MauiTextField
    {
        public override void DeleteBackward()
        {
            var empty = string.IsNullOrEmpty(this.Text);
            base.DeleteBackward();

            if (empty && owner.VirtualView is OfficeTextInput input)
                input.SendEmptyBackspace();
        }
    }
}
