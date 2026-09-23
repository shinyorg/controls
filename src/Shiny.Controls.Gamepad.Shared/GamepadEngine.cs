using Shiny.Gamepad;

namespace Shiny.Controls.Gaming;


/// <summary>
/// Turns raw pointers into gamepad state: arranges a <see cref="GamepadLayout"/> into a view, decides
/// which element each finger is on, and pushes the result into a <see cref="VirtualGamepad"/>.
/// </summary>
/// <remarks>
/// <para>The hosts do nothing but forward pointers (with a stable id per finger, in the view's
/// device-independent units) and draw <see cref="Elements"/>. Everything a player can feel - which
/// button a thumb between two buttons presses, how far a stick has to move, when a d-pad diagonal
/// starts - is decided here, once, for both hosts.</para>
/// <para>How a finger is read depends on what it first lands on. A finger that lands on a stick or a
/// d-pad is captured by it until it lifts, so a thumb that drifts off the edge keeps steering. A finger
/// that lands on a button can slide onto its neighbours and presses every button whose hit circle it
/// is inside - rolling a thumb across the NES B and A presses both, as it does on the real pad.</para>
/// <para>Thread-safe: pointers arrive on the UI thread and turbo repeats on a timer thread, and both
/// go through one lock, so state is always pushed in order.</para>
/// </remarks>
public sealed class GamepadEngine : IDisposable
{
    /// <summary>How close together two taps on a stick must be to click it.</summary>
    public static readonly TimeSpan DoubleTapWindow = TimeSpan.FromMilliseconds(300);

    readonly object gate = new();
    readonly TimeProvider time;
    readonly Dictionary<long, Capture> captures = new();
    readonly Dictionary<int, long> lastStickRelease = new();
    GamepadElementVisual[] visuals = [];
    GamepadLayout layout = new();
    VirtualGamepad gamepad;
    float width, height;
    ITimer? turboTimer;
    bool turboPhase = true;
    bool isEditing;
    bool isEnabled = true;


    /// <summary>Creates an engine feeding <paramref name="gamepad"/>.</summary>
    /// <param name="gamepad">The pad the state goes to.</param>
    /// <param name="time">Clock for double-taps and turbo. Tests pass a fake one.</param>
    public GamepadEngine(VirtualGamepad gamepad, TimeProvider? time = null)
    {
        this.gamepad = gamepad;
        this.time = time ?? TimeProvider.System;
    }


    /// <summary>Raised when anything drawn has changed. May be raised off the UI thread.</summary>
    public event EventHandler? Invalidated;

    /// <summary>
    /// Raised when a finger newly presses a button, trigger or d-pad direction - the moment for a
    /// haptic tick. Not raised for turbo repeats, which would buzz continuously.
    /// </summary>
    public event EventHandler<GamepadElement>? ElementPressed;

    /// <summary>Raised when an edit-mode move or resize finishes, with the layout that now holds the change.</summary>
    public event EventHandler<GamepadLayout>? LayoutEdited;


    /// <summary>The pad state is pushed into. Swapping it releases everything held on the old one first.</summary>
    public VirtualGamepad Gamepad
    {
        get => this.gamepad;
        set
        {
            lock (this.gate)
            {
                if (ReferenceEquals(this.gamepad, value))
                    return;

                this.ReleaseAllCore();
                this.gamepad = value;
                value.SetSupportedButtons(this.layout.SupportedButtons);
            }
            this.RaiseInvalidated();
        }
    }


    /// <summary>
    /// The layout being played. The engine reads and, in edit mode, writes it - pass a copy you own
    /// (every <see cref="GamepadLayouts"/> call already returns one).
    /// </summary>
    public GamepadLayout Layout
    {
        get => this.layout;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (this.gate)
            {
                this.ReleaseAllCore();
                this.lastStickRelease.Clear();
                this.layout = value;
                this.visuals = value.Elements.Select((e, i) => new GamepadElementVisual(e, i)).ToArray();
                this.gamepad.SetSupportedButtons(value.SupportedButtons);
                this.ArrangeCore();
            }
            this.RaiseInvalidated();
        }
    }


    /// <summary>Overrides the layout's own face style. Null uses <see cref="GamepadLayout.FaceStyle"/>.</summary>
    public GamepadFaceStyle? FaceStyle
    {
        get;
        set
        {
            lock (this.gate)
            {
                field = value;
                this.ArrangeCore();
            }
            this.RaiseInvalidated();
        }
    }

    /// <summary>The face style in effect.</summary>
    public GamepadFaceStyle EffectiveFaceStyle => this.FaceStyle ?? this.layout.FaceStyle;

    /// <summary>How the layout is fitted to the view.</summary>
    public GamepadSizing Sizing
    {
        get;
        set => this.Rearrange(() => field = value);
    } = GamepadSizing.Anchored;

    /// <summary>Multiplies every size and offset in <see cref="GamepadSizing.Anchored"/> mode - the "controller size" setting.</summary>
    public float Scale
    {
        get;
        set => this.Rearrange(() => field = Math.Clamp(value, 0.25f, 4f));
    } = 1f;

    /// <summary>How far outside its drawn edge a touch still presses a button, in device-independent units. Not scaled - a thumb is the same size whatever the controller's scale.</summary>
    public float HitSlop
    {
        get;
        set => this.Rearrange(() => field = Math.Max(0, value));
    } = 10f;

    /// <summary>How a d-pad resolves diagonals.</summary>
    public GamepadDPadMode DPadMode { get; set; } = GamepadDPadMode.EightWay;

    /// <summary>
    /// For a d-pad, the fraction of its radius around the centre that reads as no direction - a thumb
    /// resting in the middle should not be walking anywhere.
    /// </summary>
    public float DPadDeadzone { get; set; } = 0.2f;

    /// <summary>Whether a double-tap-and-hold on a stick clicks it (L3/R3).</summary>
    public bool StickClickEnabled { get; set; } = true;

    /// <summary>Presses per second for turbo buttons.</summary>
    public float TurboRate
    {
        get;
        set => field = Math.Clamp(value, 1f, 30f);
    } = 10f;

    /// <summary>The body rectangle drawn behind the elements, or null in <see cref="GamepadSizing.Anchored"/> mode.</summary>
    public GamepadRect? Body { get; private set; }

    /// <summary>The factor every design size was multiplied by - renderers scale strokes and fonts with it.</summary>
    public float VisualScale { get; private set; } = 1f;

    /// <summary>The elements as they should be drawn now, in draw order.</summary>
    public IReadOnlyList<GamepadElementVisual> Elements => this.visuals;


    /// <summary>
    /// When true, fingers move elements (and a second finger pinches the one being moved to resize it)
    /// instead of playing. Everything held is released on the way in.
    /// </summary>
    public bool IsEditing
    {
        get => this.isEditing;
        set
        {
            lock (this.gate)
            {
                if (this.isEditing == value)
                    return;

                this.ReleaseAllCore();
                this.isEditing = value;
            }
            this.RaiseInvalidated();
        }
    }


    /// <summary>
    /// When false the engine reports nothing and hit-tests nothing, so every touch passes through - used
    /// while the view steps aside for a physical controller.
    /// </summary>
    public bool IsEnabled
    {
        get => this.isEnabled;
        set
        {
            lock (this.gate)
            {
                if (this.isEnabled == value)
                    return;

                this.ReleaseAllCore();
                this.isEnabled = value;
            }
            this.RaiseInvalidated();
        }
    }


    /// <summary>Whether any finger is down.</summary>
    public bool IsTouched
    {
        get
        {
            lock (this.gate)
                return this.captures.Count > 0;
        }
    }


    /// <summary>Lays the layout out for a view of this size.</summary>
    public void Arrange(float width, float height)
    {
        lock (this.gate)
        {
            if (this.width == width && this.height == height)
                return;

            this.width = width;
            this.height = height;
            this.ArrangeCore();
        }
        this.RaiseInvalidated();
    }


    /// <summary>
    /// Whether a touch at this point would land on something. Hosts use it to let every other touch fall
    /// through to whatever is beneath an overlay.
    /// </summary>
    public bool HitTest(float x, float y)
    {
        lock (this.gate)
        {
            if (!this.isEnabled)
                return false;

            if (this.isEditing)
                return this.FindEditTarget(x, y) >= 0;

            return this.FindTarget(x, y).Kind != CaptureKind.None;
        }
    }


    /// <summary>A finger went down. Returns whether it landed on something - a host can pass a miss through.</summary>
    public bool PointerDown(long id, float x, float y)
    {
        bool hit;
        lock (this.gate)
        {
            if (!this.isEnabled)
                return false;

            // a platform that loses a pointer-up (a cancelled gesture, a lost capture) must not leave a
            // phantom finger holding a button - a reused id simply replaces its capture
            this.captures.Remove(id);

            hit = this.isEditing ? this.BeginEdit(id, x, y) : this.BeginPlay(id, x, y);
            if (hit && !this.isEditing)
                this.Publish();
        }
        if (hit)
            this.RaiseInvalidated();

        return hit;
    }


    /// <summary>A finger moved.</summary>
    public void PointerMove(long id, float x, float y)
    {
        lock (this.gate)
        {
            if (!this.captures.TryGetValue(id, out var capture))
                return;

            capture.X = x;
            capture.Y = y;

            if (this.isEditing)
            {
                this.MoveEdit(capture);
            }
            else
            {
                if (capture.Kind == CaptureKind.Buttons)
                    capture.Elements = this.ButtonsAt(x, y);

                this.Publish();
            }
        }
        this.RaiseInvalidated();
    }


    /// <summary>A finger lifted.</summary>
    public void PointerUp(long id) => this.EndPointer(id, completed: true);


    /// <summary>The platform took a finger away (a system gesture, a lost capture). Treated as a lift, without finishing an edit.</summary>
    public void PointerCancel(long id) => this.EndPointer(id, completed: false);


    /// <summary>Lifts every finger - what a host calls when the view is hidden or detached.</summary>
    public void ReleaseAll()
    {
        lock (this.gate)
            this.ReleaseAllCore();

        this.RaiseInvalidated();
    }


    /// <inheritdoc/>
    public void Dispose()
    {
        lock (this.gate)
        {
            this.ReleaseAllCore();
            this.turboTimer?.Dispose();
            this.turboTimer = null;
        }
    }


    #region Arrange

    void Rearrange(Action set)
    {
        lock (this.gate)
        {
            set();
            this.ArrangeCore();
        }
        this.RaiseInvalidated();
    }


    void ArrangeCore()
    {
        float scale, originX, originY, canvasW, canvasH;
        if (this.Sizing == GamepadSizing.Uniform && this.layout.DesignWidth > 0 && this.layout.DesignHeight > 0)
        {
            canvasW = this.layout.DesignWidth;
            canvasH = this.layout.DesignHeight;
            scale = MathF.Max(0.01f, MathF.Min(this.width / canvasW, this.height / canvasH));
            originX = (this.width - canvasW * scale) / 2f;
            originY = (this.height - canvasH * scale) / 2f;
            this.Body = new GamepadRect(originX, originY, canvasW * scale, canvasH * scale);
        }
        else
        {
            // anchored: the "canvas" is the view itself, measured in unscaled units so offsets scale
            // around their anchor rather than around the view's top-left
            // a layout is designed for a canvas; on a view narrower than that (a portrait phone is
            // ~400 wide against Standard's 620) the left and right clusters would meet in the middle,
            // so shrink to fit - never grow, the natural size is the thumb-sized one
            var fit = 1f;
            if (this.layout.DesignWidth > 0 && this.layout.DesignHeight > 0 && this.width > 0 && this.height > 0)
                fit = MathF.Min(1f, MathF.Min(this.width / this.layout.DesignWidth, this.height / this.layout.DesignHeight));

            scale = this.Scale * fit;
            originX = 0;
            originY = 0;
            canvasW = this.width / scale;
            canvasH = this.height / scale;
            this.Body = null;
        }

        this.VisualScale = scale;
        var style = this.EffectiveFaceStyle;

        foreach (var v in this.visuals)
        {
            var e = v.Element;
            var (ax, ay) = AnchorPoint(e.Anchor, canvasW, canvasH);
            var cx = originX + (ax + e.X) * scale;
            var cy = originY + (ay + e.Y) * scale;

            v.Bounds = GamepadRect.FromCenter(cx, cy, e.Width * scale, e.Height * scale);
            v.HitBounds = e.Kind == GamepadElementKind.Stick && e.IsFloating
                ? v.Bounds.Inflate(e.FloatingZone * scale)
                : v.Bounds.Inflate(this.HitSlop);

            var face = GamepadFaces.Get(style, e.Button);
            v.Face = face with
            {
                Text = e.Label ?? face.Text,
                Glyph = e.Label != null ? null : face.Glyph,
                Fill = e.Color ?? face.Fill
            };

            v.Travel = MathF.Min(v.Bounds.Width, v.Bounds.Height) / 2f;
            if (!v.IsActive)
                this.RestStick(v);
        }

        // a finger already down keeps its capture across a rotation; its stick just recentres on the
        // next move, which is what the player expects when the screen turns under their thumb
    }


    static (float X, float Y) AnchorPoint(GamepadAnchor anchor, float w, float h) => anchor switch
    {
        GamepadAnchor.TopLeft => (0, 0),
        GamepadAnchor.Top => (w / 2f, 0),
        GamepadAnchor.TopRight => (w, 0),
        GamepadAnchor.Left => (0, h / 2f),
        GamepadAnchor.Center => (w / 2f, h / 2f),
        GamepadAnchor.Right => (w, h / 2f),
        GamepadAnchor.BottomLeft => (0, h),
        GamepadAnchor.Bottom => (w / 2f, h),
        GamepadAnchor.BottomRight => (w, h),
        _ => (0, 0)
    };


    void RestStick(GamepadElementVisual v)
    {
        v.BaseX = v.KnobX = v.Bounds.CenterX;
        v.BaseY = v.KnobY = v.Bounds.CenterY;
    }

    #endregion

    #region Play

    enum CaptureKind { None, Buttons, DPad, Stick, Edit, Pinch }


    sealed class Capture
    {
        public CaptureKind Kind;
        public int Element = -1;
        public int[] Elements = [];
        public float X, Y;
        public float OriginX, OriginY;
        public bool Clicked;

        // edit
        public float StartX, StartY, StartOffsetX, StartOffsetY;
        public long? PinchPartner;
        public float PinchStartDistance, PinchStartWidth, PinchStartHeight;
    }


    readonly record struct Target(CaptureKind Kind, int Element);


    Target FindTarget(float x, float y)
    {
        // 1. anything actually drawn under the finger, topmost first
        for (var i = this.visuals.Length - 1; i >= 0; i--)
        {
            if (this.visuals[i].Bounds.Contains(x, y) && this.IsInside(this.visuals[i], x, y, 0))
                return new(KindFor(this.visuals[i].Element), i);
        }

        // 2. a button's slop - chords between neighbours come from here
        for (var i = this.visuals.Length - 1; i >= 0; i--)
        {
            var e = this.visuals[i].Element;
            if (e.Kind is GamepadElementKind.Button or GamepadElementKind.Trigger && this.IsInside(this.visuals[i], x, y, this.HitSlop))
                return new(CaptureKind.Buttons, i);
        }

        // 3. a stick's or d-pad's slop, 4. a floating stick's zone
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = this.visuals.Length - 1; i >= 0; i--)
            {
                var v = this.visuals[i];
                if (v.Element.Kind is not (GamepadElementKind.Stick or GamepadElementKind.DPad))
                    continue;

                var rect = pass == 0 ? v.Bounds.Inflate(this.HitSlop) : v.HitBounds;
                if (rect.Contains(x, y))
                    return new(KindFor(v.Element), i);
            }
        }

        return new(CaptureKind.None, -1);
    }


    static CaptureKind KindFor(GamepadElement e) => e.Kind switch
    {
        GamepadElementKind.DPad => CaptureKind.DPad,
        GamepadElementKind.Stick => CaptureKind.Stick,
        _ => CaptureKind.Buttons
    };


    bool IsInside(GamepadElementVisual v, float x, float y, float slop)
    {
        var b = v.Bounds;
        if (v.Element.Kind == GamepadElementKind.Button && v.Element.Shape == GamepadElementShape.Circle)
        {
            // circles hit as circles, so the gap in the middle of a diamond presses nothing while the
            // gap between two neighbours presses both
            var r = MathF.Min(b.Width, b.Height) / 2f + slop;
            var dx = x - b.CenterX;
            var dy = y - b.CenterY;
            return dx * dx + dy * dy <= r * r;
        }
        return b.Inflate(slop).Contains(x, y);
    }


    int[] ButtonsAt(float x, float y)
    {
        List<int>? hits = null;
        for (var i = 0; i < this.visuals.Length; i++)
        {
            var v = this.visuals[i];
            if (v.Element.Kind is GamepadElementKind.Button or GamepadElementKind.Trigger && this.IsInside(v, x, y, this.HitSlop))
                (hits ??= []).Add(i);
        }
        return hits?.ToArray() ?? [];
    }


    bool BeginPlay(long id, float x, float y)
    {
        var target = this.FindTarget(x, y);
        if (target.Kind == CaptureKind.None)
            return false;

        var capture = new Capture { Kind = target.Kind, Element = target.Element, X = x, Y = y };
        switch (target.Kind)
        {
            case CaptureKind.Buttons:
                capture.Elements = this.ButtonsAt(x, y);
                if (capture.Elements.Length == 0)
                    capture.Elements = [target.Element];
                break;

            case CaptureKind.Stick:
                var v = this.visuals[target.Element];
                if (v.Element.IsFloating)
                {
                    capture.OriginX = x;
                    capture.OriginY = y;
                }
                else
                {
                    capture.OriginX = v.Bounds.CenterX;
                    capture.OriginY = v.Bounds.CenterY;
                }

                var now = this.time.GetTimestamp();
                if (this.StickClickEnabled &&
                    this.lastStickRelease.TryGetValue(target.Element, out var released) &&
                    this.time.GetElapsedTime(released, now) <= DoubleTapWindow)
                {
                    capture.Clicked = true;
                }
                break;
        }

        this.captures[id] = capture;
        return true;
    }


    void EndPointer(long id, bool completed)
    {
        GamepadLayout? edited = null;
        lock (this.gate)
        {
            if (!this.captures.Remove(id, out var capture))
                return;

            if (this.isEditing)
            {
                // a pinch partner lifting just ends the pinch; the mover keeps moving
                foreach (var other in this.captures.Values)
                {
                    if (other.PinchPartner == id)
                    {
                        other.PinchPartner = null;
                        other.StartX = other.X;
                        other.StartY = other.Y;
                        var e = this.visuals[other.Element].Element;
                        other.StartOffsetX = e.X;
                        other.StartOffsetY = e.Y;
                    }
                }

                if (capture.Kind == CaptureKind.Edit)
                {
                    this.visuals[capture.Element].IsSelected = false;
                    if (completed)
                        edited = this.layout;
                }
            }
            else
            {
                if (capture.Kind == CaptureKind.Stick && !capture.Clicked)
                    this.lastStickRelease[capture.Element] = this.time.GetTimestamp();

                this.Publish();
            }
        }

        this.RaiseInvalidated();
        if (edited != null)
            this.LayoutEdited?.Invoke(this, edited);
    }


    void ReleaseAllCore()
    {
        var hadCaptures = this.captures.Count > 0;
        this.captures.Clear();
        foreach (var v in this.visuals)
            v.IsSelected = false;

        if (hadCaptures || this.gamepad.GetState().Buttons != GamepadButton.None)
            this.Publish();
        else
            this.ResetVisuals();
    }


    void ResetVisuals()
    {
        foreach (var v in this.visuals)
        {
            v.IsPressed = false;
            v.PressedDirections = GamepadButton.None;
            v.IsActive = false;
            v.IsClicked = false;
            this.RestStick(v);
        }
    }


    /// <summary>
    /// Recomputes the pad's state from every capture, updates the visuals to match, and pushes it. The
    /// one place state is produced, so a timer tick and a pointer can never disagree about it.
    /// </summary>
    void Publish()
    {
        var previouslyPressed = new bool[this.visuals.Length];
        var previousDirections = GamepadButton.None;
        for (var i = 0; i < this.visuals.Length; i++)
        {
            previouslyPressed[i] = this.visuals[i].IsPressed;
            previousDirections |= this.visuals[i].PressedDirections;
        }
        this.ResetVisuals();

        var buttons = GamepadButton.None;
        GamepadStick left = default, right = default;
        float leftTrigger = 0, rightTrigger = 0;
        var anyTurbo = false;

        foreach (var capture in this.captures.Values)
        {
            switch (capture.Kind)
            {
                case CaptureKind.Buttons:
                    foreach (var index in capture.Elements)
                    {
                        var v = this.visuals[index];
                        v.IsPressed = true;

                        if (v.Element.IsTurbo)
                        {
                            anyTurbo = true;
                            if (!this.turboPhase)
                                continue;
                        }

                        buttons |= v.Element.Button;
                        if (v.Element.Kind == GamepadElementKind.Trigger)
                        {
                            if (v.Element.Button.HasFlag(GamepadButton.LeftTrigger))
                                leftTrigger = 1f;
                            if (v.Element.Button.HasFlag(GamepadButton.RightTrigger))
                                rightTrigger = 1f;
                        }
                    }
                    break;

                case CaptureKind.DPad:
                    var dpad = this.visuals[capture.Element];
                    var directions = this.ResolveDPad(dpad, capture.X, capture.Y);
                    dpad.PressedDirections |= directions;
                    dpad.IsActive = true;
                    buttons |= directions;
                    break;

                case CaptureKind.Stick:
                    var stick = this.visuals[capture.Element];
                    var value = this.ResolveStick(stick, capture);
                    if (stick.Element.Button == GamepadButton.RightStick)
                        right = value;
                    else
                        left = value;

                    if (capture.Clicked)
                    {
                        stick.IsClicked = true;
                        buttons |= stick.Element.Button;
                    }
                    break;
            }
        }

        this.SetTurbo(anyTurbo);

        this.gamepad.Push(new GamepadState
        {
            Buttons = buttons,
            LeftStick = left,
            RightStick = right,
            LeftTrigger = leftTrigger,
            RightTrigger = rightTrigger
        });

        // haptics: only for presses a finger made, never for a turbo repeat or a release
        List<GamepadElement>? pressed = null;
        for (var i = 0; i < this.visuals.Length; i++)
        {
            var v = this.visuals[i];
            if (v.IsPressed && !previouslyPressed[i])
                (pressed ??= []).Add(v.Element);
            else if ((v.PressedDirections & ~previousDirections) != GamepadButton.None)
                (pressed ??= []).Add(v.Element);
        }
        if (pressed != null && this.ElementPressed != null)
        {
            foreach (var e in pressed)
                this.ElementPressed.Invoke(this, e);
        }
    }


    GamepadButton ResolveDPad(GamepadElementVisual v, float x, float y)
    {
        var dx = x - v.Bounds.CenterX;
        var dy = y - v.Bounds.CenterY;
        var radius = MathF.Min(v.Bounds.Width, v.Bounds.Height) / 2f;
        if (dx * dx + dy * dy < MathF.Pow(radius * this.DPadDeadzone, 2))
            return GamepadButton.None;

        // screen y grows downward; the angle is measured with up positive, 0 pointing right
        var angle = MathF.Atan2(-dy, dx) * 180f / MathF.PI;
        if (angle < 0)
            angle += 360f;

        if (this.DPadMode == GamepadDPadMode.FourWay)
        {
            return ((int)MathF.Round(angle / 90f) % 4) switch
            {
                0 => GamepadButton.DPadRight,
                1 => GamepadButton.DPadUp,
                2 => GamepadButton.DPadLeft,
                _ => GamepadButton.DPadDown
            };
        }

        return ((int)MathF.Round(angle / 45f) % 8) switch
        {
            0 => GamepadButton.DPadRight,
            1 => GamepadButton.DPadRight | GamepadButton.DPadUp,
            2 => GamepadButton.DPadUp,
            3 => GamepadButton.DPadUp | GamepadButton.DPadLeft,
            4 => GamepadButton.DPadLeft,
            5 => GamepadButton.DPadLeft | GamepadButton.DPadDown,
            6 => GamepadButton.DPadDown,
            _ => GamepadButton.DPadDown | GamepadButton.DPadRight
        };
    }


    GamepadStick ResolveStick(GamepadElementVisual v, Capture capture)
    {
        var travel = MathF.Max(1f, v.Travel);
        var dx = (capture.X - capture.OriginX) / travel;
        var dy = (capture.Y - capture.OriginY) / travel;

        var magnitude = MathF.Sqrt(dx * dx + dy * dy);
        if (magnitude > 1f)
        {
            dx /= magnitude;
            dy /= magnitude;
        }

        v.IsActive = true;
        v.BaseX = capture.OriginX;
        v.BaseY = capture.OriginY;
        v.KnobX = capture.OriginX + dx * travel;
        v.KnobY = capture.OriginY + dy * travel;

        // Shiny.Gamepad reports Y positive upward on every backend; the screen's Y grows downward
        return new GamepadStick(dx, -dy);
    }


    void SetTurbo(bool active)
    {
        if (active)
        {
            if (this.turboTimer != null)
                return;

            this.turboPhase = true;
            var half = TimeSpan.FromSeconds(1.0 / (this.TurboRate * 2));
            this.turboTimer = this.time.CreateTimer(_ => this.OnTurboTick(), null, half, half);
        }
        else if (this.turboTimer != null)
        {
            this.turboTimer.Dispose();
            this.turboTimer = null;
            this.turboPhase = true;
        }
    }


    void OnTurboTick()
    {
        lock (this.gate)
        {
            if (this.turboTimer == null)
                return;

            this.turboPhase = !this.turboPhase;
            this.Publish();
        }
    }

    #endregion

    #region Edit

    int FindEditTarget(float x, float y)
    {
        for (var i = this.visuals.Length - 1; i >= 0; i--)
        {
            if (this.visuals[i].Bounds.Inflate(this.HitSlop).Contains(x, y))
                return i;
        }
        return -1;
    }


    bool BeginEdit(long id, float x, float y)
    {
        // a second finger while one is moving an element pinches that element, wherever it lands
        foreach (var other in this.captures.Values)
        {
            if (other.Kind != CaptureKind.Edit || other.PinchPartner != null)
                continue;

            var e = this.visuals[other.Element].Element;
            other.PinchPartner = id;
            other.PinchStartDistance = MathF.Max(1f, Distance(other.X, other.Y, x, y));
            other.PinchStartWidth = e.Width;
            other.PinchStartHeight = e.Height;
            this.captures[id] = new Capture { Kind = CaptureKind.Pinch, X = x, Y = y };
            return true;
        }

        var index = this.FindEditTarget(x, y);
        if (index < 0)
            return false;

        var element = this.visuals[index].Element;
        this.visuals[index].IsSelected = true;
        this.captures[id] = new Capture
        {
            Kind = CaptureKind.Edit,
            Element = index,
            X = x,
            Y = y,
            StartX = x,
            StartY = y,
            StartOffsetX = element.X,
            StartOffsetY = element.Y
        };
        return true;
    }


    void MoveEdit(Capture moved)
    {
        // a pinch finger carries no element of its own; whichever finger moved, every mover re-reads
        foreach (var capture in this.captures.Values)
        {
            if (capture.Kind != CaptureKind.Edit)
                continue;

            var e = this.visuals[capture.Element].Element;
            if (capture.PinchPartner is { } partnerId && this.captures.TryGetValue(partnerId, out var partner))
            {
                var ratio = Distance(capture.X, capture.Y, partner.X, partner.Y) / capture.PinchStartDistance;
                e.Width = Math.Clamp(capture.PinchStartWidth * ratio, 20f, 400f);
                e.Height = Math.Clamp(capture.PinchStartHeight * ratio, 20f, 400f);
            }
            else if (ReferenceEquals(capture, moved))
            {
                e.X = capture.StartOffsetX + (capture.X - capture.StartX) / this.VisualScale;
                e.Y = capture.StartOffsetY + (capture.Y - capture.StartY) / this.VisualScale;
            }
        }
        this.ArrangeCore();
    }


    static float Distance(float x1, float y1, float x2, float y2)
        => MathF.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

    #endregion


    void RaiseInvalidated() => this.Invalidated?.Invoke(this, EventArgs.Empty);
}
