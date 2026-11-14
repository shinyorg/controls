using Shiny.Controls.Analysis;
using Shiny.Maui.Controls;
using Shiny.Maui.Controls.Analysis;
using Shiny.Maui.Controls.Analysis.Barcode;
using Shiny.Maui.Controls.Analysis.Face;
using Shiny.Maui.Controls.Analysis.Motion;
using Shiny.Maui.Controls.Analysis.Ocr;
using Shiny.Maui.Controls.Media;

namespace Sample.Features.Media;

public partial class MediaAnalysisPage : ShinyContentPage
{
    public MediaAnalysisPage()
    {
        this.InitializeComponent();
        this.AnalyzerPicker.SelectedIndex = 0;
        this.Captions.CaptionCommitted += (_, cue) => this.CaptionStatus.Text = $"{(this.Captions.IsTimed ? "Transcribed ahead" : "Live")}: {this.Captions.Cues.Count} captions, last at {cue.Start:mm\\:ss}";
        this.Captions.RecognitionFailed += (_, message) => this.CaptionStatus.Text = message;
    }

    void OnLocalClicked(object? sender, EventArgs e) => this.Player.Source = MediaSource.FromResource("speech.mp4");

    void OnRemoteClicked(object? sender, EventArgs e) => this.Player.Source = MediaSource.FromUri("https://media.w3.org/2010/05/sintel/trailer.mp4");

    void OnAnalyzerChanged(object? sender, EventArgs e)
    {
        this.FrameResult.Text = null;
        this.Player.Analyzer = this.AnalyzerPicker.SelectedIndex switch
        {
            1 => this.Ocr(),
            2 => this.Barcodes(),
            3 => this.Faces(),
            4 => this.Motion(),
            _ => null
        };

        // analyzers only deliver results while armed; keep them armed so the label follows the video
        this.Player.Scan();
    }

    FrameAnalyzer Ocr()
    {
        var analyzer = new OcrAnalyzer();
        analyzer.TextRecognized += (_, args) =>
        {
            this.FrameResult.Text = $"{this.Player.Position:mm\\:ss}  " + String.Join(" · ", args.Blocks.Select(b => b.Text));
            this.Player.Scan();
        };
        return analyzer;
    }

    FrameAnalyzer Barcodes() => new BarcodeAnalyzer
    {
        OnDetected = args =>
        {
            this.FrameResult.Text = String.Join(", ", args.Barcodes.Select(b => $"{b.Format}: {b.Value}"));
            return Task.FromResult(true);
        }
    };

    FrameAnalyzer Faces() => new FaceAnalyzer
    {
        OnDetected = args =>
        {
            this.FrameResult.Text = $"{this.Player.Position:mm\\:ss}  {args.Faces.Count} face(s)";
            return Task.FromResult(true);
        }
    };

    FrameAnalyzer Motion()
    {
        var analyzer = new MotionAnalyzer();
        this.FrameResult.Text = "Boxes mark where the picture is changing; a seek resets the baseline.";
        return analyzer;
    }
}
