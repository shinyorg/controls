# TagEntry

[← All Shiny Controls](../../README.md)

A free-text tags field: type and press Enter — or a delimiter — to commit each value as a removable chip, all inside one bordered box. It ships in the **core** package on both hosts.

```bash
dotnet add package Shiny.Maui.Controls             # MAUI
dotnet add package Shiny.Blazor.Controls           # Blazor
```

```xml
<shiny:TagEntry Tags="{Binding Topics}"
                Placeholder="Add a topic..."
                MaxTags="5" />
```

```razor
<TagEntry @bind-Tags="topics" Placeholder="Add a topic..." MaxTags="5" />
```

Reach for it when the values are the user's own words. When they have to come from a known list, [`AutoCompleteEntry`](autocomplete.md) is the control that offers one — this deliberately has no suggestion popup, because a field that both accepts anything and suggests something is a field nobody can tell the rules of.

## Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Tags` | `IList<string>` / `IReadOnlyList<string>` | empty | The committed tags (TwoWay / `@bind-Tags`) |
| `Delimiters` | `IList<string>` | `[","]` | What commits besides Enter, and what a paste splits on |
| `MaxTags` | `int` | `0` | Cap, or 0 for no limit |
| `AllowDuplicates` | `bool` | `false` | Whether the same tag may be committed twice |
| `CaseSensitiveDuplicates` | `bool` | `false` | Whether `Design` and `design` are two tags |
| `TrimWhitespace` | `bool` | `true` | Trim a committed tag |
| `BackspaceRemovesTag` | `bool` | `true` | Backspace on empty text removes the newest chip |
| `CommitOnUnfocus` / `CommitOnBlur` | `bool` | `true` | Leaving the field commits what was typed |
| `IsReadOnly` / `ReadOnly` | `bool` | `false` | Chips stay crisp, adding and removing are blocked |
| `Placeholder` | `string` | `""` | Prompt in the typing area |
| `AutoFocus` | `bool` | `false` | Focus the typing area as soon as the field appears |
| `ChipTemplate` / `ChipContent` | `DataTemplate` / `RenderFragment<string>` | `null` | Replaces a chip's label |
| `ChipBackgroundColor`, `ChipTextColor`, `ChipCornerRadius` | | unset | Chip chrome; unset follows the theme |
| `CornerRadius`, `BorderColor`, `BorderThickness` | | unset | Box chrome; unset follows the theme |

**MAUI** adds `TextColor`, `PlaceholderColor`, `FontSize`, `DisabledOpacity`, and the events `TagAdding` (cancellable), `TagAdded`, `TagRemoved`, `TagsChanged`, plus `TagAddedCommand`/`TagRemovedCommand`. `Focus()` moves the keyboard to the typing area.

**Blazor** adds `TagValidator` (`Func<string, bool>` — the cancellable-event equivalent), `TagAdded`/`TagRemoved` callbacks, `Disabled`, `CssClass`, and `FocusAsync()`. Anything else set on the component is splatted onto the inner `<input>`, so `aria-invalid` and a `Field` pairing behave as they would on any other input.

## Committing

Enter commits what is typed. So does any string in `Delimiters` — and an **empty** list means Enter only, which is how a tag gets to contain a comma:

```xml
<shiny:TagEntry Tags="{Binding Recipients}" Delimiters="{Binding CommaOrSemicolon}" />
<shiny:TagEntry Tags="{Binding Phrases}"    Delimiters="{Binding NoDelimiters}" />
```

```razor
<TagEntry Delimiters="@([",", ";"])" Placeholder="Comma or semicolon commits..." />
<TagEntry Delimiters="@([])"         Placeholder="Only Enter commits..." />
```

Pasting `"a, b, c"` commits **three** tags; typing the same characters commits `a` and `b` and leaves `c` in the editor, because the user is still writing it. The difference is the size of the insertion: more than one character arriving at once is a paste.

At `MaxTags` further commits are ignored and the typed text stays put — eating it would throw away something the user wrote with no sign that it happened. Duplicates are dropped quietly.

## Validation

```xml
<shiny:TagEntry Tags="{Binding Tags}" TagAdding="OnTagAdding" />
```
```csharp
void OnTagAdding(object sender, TagAddingEventArgs e) => e.Cancel = e.Tag.Length < 3;
```

```razor
<TagEntry TagValidator="@(tag => tag.Length >= 3)" />
```

It runs after trimming and after the duplicate and `MaxTags` rules, so a handler only ever sees a tag that would otherwise have been added. A refusal leaves the typed text where it is, to be corrected rather than retyped.

## Backspace on empty text — the MAUI difference

On Blazor this is a real `keydown` and there is nothing to explain.

MAUI has no portable key-down on an `Entry`: backspacing text that is already empty changes nothing and raises nothing. So the field keeps a **zero-width space** in front of whatever is typed — deleting *that* does raise `TextChanged`, and is the signal. It never reaches a committed tag, and it is in the box only while it can do something: while the field is being typed into, and while there is a tag for Backspace to reach. That matters because a placeholder does not show while any text is present, invisible or not — so an empty or unfocused field keeps its prompt. If a platform's predictive text fights it, `BackspaceRemovesTag="False"` turns the whole mechanism off and everything else about the control is unaffected.

## Chips

`ChipTemplate` (MAUI) and `ChipContent` (Blazor) replace a chip's **label** — the tag string is the context. The remove affordance is never part of the template: every chip keeps the same way out, however it is drawn.

```xml
<shiny:TagEntry Tags="{Binding Labels}">
    <shiny:TagEntry.ChipTemplate>
        <DataTemplate x:DataType="x:String">
            <HorizontalStackLayout Spacing="4">
                <shiny:MotionIconView Icon="tag" WidthRequest="12" HeightRequest="12"
                                      Trigger="Manual" InputTransparent="True" />
                <Label Text="{Binding .}" FontSize="14" VerticalTextAlignment="Center" />
            </HorizontalStackLayout>
        </DataTemplate>
    </shiny:TagEntry.ChipTemplate>
</shiny:TagEntry>
```

```razor
<TagEntry Tags="@labels">
    <ChipContent Context="tag">
        <MotionIcon Icon="tag" Size="12" Trigger="MotionTrigger.Manual" />
        @tag
    </ChipContent>
</TagEntry>
```

Chips are not [`PillView`](pillview.md)s. A pill is a status badge and carries no interaction; giving it one would mean every pill in every app grew a hit target it does not want. A chip is those visuals plus a remove button, and the remove button is the only part that takes a tap — tapping the word does nothing rather than something surprising.

An unset `ChipBackgroundColor` follows the theme's secondary container. Set one and the chip's ink is computed from it by WCAG luminance — the same rule `PillView` uses — so a brand colour stays readable.

## Read-only, disabled and focus

`IsReadOnly`/`ReadOnly` keeps the chips visible and crisp but blocks adding and removing; disabling the field also dims it. Read-only is "these are the values", disabled is "not right now".

Focus always targets the typing area, never a chip or its remove button — so clearing the tags and calling `Focus()`/`FocusAsync()` hands the field straight back, ready for the replacement list.

## Layout

On MAUI the chips sit in a small internal wrapping layout: they flow onto as many lines as they need, and the editor takes the rest of whichever line it lands on, dropping to a line of its own rather than becoming a sliver nobody can type into. On Blazor that is `flex-wrap` and a `min-width` on the input.

## Code generation guidance

- Bind `Tags` to an `ObservableCollection<string>` on MAUI and it is watched live; a plain `List` works too, the control just rebuilds the chips itself.
- Do not pair it with a suggestion list. If the values come from a list, that is a `AutoCompleteEntry` or a multi-select picker.
- Set `MaxTags` when the backend has a limit — the cap is visible behaviour rather than a validation error after submit.
- Leave the colour properties unset so the theme carries through; set `ChipBackgroundColor` only for a brand colour.
