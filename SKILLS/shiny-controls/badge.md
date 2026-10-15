# BadgeView

A content-wrapping overlay that pins a small badge (count, label, or dot) to one of the four corners of a wrapped view. Use it for unread counts, cart counters, "new" indicators, status dots — anything that needs to sit on top of an avatar, icon, button, or card.

Both hosts mirror the same API. Setting `Text` to an empty string (and leaving `IsDot` false) hides the badge automatically — bind your unread/count value directly and the control shows/clears itself.

## MAUI

`BadgeView` derives from `Grid` and uses `Content` for the wrapped view (it is the `ContentProperty`, so you can write the child directly).

### Count badge with overflow

```xml
<shiny:BadgeView Text="{Binding UnreadCount}" MaxCount="99">
    <Border Stroke="#E5E7EB" StrokeThickness="1" Padding="14,10"
            StrokeShape="RoundRectangle 10">
        <Label Text="📬 Inbox" FontSize="16" />
    </Border>
</shiny:BadgeView>
```

When `UnreadCount` is `"0"` set it to `""` to hide the badge; when it parses as a number above `MaxCount`, the badge renders `"99+"` instead of the raw value.

### Corner positions

```xml
<shiny:BadgeView Text="9" Position="TopLeft" />
<shiny:BadgeView Text="9" Position="TopRight" />     <!-- default -->
<shiny:BadgeView Text="9" Position="BottomLeft" />
<shiny:BadgeView Text="9" Position="BottomRight" />
```

`OffsetX` (default 4) and `OffsetY` (default -4) nudge the badge from the corner. Positive `OffsetX` pushes outward (left/right), positive `OffsetY` pushes downward — sign on Y is preserved so `-4` lifts the badge above the corner.

### Dot indicator

For "has new" / "has unread" states with no count:

```xml
<shiny:BadgeView IsDot="{Binding HasNew}"
                 BadgeColor="#3B82F6"
                 OffsetX="2" OffsetY="2">
    <Border WidthRequest="56" HeightRequest="56"
            StrokeShape="RoundRectangle 28"
            Padding="12">
        <Label Text="👤" FontSize="22"
               HorizontalTextAlignment="Center"
               VerticalTextAlignment="Center" />
    </Border>
</shiny:BadgeView>
```

When `IsDot` is true the `Text`, `MaxCount`, and `BadgePadding` are ignored, and the badge renders as a circle of `DotSize` (default 10).

### Pulse / call attention

```xml
<shiny:BadgeView Text="NEW" IsPulsing="True" BadgeColor="#F59E0B" FontSize="9">
    <Border Padding="14,10" StrokeShape="RoundRectangle 10"
            Stroke="#E5E7EB" StrokeThickness="1">
        <Label Text="✨ Features" FontSize="16" />
    </Border>
</shiny:BadgeView>
```

`IsAnimated` (default true) handles the scale-in/out when the badge appears or disappears; a show that arrives while it is still animating out wins, so a count cleared and set again in quick succession stays visible. `IsPulsing` (default false) runs a continuous gentle scale animation while the badge is visible — leave it off for normal count badges, turn it on for important "look at me" badges.

### BadgeView Properties (MAUI)

| Property | Type | Default | Description |
|---|---|---|---|
| Content | View? | null | The wrapped view (`ContentProperty`) |
| Text | string | "" | Badge text. Empty hides the badge unless `IsDot` is true |
| Position | BadgePosition | TopRight | `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight` |
| BadgeColor | Color | #DC2626 | Badge fill color |
| BadgeTextColor | Color | White | Badge text color |
| BadgeBorderColor | Color | White | Border color (default white creates a clean ring) |
| BadgeBorderThickness | double | 1.5 | Border thickness |
| FontSize | double | 10 | Badge text font size |
| FontAttributes | FontAttributes | Bold | Badge font weight |
| CornerRadius | double | 999 | Default fully rounded pill |
| BadgePadding | Thickness | 6,2 | Inner padding |
| OffsetX | double | 4 | Horizontal nudge from the corner (positive = outward) |
| OffsetY | double | -4 | Vertical nudge from the corner (negative = upward) |
| IsDot | bool | false | When true, renders a small dot — text is ignored |
| DotSize | double | 10 | Dot diameter when `IsDot` is true |
| MaxCount | int | 0 | When > 0 and `Text` parses as a number above this limit, renders `"{MaxCount}+"` |
| IsAnimated | bool | true | Scale/fade in-out when the badge appears or disappears |
| IsPulsing | bool | false | Continuous pulse animation while visible |

## Blazor

The Blazor `BadgeView` wraps `ChildContent` with a `position:relative` host and renders the badge as an absolutely-positioned span. Animations are pure CSS and honor `prefers-reduced-motion`.

### Count badge

```razor
<BadgeView Text="@unreadCount" Position="BadgePosition.TopRight" MaxCount="99">
    <div class="inbox-card">📬 Inbox</div>
</BadgeView>
```

Bind `Text` directly to a string. When `unreadCount` is `""` the badge is hidden.

### Dot indicator

```razor
<BadgeView IsDot="@hasNew" BadgeColor="#3B82F6" OffsetX="2" OffsetY="2">
    <div class="avatar">👤</div>
</BadgeView>
```

### Pulse

```razor
<BadgeView Text="NEW" IsPulsing="true" BadgeColor="#F59E0B" FontSize="9">
    <div class="feature-card">✨ Features</div>
</BadgeView>
```

### BadgeView Parameters (Blazor)

| Parameter | Type | Default | Description |
|---|---|---|---|
| ChildContent | RenderFragment? | — | The wrapped view |
| Text | string? | "" | Badge text. Empty hides the badge unless `IsDot` is true |
| Position | BadgePosition | TopRight | `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight` |
| BadgeColor | string | #DC2626 | Badge fill color (CSS) |
| BadgeTextColor | string | #FFFFFF | Badge text color (CSS) |
| BadgeBorderColor | string | #FFFFFF | Badge border color (CSS) |
| BadgeBorderThickness | double | 1.5 | Border thickness (px) |
| FontSize | double | 10 | Badge text font size (px) |
| FontWeight | string | "700" | Badge text font weight (CSS) |
| CornerRadius | double | 999 | Default fully rounded pill (px) |
| BadgePadding | string | "2px 6px" | Inner padding (CSS) |
| OffsetX | double | 4 | Horizontal nudge from the corner (px) |
| OffsetY | double | -4 | Vertical nudge from the corner (px) |
| IsDot | bool | false | Render a small dot — text is ignored |
| DotSize | double | 10 | Dot diameter (px) when `IsDot` is true |
| MaxCount | int | 0 | When > 0 and `Text` is numeric above this limit, renders `"{MaxCount}+"` |
| IsAnimated | bool | true | CSS scale-in animation on first render |
| IsPulsing | bool | false | Continuous CSS pulse animation while visible |
| CssClass | string? | null | Additional CSS class on the root |

## Notes

- `Position` is an `enum`: `BadgePosition.TopLeft`, `BadgePosition.TopRight`, `BadgePosition.BottomLeft`, `BadgePosition.BottomRight`. Bind it directly (XAML accepts the name; Blazor needs the fully qualified name in markup).
- `Text` being an empty string is the auto-hide signal. Bind your count directly — set the source to `""` (or `null`) to clear the badge.
- `MaxCount` only formats when `Text` parses as an integer. Non-numeric text (e.g. `"NEW"`, `"PRO"`) is shown as-is regardless of `MaxCount`.
- Default `OffsetX=4`, `OffsetY=-4` nudges the badge slightly outside the corner of the wrapped content — typical "hangs off the edge" badge look. Set both to `0` to keep the badge fully inside the corner instead.
- `BadgeBorderColor` defaults to white so the badge reads cleanly against any underlying content; match it to the surrounding background when wrapping inside a colored card.
- Use `IsDot` for unread indicators (no count needed) and `MaxCount` for count badges. Use `IsPulsing` sparingly — it's eye-catching and should be reserved for genuinely important badges.
- The wrapped view is laid out exactly as if the badge weren't there — the badge does not affect the host's measurement.
