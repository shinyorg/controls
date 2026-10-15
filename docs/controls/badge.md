# BadgeView

[← All Shiny Controls](../../README.md)

Wraps a single content view and overlays a small notification badge at any of the four corners. Available on both MAUI and Blazor. Setting `Text` to an empty string (and leaving `IsDot` false) hides the badge — bind your unread/cart/count value directly and it shows/clears itself.

```xml
<shiny:BadgeView Text="{Binding UnreadCount}"
                 Position="TopRight"
                 MaxCount="99"
                 BadgeColor="#DC2626"
                 BadgeTextColor="White"
                 BadgeBorderColor="White">
    <Border Stroke="#E5E7EB" StrokeThickness="1" Padding="14,10"
            StrokeShape="RoundRectangle 10">
        <Label Text="📬 Inbox" FontSize="16" />
    </Border>
</shiny:BadgeView>
```

```razor
<BadgeView Text="@unreadCount" Position="BadgePosition.TopRight" MaxCount="99"
           BadgeColor="#DC2626" BadgeTextColor="#FFFFFF" BadgeBorderColor="#FFFFFF">
    <div class="inbox-card">📬 Inbox</div>
</BadgeView>
```

| Property | Type (MAUI / Blazor) | Default | Description |
|---|---|---|---|
| Content / ChildContent | View / RenderFragment | null | The wrapped view the badge overlays |
| Text | string | "" | Badge text. Empty hides the badge unless `IsDot` is true |
| Position | BadgePosition | TopRight | Corner anchor: `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight` |
| BadgeColor | Color / string | #DC2626 | Badge fill color |
| BadgeTextColor | Color / string | White | Badge text color |
| BadgeBorderColor | Color / string | White | Border color (creates a clean ring around the badge) |
| BadgeBorderThickness | double | 1.5 | Border thickness |
| FontSize | double | 10 | Badge text font size |
| FontAttributes / FontWeight | FontAttributes / string | Bold / "700" | Font weight |
| CornerRadius | double | 999 | Badge corner radius (default fully rounded pill) |
| BadgePadding | Thickness / string | 6,2 / "2px 6px" | Inner padding |
| OffsetX | double | 4 | Horizontal nudge from the corner (positive = outward) |
| OffsetY | double | -4 | Vertical nudge from the corner (negative = upward) |
| IsDot | bool | false | When true, renders a small dot (text is ignored) — for "has new" indicators |
| DotSize | double | 10 | Dot diameter (when `IsDot` is true) |
| MaxCount | int | 0 | When > 0 and `Text` parses as a number above this limit, displays `"{MaxCount}+"` (e.g. `99+`) |
| IsAnimated | bool | true | When true, the badge scale/fades in and out as it appears or disappears; a change that arrives mid-animation wins |
| IsPulsing | bool | false | When true, the badge continuously pulses to draw attention |

**Features:**
- Four-corner positioning with per-corner offset nudge
- Auto-hide when `Text` is empty (just bind your count and let the control show/hide itself)
- Dot mode for simple notification indicators
- `MaxCount` overflow ("99+" style) for numeric counts
- Configurable show/hide scale animation and optional continuous pulse for attention-grabbing badges
- Blazor honors `prefers-reduced-motion` and disables both animations when set
