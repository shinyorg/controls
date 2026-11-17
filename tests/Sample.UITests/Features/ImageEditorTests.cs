namespace Sample.UITests.Features;

/// <summary>
/// ImageEditor: zoom clamped into MinZoom..MaxZoom and reported back through ZoomLevel, and the
/// ribbon's tool toggles (faces carry ImageEditorTool{Mode} ids and tap through a recognizer Command).
/// </summary>
public class ImageEditorTests : UITest
{
    /// <summary>Shell keeps the page alive between visits, so the editor is put back to fit + Move.</summary>
    async Task OpenPage()
    {
        await Open("imageeditor", "ImageEditorPage");
        await Driver.SetProperty("CurrentToolMode", "Move", automationId: "PhotoEditor");
        await Driver.SetProperty("ZoomLevel", "1", automationId: "PhotoEditor");
        await Driver.WaitForProperty("PhotoEditor", "CurrentToolMode", "Move");
    }


    [Test]
    public async Task Zoom_Level_Is_Applied()
    {
        await OpenPage();
        await Driver.WaitUntil(async () => await Driver.GetDouble("ZoomLevel", "PhotoEditor") == 1.0, "the editor to start at fit");

        await Driver.SetProperty("ZoomLevel", "2", automationId: "PhotoEditor");

        await Driver.WaitUntil(async () => await Driver.GetDouble("ZoomLevel", "PhotoEditor") is { } z && Math.Abs(z - 2) < 0.01, "2x to apply");
        await AssertNoAppErrors();
    }


    [Test]
    public async Task Zoom_Above_MaxZoom_Is_Clamped()
    {
        await OpenPage();

        // MaxZoom="8" on the sample
        await Driver.SetProperty("ZoomLevel", "20", automationId: "PhotoEditor");

        await Driver.WaitUntil(async () => await Driver.GetDouble("ZoomLevel", "PhotoEditor") is { } z && Math.Abs(z - 8) < 0.01, "the zoom to clamp to MaxZoom 8");
        await AssertNoAppErrors();
    }


    [Test]
    public async Task Zoom_Below_MinZoom_Is_Clamped()
    {
        await OpenPage();
        await Driver.SetProperty("ZoomLevel", "3", automationId: "PhotoEditor");
        await Driver.WaitUntil(async () => await Driver.GetDouble("ZoomLevel", "PhotoEditor") is { } z && Math.Abs(z - 3) < 0.01, "3x to apply");

        // MinZoom defaults to 1 (fit)
        await Driver.SetProperty("ZoomLevel", "0.2", automationId: "PhotoEditor");

        await Driver.WaitUntil(async () => await Driver.GetDouble("ZoomLevel", "PhotoEditor") is { } z && Math.Abs(z - 1) < 0.01, "the zoom to clamp to MinZoom 1");
        await AssertNoAppErrors();
    }


    [Test]
    public async Task Ribbon_Tool_Toggle_Selects_And_Puts_Down()
    {
        await OpenPage();
        await Driver.WaitForProperty("PhotoEditor", "CurrentToolMode", "Move");

        await Driver.Tap(automationId: "PhotoEditor_Tool_Draw");
        await Driver.WaitForProperty("PhotoEditor", "CurrentToolMode", "Draw");

        // pressing the tool in hand returns to Move (the ribbon is rebuilt, so look it up again)
        await Driver.Tap(automationId: "PhotoEditor_Tool_Draw");
        await Driver.WaitForProperty("PhotoEditor", "CurrentToolMode", "Move");
        await AssertNoAppErrors();
    }


    [Test]
    public async Task Tool_Set_From_Code_Is_Put_Down_By_Its_Ribbon_Toggle()
    {
        await OpenPage();

        await Driver.SetProperty("CurrentToolMode", "Rectangle", automationId: "PhotoEditor");

        await Driver.WaitForProperty("PhotoEditor", "CurrentToolMode", "Rectangle");

        // the ribbon is rebuilt for the new tool, so its Rectangle toggle now reads as the tool in hand

        await Driver.Tap(automationId: "PhotoEditor_Tool_Rectangle");
        await Driver.WaitForProperty("PhotoEditor", "CurrentToolMode", "Move");
        await AssertNoAppErrors();
    }
}
