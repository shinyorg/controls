namespace Shiny.Controls.Gamepad.Tests;


public class GamepadEngineTests
{
    // a landscape phone - every preset is anchored to its corners, so the coordinates below are
    // measured from them exactly as a player's thumbs would be
    const float W = 844, H = 390;

    readonly ManualTimeProvider time = new();
    readonly VirtualGamepad pad = new("test");
    readonly List<GamepadButtonChangedEventArgs> buttonEvents = [];


    GamepadEngine Create(GamepadLayout layout)
    {
        var engine = new GamepadEngine(this.pad, this.time) { Layout = layout };
        engine.Arrange(W, H);
        this.pad.ButtonChanged += (_, e) => this.buttonEvents.Add(e);
        return engine;
    }


    static GamepadElementVisual Visual(GamepadEngine engine, string id)
        => engine.Elements.Single(x => x.Element.Id == id);


    [Fact]
    public void Anchored_ElementsSitAtTheirCorners()
    {
        var engine = this.Create(GamepadLayouts.Nes());

        var dpad = Visual(engine, "dpad");
        dpad.Bounds.CenterX.ShouldBe(100);
        dpad.Bounds.CenterY.ShouldBe(H - 100);

        var a = Visual(engine, "a");
        a.Bounds.CenterX.ShouldBe(W - 78);
        a.Bounds.CenterY.ShouldBe(H - 88);
    }


    [Fact]
    public void Anchored_ScaleGrowsAroundTheAnchor()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        engine.Scale = 2;

        var dpad = Visual(engine, "dpad");
        dpad.Bounds.CenterX.ShouldBe(200);
        dpad.Bounds.CenterY.ShouldBe(H - 200);
        dpad.Bounds.Width.ShouldBe(260);
    }


    [Theory]
    [InlineData(GamepadPreset.Standard)]
    [InlineData(GamepadPreset.Snes)]
    [InlineData(GamepadPreset.Arcade)]
    public void Anchored_PortraitPhoneShrinksSoClustersDoNotCollide(GamepadPreset preset)
    {
        // found running the MAUI sample on a 411dp-wide portrait phone: Standard's corners met
        var engine = this.Create(GamepadLayouts.Get(preset));
        engine.Arrange(411, 860);

        engine.VisualScale.ShouldBeLessThan(1f);
        var visuals = engine.Elements;
        for (var i = 0; i < visuals.Count; i++)
        for (var j = i + 1; j < visuals.Count; j++)
        {
            var a = visuals[i].Bounds;
            var b = visuals[j].Bounds;
            var dx = a.CenterX - b.CenterX;
            var dy = a.CenterY - b.CenterY;
            // compare as circles of the smaller dimension - generous to pills and shoulders
            var reach = (MathF.Min(a.Width, a.Height) + MathF.Min(b.Width, b.Height)) / 2;
            (MathF.Sqrt(dx * dx + dy * dy) >= reach).ShouldBeTrue($"{visuals[i].Element.Id} collides with {visuals[j].Element.Id}");
        }
    }


    [Fact]
    public void Anchored_LandscapeKeepsNaturalSize()
    {
        var engine = this.Create(GamepadLayouts.Standard());
        engine.VisualScale.ShouldBe(1f);
    }


    [Fact]
    public void Uniform_FitsTheDesignCanvasAndCentresIt()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        engine.Sizing = GamepadSizing.Uniform;

        // 520x200 design into 1040 wide = scale 2, centred vertically in 1000
        engine.Arrange(1040, 1000);
        engine.VisualScale.ShouldBe(2);
        engine.Body.ShouldNotBeNull();
        engine.Body!.Value.Height.ShouldBe(400);
        engine.Body!.Value.Y.ShouldBe(300);
        Visual(engine, "dpad").Bounds.CenterX.ShouldBe(200);
    }


    [Fact]
    public void Button_PressAndReleaseRaiseOneEventEach()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        var a = Visual(engine, "a");

        engine.PointerDown(1, a.Bounds.CenterX, a.Bounds.CenterY).ShouldBeTrue();
        this.pad.GetState().IsPressed(GamepadButton.B).ShouldBeTrue();
        a.IsPressed.ShouldBeTrue();

        engine.PointerUp(1);
        this.pad.GetState().Buttons.ShouldBe(GamepadButton.None);

        this.buttonEvents.Select(x => (x.Button, x.IsPressed)).ShouldBe([
            (GamepadButton.B, true),
            (GamepadButton.B, false)
        ]);
    }


    [Fact]
    public void Nes_LabelsArePositional()
    {
        var engine = this.Create(GamepadLayouts.Nes());

        // NES "B" is the left button and reports the bottom face button, as on Nintendo's later pads
        var b = Visual(engine, "b");
        b.Face.Text.ShouldBe("B");
        b.Element.Button.ShouldBe(GamepadButton.A);

        var a = Visual(engine, "a");
        a.Face.Text.ShouldBe("A");
        a.Element.Button.ShouldBe(GamepadButton.B);
    }


    [Fact]
    public void Nes_ThumbBetweenBAndAPressesBoth()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        var b = Visual(engine, "b");
        var a = Visual(engine, "a");

        engine.PointerDown(1, (a.Bounds.CenterX + b.Bounds.CenterX) / 2, a.Bounds.CenterY);

        this.pad.GetState().Buttons.ShouldBe(GamepadButton.A | GamepadButton.B);
    }


    [Fact]
    public void Button_SlidingAThumbMovesThePress()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        var b = Visual(engine, "b");
        var a = Visual(engine, "a");

        engine.PointerDown(1, b.Bounds.CenterX, b.Bounds.CenterY);
        engine.PointerMove(1, a.Bounds.CenterX, a.Bounds.CenterY);

        this.pad.GetState().Buttons.ShouldBe(GamepadButton.B);
        b.IsPressed.ShouldBeFalse();
        a.IsPressed.ShouldBeTrue();
    }


    [Fact]
    public void Snes_CentreOfTheDiamondPressesNothing()
    {
        var engine = this.Create(GamepadLayouts.Snes());
        var x = Visual(engine, "x");
        var b = Visual(engine, "b");

        // the diamond's centre is 44 from every button - further than radius plus slop
        engine.HitTest(x.Bounds.CenterX, (x.Bounds.CenterY + b.Bounds.CenterY) / 2).ShouldBeFalse();
    }


    [Fact]
    public void Snes_AdjacentButtonsChord()
    {
        var engine = this.Create(GamepadLayouts.Snes());
        var b = Visual(engine, "b");   // bottom -> GamepadButton.A
        var a = Visual(engine, "a");   // right  -> GamepadButton.B

        engine.PointerDown(1, (a.Bounds.CenterX + b.Bounds.CenterX) / 2, (a.Bounds.CenterY + b.Bounds.CenterY) / 2);

        this.pad.GetState().Buttons.ShouldBe(GamepadButton.A | GamepadButton.B);
    }


    [Theory]
    [InlineData(1, 0, GamepadButton.DPadRight)]
    [InlineData(0, -1, GamepadButton.DPadUp)]
    [InlineData(-1, 0, GamepadButton.DPadLeft)]
    [InlineData(0, 1, GamepadButton.DPadDown)]
    [InlineData(1, -1, GamepadButton.DPadUp | GamepadButton.DPadRight)]
    [InlineData(-1, 1, GamepadButton.DPadDown | GamepadButton.DPadLeft)]
    public void DPad_EightWay(float dx, float dy, GamepadButton expected)
    {
        var engine = this.Create(GamepadLayouts.Nes());
        var dpad = Visual(engine, "dpad");

        engine.PointerDown(1, dpad.Bounds.CenterX + dx * 40, dpad.Bounds.CenterY + dy * 40);

        this.pad.GetState().Buttons.ShouldBe(expected);
        dpad.PressedDirections.ShouldBe(expected);
    }


    [Fact]
    public void DPad_FourWayNeverReportsADiagonal()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        engine.DPadMode = GamepadDPadMode.FourWay;
        var dpad = Visual(engine, "dpad");

        engine.PointerDown(1, dpad.Bounds.CenterX + 40, dpad.Bounds.CenterY - 36);

        this.pad.GetState().Buttons.ShouldBe(GamepadButton.DPadRight);
    }


    [Fact]
    public void DPad_CentreIsNeutral()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        var dpad = Visual(engine, "dpad");

        engine.PointerDown(1, dpad.Bounds.CenterX + 2, dpad.Bounds.CenterY + 2).ShouldBeTrue();

        this.pad.GetState().Buttons.ShouldBe(GamepadButton.None);
    }


    [Fact]
    public void DPad_KeepsSteeringOffItsEdge()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        var dpad = Visual(engine, "dpad");

        engine.PointerDown(1, dpad.Bounds.CenterX + 40, dpad.Bounds.CenterY);
        engine.PointerMove(1, dpad.Bounds.CenterX + 400, dpad.Bounds.CenterY);

        this.pad.GetState().Buttons.ShouldBe(GamepadButton.DPadRight);
    }


    [Fact]
    public void Stick_YIsPositiveUpAndClampedToTheUnitCircle()
    {
        var engine = this.Create(GamepadLayouts.Standard());
        var ls = Visual(engine, "ls");

        engine.PointerDown(1, ls.Bounds.CenterX, ls.Bounds.CenterY);
        engine.PointerMove(1, ls.Bounds.CenterX, ls.Bounds.CenterY - 1000);

        var stick = this.pad.GetState().LeftStick;
        stick.X.ShouldBe(0, 0.001);
        stick.Y.ShouldBe(1, 0.001);

        // the knob stops at the rim however far the thumb goes
        ls.KnobY.ShouldBe(ls.Bounds.CenterY - ls.Travel, 0.01);
    }


    [Fact]
    public void Stick_HalfTravelReadsHalf()
    {
        var engine = this.Create(GamepadLayouts.Standard());
        var rs = Visual(engine, "rs");

        engine.PointerDown(1, rs.Bounds.CenterX, rs.Bounds.CenterY);
        engine.PointerMove(1, rs.Bounds.CenterX + rs.Travel / 2, rs.Bounds.CenterY);

        this.pad.GetState().RightStick.X.ShouldBe(0.5f, 0.001);
        this.pad.GetState().LeftStick.ShouldBe(GamepadStick.Neutral);
    }


    [Fact]
    public void Stick_ReleaseReturnsToNeutral()
    {
        var engine = this.Create(GamepadLayouts.Standard());
        var ls = Visual(engine, "ls");

        engine.PointerDown(1, ls.Bounds.CenterX + 30, ls.Bounds.CenterY);
        engine.PointerUp(1);

        this.pad.GetState().LeftStick.ShouldBe(GamepadStick.Neutral);
        ls.KnobX.ShouldBe(ls.Bounds.CenterX);
        ls.IsActive.ShouldBeFalse();
    }


    [Fact]
    public void Stick_FloatingCentresOnTheThumb()
    {
        var layout = GamepadLayouts.TwinStick();
        layout.Find("ls")!.IsFloating = true;
        var engine = this.Create(layout);
        var ls = Visual(engine, "ls");

        // well outside the drawn stick but inside its zone
        var downX = ls.Bounds.X - 40;
        var downY = ls.Bounds.Y - 40;
        engine.PointerDown(1, downX, downY).ShouldBeTrue();
        this.pad.GetState().LeftStick.ShouldBe(GamepadStick.Neutral);
        ls.BaseX.ShouldBe(downX);

        engine.PointerMove(1, downX + ls.Travel, downY);
        this.pad.GetState().LeftStick.X.ShouldBe(1, 0.001);
    }


    [Fact]
    public void Stick_DoubleTapHoldClicks()
    {
        var engine = this.Create(GamepadLayouts.Standard());
        var ls = Visual(engine, "ls");

        engine.PointerDown(1, ls.Bounds.CenterX, ls.Bounds.CenterY);
        engine.PointerUp(1);
        this.time.Advance(TimeSpan.FromMilliseconds(150));
        engine.PointerDown(2, ls.Bounds.CenterX, ls.Bounds.CenterY);

        this.pad.GetState().IsPressed(GamepadButton.LeftStick).ShouldBeTrue();
        ls.IsClicked.ShouldBeTrue();

        engine.PointerUp(2);
        this.pad.GetState().IsPressed(GamepadButton.LeftStick).ShouldBeFalse();
    }


    [Fact]
    public void Stick_SlowSecondTapDoesNotClick()
    {
        var engine = this.Create(GamepadLayouts.Standard());
        var ls = Visual(engine, "ls");

        engine.PointerDown(1, ls.Bounds.CenterX, ls.Bounds.CenterY);
        engine.PointerUp(1);
        this.time.Advance(TimeSpan.FromMilliseconds(500));
        engine.PointerDown(2, ls.Bounds.CenterX, ls.Bounds.CenterY);

        this.pad.GetState().IsPressed(GamepadButton.LeftStick).ShouldBeFalse();
    }


    [Fact]
    public void MultiTouch_StickAndButtonTogether()
    {
        var engine = this.Create(GamepadLayouts.Standard());
        var ls = Visual(engine, "ls");
        var a = Visual(engine, "a");

        engine.PointerDown(1, ls.Bounds.CenterX, ls.Bounds.CenterY);
        engine.PointerMove(1, ls.Bounds.CenterX - ls.Travel, ls.Bounds.CenterY);
        engine.PointerDown(2, a.Bounds.CenterX, a.Bounds.CenterY);

        var state = this.pad.GetState();
        state.LeftStick.X.ShouldBe(-1, 0.001);
        state.IsPressed(GamepadButton.A).ShouldBeTrue();

        engine.PointerUp(1);
        this.pad.GetState().IsPressed(GamepadButton.A).ShouldBeTrue();
        this.pad.GetState().LeftStick.ShouldBe(GamepadStick.Neutral);
    }


    [Fact]
    public void Trigger_DrivesItsAxis()
    {
        var engine = this.Create(GamepadLayouts.Standard());
        var rt = Visual(engine, "rt");

        engine.PointerDown(1, rt.Bounds.CenterX, rt.Bounds.CenterY);

        this.pad.GetState().RightTrigger.ShouldBe(1f);
        this.pad.GetState().IsPressed(GamepadButton.RightTrigger).ShouldBeTrue();
        this.pad.GetState().LeftTrigger.ShouldBe(0f);
    }


    [Fact]
    public void Turbo_RepeatsWhileHeldAndStopsOnRelease()
    {
        var layout = GamepadLayouts.Nes();
        layout.Find("a")!.IsTurbo = true;
        var engine = this.Create(layout);
        engine.TurboRate = 10;
        var a = Visual(engine, "a");

        engine.PointerDown(1, a.Bounds.CenterX, a.Bounds.CenterY);
        this.pad.GetState().IsPressed(GamepadButton.B).ShouldBeTrue();

        // 10 presses a second = a toggle every 50ms
        this.time.Advance(TimeSpan.FromMilliseconds(50));
        this.pad.GetState().IsPressed(GamepadButton.B).ShouldBeFalse();
        a.IsPressed.ShouldBeTrue("the finger is still down - only the reported state repeats");

        this.time.Advance(TimeSpan.FromMilliseconds(50));
        this.pad.GetState().IsPressed(GamepadButton.B).ShouldBeTrue();

        engine.PointerUp(1);
        this.time.ActiveTimers.ShouldBe(0);
        this.pad.GetState().Buttons.ShouldBe(GamepadButton.None);

        this.buttonEvents.Count(x => x.IsPressed).ShouldBe(2);
    }


    [Fact]
    public void ElementPressed_FiresOncePerPressNotPerTurboRepeat()
    {
        var layout = GamepadLayouts.Nes();
        layout.Find("a")!.IsTurbo = true;
        var engine = this.Create(layout);
        var presses = 0;
        engine.ElementPressed += (_, _) => presses++;
        var a = Visual(engine, "a");

        engine.PointerDown(1, a.Bounds.CenterX, a.Bounds.CenterY);
        this.time.Advance(TimeSpan.FromMilliseconds(500));

        presses.ShouldBe(1);
    }


    [Fact]
    public void ElementPressed_TellsDirectionsFromButtons()
    {
        // the hosts split haptics on this: a d-pad direction arrives as the DPad element, everything
        // else as the button's own element
        var engine = this.Create(GamepadLayouts.Nes());
        var pressed = new List<GamepadElementKind>();
        engine.ElementPressed += (_, e) => pressed.Add(e.Kind);
        var dpad = Visual(engine, "dpad");
        var a = Visual(engine, "a");

        engine.PointerDown(1, dpad.Bounds.CenterX + 40, dpad.Bounds.CenterY);
        engine.PointerMove(1, dpad.Bounds.CenterX + 40, dpad.Bounds.CenterY - 40);   // adds Up
        engine.PointerDown(2, a.Bounds.CenterX, a.Bounds.CenterY);

        pressed.ShouldBe([GamepadElementKind.DPad, GamepadElementKind.DPad, GamepadElementKind.Button]);
    }


    [Fact]
    public void HitTest_EmptySpacePassesThrough()
    {
        var engine = this.Create(GamepadLayouts.Nes());

        engine.HitTest(W / 2, 40).ShouldBeFalse();
        engine.PointerDown(1, W / 2, 40).ShouldBeFalse();
        this.pad.GetState().Buttons.ShouldBe(GamepadButton.None);
    }


    [Fact]
    public void Disabled_PassesEverythingThroughAndReleases()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        var a = Visual(engine, "a");
        engine.PointerDown(1, a.Bounds.CenterX, a.Bounds.CenterY);

        engine.IsEnabled = false;

        this.pad.GetState().Buttons.ShouldBe(GamepadButton.None);
        engine.HitTest(a.Bounds.CenterX, a.Bounds.CenterY).ShouldBeFalse();
    }


    [Fact]
    public void SwappingTheGamepadReleasesTheOldOne()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        var a = Visual(engine, "a");
        engine.PointerDown(1, a.Bounds.CenterX, a.Bounds.CenterY);

        var replacement = new VirtualGamepad("test");
        engine.Gamepad = replacement;

        this.pad.GetState().Buttons.ShouldBe(GamepadButton.None);
        this.buttonEvents.Last().IsPressed.ShouldBeFalse();
    }


    [Fact]
    public void Edit_DragMovesTheElementAndReportsTheLayout()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        GamepadLayout? edited = null;
        engine.LayoutEdited += (_, l) => edited = l;
        engine.IsEditing = true;
        var a = Visual(engine, "a");
        var start = (a.Bounds.CenterX, a.Bounds.CenterY);

        engine.PointerDown(1, start.CenterX, start.CenterY);
        a.IsSelected.ShouldBeTrue();
        engine.PointerMove(1, start.CenterX - 30, start.CenterY - 20);
        engine.PointerUp(1);

        this.pad.GetState().Buttons.ShouldBe(GamepadButton.None, "editing never plays");
        edited.ShouldNotBeNull();
        edited!.Find("a")!.X.ShouldBe(-78 - 30);
        edited.Find("a")!.Y.ShouldBe(-88 - 20);
        Visual(engine, "a").Bounds.CenterX.ShouldBe(start.CenterX - 30);
    }


    [Fact]
    public void Edit_SecondFingerPinchesToResize()
    {
        var engine = this.Create(GamepadLayouts.Nes());
        engine.IsEditing = true;
        var a = Visual(engine, "a");

        engine.PointerDown(1, a.Bounds.CenterX, a.Bounds.CenterY);
        engine.PointerDown(2, a.Bounds.CenterX - 50, a.Bounds.CenterY);
        engine.PointerMove(2, a.Bounds.CenterX - 100, a.Bounds.CenterY);

        a.Element.Width.ShouldBe(140, 0.01);
    }


    [Fact]
    public void Layout_JsonRoundTrips()
    {
        var layout = GamepadLayouts.Standard();
        layout.Find("ls")!.IsFloating = true;
        layout.Find("a")!.Label = "Jump";

        var copy = GamepadLayout.FromJson(layout.ToJson());

        copy.Elements.Count.ShouldBe(layout.Elements.Count);
        copy.Find("ls")!.IsFloating.ShouldBeTrue();
        copy.Find("a")!.Label.ShouldBe("Jump");
        copy.FaceStyle.ShouldBe(GamepadFaceStyle.Xbox);
        layout.ToJson().ShouldContain("\"Stick\"", customMessage: "enums are written as names so a saved layout survives reordering");
    }


    [Fact]
    public void FaceStyle_RelabelsWithoutRemapping()
    {
        var engine = this.Create(GamepadLayouts.Standard());
        engine.FaceStyle = GamepadFaceStyle.PlayStation;
        var a = Visual(engine, "a");

        a.Face.Glyph.ShouldNotBeNull();
        a.Face.Text.ShouldBeNull();
        engine.PointerDown(1, a.Bounds.CenterX, a.Bounds.CenterY);
        this.pad.GetState().IsPressed(GamepadButton.A).ShouldBeTrue();
    }


    [Theory]
    [InlineData(GamepadPreset.Nes)]
    [InlineData(GamepadPreset.Snes)]
    [InlineData(GamepadPreset.Standard)]
    [InlineData(GamepadPreset.TwinStick)]
    [InlineData(GamepadPreset.Arcade)]
    public void Presets_NoTwoElementsOverlapAndAllFitTheirCanvas(GamepadPreset preset)
    {
        var engine = this.Create(GamepadLayouts.Get(preset));
        engine.Sizing = GamepadSizing.Uniform;
        engine.Arrange(engine.Layout.DesignWidth, engine.Layout.DesignHeight);

        var visuals = engine.Elements;
        foreach (var v in visuals)
        {
            v.Bounds.X.ShouldBeGreaterThanOrEqualTo(0, v.Element.Id);
            v.Bounds.Y.ShouldBeGreaterThanOrEqualTo(0, v.Element.Id);
            v.Bounds.Right.ShouldBeLessThanOrEqualTo(engine.Layout.DesignWidth, v.Element.Id);
            v.Bounds.Bottom.ShouldBeLessThanOrEqualTo(engine.Layout.DesignHeight, v.Element.Id);
        }

        for (var i = 0; i < visuals.Count; i++)
        for (var j = i + 1; j < visuals.Count; j++)
        {
            var a = visuals[i].Bounds;
            var b = visuals[j].Bounds;
            var overlap = a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
            if (overlap && IsRound(visuals[i]) && IsRound(visuals[j]))
            {
                // round elements only collide if their circles do
                var dx = a.CenterX - b.CenterX;
                var dy = a.CenterY - b.CenterY;
                overlap = MathF.Sqrt(dx * dx + dy * dy) < (a.Width + b.Width) / 2;
            }
            overlap.ShouldBeFalse($"{visuals[i].Element.Id} overlaps {visuals[j].Element.Id}");
        }
    }


    static bool IsRound(GamepadElementVisual v) => v.Element.Kind is GamepadElementKind.Stick or GamepadElementKind.DPad
        || v.Element.Shape == GamepadElementShape.Circle;
}
