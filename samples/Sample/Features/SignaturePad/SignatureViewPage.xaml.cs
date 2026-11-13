namespace Sample.Features.SignaturePad;

public partial class SignatureViewPage : ContentPage
{
    public SignatureViewPage()
    {
        InitializeComponent();
        SampleSourceCode.Attach(this);
        this.BindingContext = this;
    }


    void OnStroke(object? sender, EventArgs e)
        => this.StatusLabel.Text = $"Signing… {this.Signature.StrokeCount} stroke(s)";

    void OnCleared(object? sender, EventArgs e)
        => this.StatusLabel.Text = "Cleared";

    void OnUndo(object? sender, EventArgs e) => this.Signature.Undo();

    void OnLock(object? sender, EventArgs e)
    {
        this.Signature.IsReadOnly = !this.Signature.IsReadOnly;
        this.LockButton.Text = this.Signature.IsReadOnly ? "Unlock" : "Lock";
    }

    void OnSubmit(object? sender, EventArgs e)
    {
        using var stream = this.Signature.ExportToPng();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        // A fresh stream per read: the image may load the source again (a recycled handler).
        var png = ms.ToArray();
        this.ResultImage.Source = ImageSource.FromStream(() => new MemoryStream(png));
        this.ResultFrame.IsVisible = png.Length > 0;
        this.StatusLabel.Text = $"Submitted ({png.Length:N0} bytes)";
    }
}
