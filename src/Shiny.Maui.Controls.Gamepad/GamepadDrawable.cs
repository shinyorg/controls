using System.Numerics;
using Shiny.Controls.Gaming;
using Shiny.Gamepad;

namespace Shiny.Maui.Controls.Gaming;


/// <summary>Draws a <see cref="GamepadEngine"/>'s elements. All the geometry comes from the engine; this only paints it.</summary>
sealed class GamepadDrawable(GamepadView view) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var engine = view.Engine;
        if (view.IsSteppedAside)
            return;

        canvas.Alpha = view.IdleAlpha;
        var scale = engine.VisualScale;

        if ((view.ShowBody ?? view.Sizing == GamepadSizing.Uniform) && engine.Body is { } body)
        {
            canvas.FillColor = view.BodyColor;
            canvas.FillRoundedRectangle(ToRect(body), body.Height * engine.Layout.BodyRoundness);
        }

        foreach (var v in engine.Elements)
        {
            switch (v.Element.Kind)
            {
                case GamepadElementKind.DPad:
                    this.DrawDPad(canvas, v, scale);
                    break;

                case GamepadElementKind.Stick:
                    this.DrawStick(canvas, v, scale);
                    break;

                default:
                    this.DrawButton(canvas, v, scale);
                    break;
            }

            if (engine.IsEditing)
            {
                canvas.StrokeColor = view.AccentColor;
                canvas.StrokeSize = v.IsSelected ? 3 : 1.5f;
                canvas.StrokeDashPattern = v.IsSelected ? null : [6, 4];
                canvas.DrawRoundedRectangle(ToRect(v.Bounds).Inflate(4, 4), 8);
                canvas.StrokeDashPattern = null;
            }
        }
    }


    void DrawButton(ICanvas canvas, GamepadElementVisual v, float scale)
    {
        var rect = ToRect(v.Bounds);
        var fill = Parse(v.Face.Fill) ?? view.ButtonColor;
        canvas.FillColor = v.IsPressed ? Pressed(fill) : fill;
        canvas.StrokeColor = view.OutlineColor;
        canvas.StrokeSize = MathF.Max(1f, 1.5f * scale);

        switch (v.Element.Shape)
        {
            case GamepadElementShape.Circle:
                canvas.FillEllipse(rect);
                canvas.DrawEllipse(rect);
                break;

            case GamepadElementShape.Pill:
                canvas.FillRoundedRectangle(rect, rect.Height / 2);
                canvas.DrawRoundedRectangle(rect, rect.Height / 2);
                break;

            default:
                canvas.FillRoundedRectangle(rect, rect.Height * 0.3f);
                canvas.DrawRoundedRectangle(rect, rect.Height * 0.3f);
                break;
        }

        this.DrawFace(canvas, v.Face, rect, v.Element.Shape == GamepadElementShape.Pill);
    }


    void DrawFace(ICanvas canvas, GamepadButtonFace face, RectF rect, bool isPill)
    {
        var color = Parse(face.Accent) ?? view.LabelColor;

        if (face.Glyph != null)
        {
            var size = MathF.Min(rect.Width, rect.Height) * 0.62f;
            var path = PathBuilder.Build(face.Glyph);
            path.Transform(
                Matrix3x2.CreateScale(size) *
                Matrix3x2.CreateTranslation(rect.Center.X - size / 2, rect.Center.Y - size / 2)
            );
            canvas.StrokeColor = color;
            canvas.StrokeSize = MathF.Max(1.5f, size * 0.1f);
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.DrawPath(path);
            return;
        }

        if (string.IsNullOrEmpty(face.Text))
            return;

        canvas.FontColor = color;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        // long pill labels ("Options", "SELECT") are sized to the width, single letters to the height
        canvas.FontSize = isPill || face.Text.Length > 2
            ? MathF.Min(rect.Height * 0.5f, rect.Width / MathF.Max(3, face.Text.Length) * 1.5f)
            : rect.Height * 0.42f;
        canvas.DrawString(face.Text, rect, HorizontalAlignment.Center, VerticalAlignment.Center);
    }


    void DrawDPad(ICanvas canvas, GamepadElementVisual v, float scale)
    {
        var b = v.Bounds;
        var arm = b.Width / 3f;
        var cx = b.CenterX;
        var cy = b.CenterY;
        var radius = arm * 0.2f;

        canvas.FillColor = view.ButtonColor;
        canvas.StrokeColor = view.OutlineColor;
        canvas.StrokeSize = MathF.Max(1f, 1.5f * scale);

        var horizontal = new RectF(b.X, cy - arm / 2, b.Width, arm);
        var vertical = new RectF(cx - arm / 2, b.Y, arm, b.Height);
        canvas.FillRoundedRectangle(horizontal, radius);
        canvas.FillRoundedRectangle(vertical, radius);

        // one outline around the whole cross, not two overlapping rectangles
        var cross = new PathF();
        var l = b.X; var r = b.Right; var t = b.Y; var bt = b.Bottom;
        var a0 = cx - arm / 2; var a1 = cx + arm / 2; var c0 = cy - arm / 2; var c1 = cy + arm / 2;
        cross.MoveTo(a0, t).LineTo(a1, t).LineTo(a1, c0).LineTo(r, c0).LineTo(r, c1).LineTo(a1, c1)
             .LineTo(a1, bt).LineTo(a0, bt).LineTo(a0, c1).LineTo(l, c1).LineTo(l, c0).LineTo(a0, c0).Close();
        canvas.DrawPath(cross);

        canvas.FillColor = view.PressedColor;
        if (v.PressedDirections.HasFlag(GamepadButton.DPadUp))
            canvas.FillRoundedRectangle(new RectF(a0, t, arm, arm), radius);
        if (v.PressedDirections.HasFlag(GamepadButton.DPadDown))
            canvas.FillRoundedRectangle(new RectF(a0, bt - arm, arm, arm), radius);
        if (v.PressedDirections.HasFlag(GamepadButton.DPadLeft))
            canvas.FillRoundedRectangle(new RectF(l, c0, arm, arm), radius);
        if (v.PressedDirections.HasFlag(GamepadButton.DPadRight))
            canvas.FillRoundedRectangle(new RectF(r - arm, c0, arm, arm), radius);

        // direction arrows, one per arm
        canvas.FillColor = view.LabelColor.WithAlpha(0.8f);
        var s = arm * 0.22f;
        Arrow(canvas, cx, t + arm / 2, 0, -1, s);
        Arrow(canvas, cx, bt - arm / 2, 0, 1, s);
        Arrow(canvas, l + arm / 2, cy, -1, 0, s);
        Arrow(canvas, r - arm / 2, cy, 1, 0, s);
    }


    static void Arrow(ICanvas canvas, float x, float y, float dx, float dy, float size)
    {
        var path = new PathF();
        path.MoveTo(x + dx * size, y + dy * size);
        path.LineTo(x - dy * size - dx * size * 0.6f, y + dx * size - dy * size * 0.6f);
        path.LineTo(x + dy * size - dx * size * 0.6f, y - dx * size - dy * size * 0.6f);
        path.Close();
        canvas.FillPath(path);
    }


    void DrawStick(ICanvas canvas, GamepadElementVisual v, float scale)
    {
        var travel = v.Travel;
        var baseRect = new RectF(v.BaseX - travel, v.BaseY - travel, travel * 2, travel * 2);

        // a floating stick at rest is only a hint of where the zone is
        var restingFloat = v.Element.IsFloating && !v.IsActive;
        canvas.Alpha = view.IdleAlpha * (restingFloat ? 0.55f : 1f);

        canvas.FillColor = view.ButtonColor;
        canvas.StrokeColor = view.OutlineColor;
        canvas.StrokeSize = MathF.Max(1f, 1.5f * scale);
        canvas.FillEllipse(baseRect);
        canvas.DrawEllipse(baseRect);

        var knob = travel * 0.62f;
        var knobRect = new RectF(v.KnobX - knob, v.KnobY - knob, knob * 2, knob * 2);
        canvas.FillColor = v.IsActive ? view.PressedColor : Pressed(view.ButtonColor);
        canvas.FillEllipse(knobRect);
        canvas.DrawEllipse(knobRect);

        if (v.IsClicked)
        {
            canvas.StrokeColor = view.AccentColor;
            canvas.StrokeSize = MathF.Max(2f, 3f * scale);
            canvas.DrawEllipse(knobRect.Inflate(-3, -3));
        }

        canvas.Alpha = view.IdleAlpha;
    }


    static RectF ToRect(GamepadRect r) => new(r.X, r.Y, r.Width, r.Height);


    static Color Pressed(Color fill)
        // lighten toward white, keeping the colour recognisable - a red A still reads as the red A
        => new(fill.Red + (1 - fill.Red) * 0.35f, fill.Green + (1 - fill.Green) * 0.35f, fill.Blue + (1 - fill.Blue) * 0.35f, MathF.Min(1f, fill.Alpha + 0.2f));


    static readonly Dictionary<string, Color> parsed = new();

    static Color? Parse(string? hex)
    {
        if (hex == null)
            return null;

        lock (parsed)
        {
            if (!parsed.TryGetValue(hex, out var color))
                parsed[hex] = color = Color.FromArgb(hex);

            return color;
        }
    }
}
