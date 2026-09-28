# TableView

[← All Shiny Controls](../../README.md)

A settings-style table view with 14+ built-in cell types, section grouping, drag-to-reorder, and dynamic data binding.

`TableSection.IsVisible` is live and safe to bind — a section that flips re-renders the table, and a
hidden section draws nothing at all, including no section separator on either side of it. Binding it
to a feature flag or a mode switch is the intended use — including to a `SwitchCell` in the same
table. The re-render runs on the dispatcher just after the change, not inside it, and several sections
changing together re-render once.

| Basic | Dynamic | Drag & Sort | Pickers | Styling |
|:---:|:---:|:---:|:---:|:---:|
| ![Basic](../../assets/tableview-basic.png) | ![Dynamic](../../assets/tableview-dynamic.png) | ![Drag & Sort](../../assets/tableview-dragsort.png) | ![Pickers](../../assets/tableview-picker.png) | ![Styling](../../assets/tableview-styling.png) |

```xml
<shiny:TableView>
    <shiny:TableRoot>
        <shiny:TableSection Title="General">
            <shiny:SwitchCell Title="Wi-Fi" On="{Binding WifiEnabled}" />
            <shiny:EntryCell Title="Username" Text="{Binding Username}" />
            <shiny:PickerCell Title="Theme" ItemsSource="{Binding Themes}" SelectedItem="{Binding SelectedTheme}" />
        </shiny:TableSection>
    </shiny:TableRoot>
</shiny:TableView>
```

**Cell Types:**

| Cell | Description |
|---|---|
| SwitchCell | Toggle switch |
| EntryCell | Text input field — with TextEntry's input masking, keyboard accessory bar (iOS/Android) and autocomplete opt-out |
| CheckboxCell | Checkbox with accent color |
| RadioCell | Radio button with section-level grouping |
| CommandCell | Tappable row with optional arrow indicator |
| ButtonCell | Command-bound button |
| LabelCell | Read-only text display |
| PickerCell | Single or multi-select picker |
| TextPickerCell | String list picker |
| DatePickerCell | Date selection with min/max bounds |
| TimePickerCell | Time selection with 24-hour mode and minute interval |
| DurationPickerCell | TimeSpan picker with min/max |
| NumberPickerCell | Integer picker with min/max/unit |
| SimpleCheckCell | Checkmark indicator |
| CustomCell | Custom view content with drag-reorder support |

**EntryCell input features** — `EntryCell` shares `TextEntry`'s input behaviour without any of its chrome (no tools, no floating label, no hint — the cell already has those):

```xml
<shiny:TableSection Title="Payment">
    <shiny:EntryCell Title="Phone" Mask="(###) ###-####"
                     ValueText="{Binding Phone, Mode=TwoWay}"
                     FieldGroup="payment" AccessoryPreset="NavigationAndDone" />
    <shiny:EntryCell Title="Card" Mask="#### #### #### ####"
                     ValueText="{Binding Card, Mode=TwoWay}"
                     FieldGroup="payment" AccessoryPreset="NavigationAndDone" />
</shiny:TableSection>
```

| Property | Type | Default | Description |
|---|---|---|---|
| Mask | string? | null | Input mask (`#` = digit slot). `ValueText` stays raw; `FormattedValueText` is what's displayed |
| FormattedValueText | string | "" | Read-only masked display value |
| Accessory | KeyboardAccessoryView? | null | Bar docked to the top of the soft keyboard (iOS + Android) |
| AccessoryPreset | KeyboardAccessoryPreset | None | `Done`, `Navigation`, `NavigationAndDone` |
| FieldGroup | string? | null | Scopes accessory prev/next to a subset of fields |
| IsAutoCompleteEnabled | bool | true | False switches off autofill, autocorrect, prediction and spell check |

`TableView` is not virtualized, so accessory prev/next reaches every cell on the page. Blazor supports `Mask` and `IsAutoCompleteEnabled`; the accessory bar is MAUI-only.

**Dynamic Sections** - Bind to a collection to generate sections from data:

```xml
<shiny:TableView ItemsSource="{Binding Items}" ItemTemplate="{StaticResource SectionTemplate}" />
```

**Drag to reorder** - `UseDragSort="True"` puts a drag handle on every row in a section. Dragging a
handle lifts the row under the finger, draws an insertion line at the drop position, and auto-scrolls
when the drag reaches the top or bottom edge; touches anywhere else still scroll the table. Rows
reorder within their own section only.

```xml
<shiny:TableView ItemDropped="OnItemDropped">
    <shiny:TableRoot>
        <shiny:TableSection Title="Reorder" UseDragSort="True">
            <shiny:LabelCell Title="First" ValueText="1" />
            <shiny:LabelCell Title="Second" ValueText="2" />
        </shiny:TableSection>
    </shiny:TableRoot>
</shiny:TableView>
```

`ItemDropped` / `ItemDroppedCommand` report `Section`, `Cell`, `Item`, `FromIndex`, and `ToIndex`.
Cells declared in XAML are reordered by the control; rows generated from a section's `ItemsSource`
are not - their order lives in your collection, so move `Item` to `ToIndex` yourself in the handler.
The gesture is pan-driven on every platform (the platform `DragGestureRecognizer` is broken on Mac
Catalyst and absent from the AppKit and GTK4 hosts, and reports no pointer position where it does
work), with native hooks on iOS and Android that stop the enclosing scroller from stealing the drag.

## Section headers

A section title is drawn as a grouped-table header: uppercase, small, tracked, in
`on-surface-variant` on a `surface-container` band, matching the Blazor `TableView`.

| Property | Default | Notes |
| --- | --- | --- |
| `HeaderTextTransform` | `Uppercase` | MAUI only. `TextTransform`, so it changes how the title is **drawn** and never what `Title` holds — bindings, accessibility and tests still see the string you set. Set `None` for sentence case. |
| `HeaderCharacterSpacing` | `0.5` | MAUI only. Uppercase small text needs a little air; Blazor applies the same tracking in CSS. |
| `HeaderFontSize` | unset | Resolves to the theme's `BodySmallSize`. |
| `HeaderBackgroundColor`, `HeaderTextColor`, `FooterTextColor`, `SeparatorColor`, `SectionSeparatorColor` | unset | Resolve from theme tokens and follow a theme swap or an appearance flip live. |

**Why the colours changed.** These defaults used to be literal iOS system greys chosen from
`Application.Current.RequestedTheme` at render time. That was wrong twice: a theme pack restyled
every other part of the table and left the headers in iOS grey, and — because the colour was read
once, while the section was being built — a theme swap or an appearance flip arriving after the
first render left the old value on screen. The visible symptom was a near-black band sitting between
the sections of a light table, or headers drifting out of step with the rows under them. They are
bound to theme tokens now, which re-resolve on both.

Setting any of them still pins it, as before. See [Styling & theming](styling.md#dark-mode).
