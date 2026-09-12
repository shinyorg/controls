# TagEntry

A free-text tags field: type and press Enter — or a delimiter — to commit each value as a removable
chip, all inside one bordered box. `TagEntry` on both hosts, in the **core** package.

Reach for it when the values are the user's **own words**. When they must come from a known list, use
`AutoCompleteEntry` (see `autocomplete.md`) — this deliberately has no suggestion popup.

## Basic Usage

```xml
<shiny:TagEntry Tags="{Binding Topics}"
                Placeholder="Add a topic..."
                MaxTags="5" />

<!-- Comma or semicolon commits; an EMPTY list means Enter only, so a tag can contain a comma -->
<shiny:TagEntry Tags="{Binding Recipients}" Delimiters="{Binding CommaOrSemicolon}" />
<shiny:TagEntry Tags="{Binding Phrases}"    Delimiters="{Binding NoDelimiters}" />

<!-- Validation -->
<shiny:TagEntry Tags="{Binding Tags}" TagAdding="OnTagAdding" />
```

```csharp
void OnTagAdding(object sender, TagAddingEventArgs e) => e.Cancel = e.Tag.Length < 3;
```

## Properties (MAUI)

**Values** — `Tags` (`IList<string>`, TwoWay), `Delimiters` (`IList<string>`, `[","]`), `MaxTags` (0 =
unlimited), `AllowDuplicates` (false), `CaseSensitiveDuplicates` (false), `TrimWhitespace` (true),
`BackspaceRemovesTag` (true), `CommitOnUnfocus` (true), `IsReadOnly`, `AutoFocus`.

**Text** — `Placeholder`, `PlaceholderColor?`, `TextColor?`, `FontSize`.

**Chrome** — `CornerRadius`, `BorderColor?`, `BorderThickness`, `DisabledOpacity` (0.38).

**Chips** — `ChipTemplate` (`DataTemplate`, the tag string is the binding context),
`ChipBackgroundColor?`, `ChipTextColor?`, `ChipCornerRadius`.

Events: `TagAdding` (cancellable), `TagAdded`, `TagRemoved`, `TagsChanged`, plus `TagAddedCommand` /
`TagRemovedCommand`. Methods: `AddTag(string)`, `RemoveTag(string)`, `CommitPending()`, `Focus()`.
`PendingText` is what is currently typed.

## Committing

- Enter commits. So does any string in `Delimiters`.
- **Pasting** `"a, b, c"` commits three tags; **typing** the same characters commits `a` and `b` and
  leaves `c` in the editor, because the user is still writing it. The rule is the size of the
  insertion — more than one character at once is a paste.
- At `MaxTags` further commits are ignored and **the typed text stays put**. Duplicates are dropped
  quietly.
- `TagAdding` runs after trimming and after the duplicate and cap rules, so a handler only sees a tag
  that would otherwise have been added. Cancelling leaves the text to be corrected.

## Backspace on empty text

On Blazor this is a real `keydown`. On MAUI there is **no portable key-down on an `Entry`**, so the
field keeps a zero-width space in front of whatever is typed and reads its deletion as the Backspace.
The sentinel never reaches a committed tag, and it is only in the box while it can do something — while
the field is focused, and while there is a tag for Backspace to reach — so an empty or unfocused field
keeps its placeholder.
`BackspaceRemovesTag="False"` turns the mechanism off. Do not try to add a key handler instead — there
is not one to add.

## Chips

`ChipTemplate` replaces a chip's **label** only; the remove affordance is never part of it.

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

An unset `ChipBackgroundColor` follows the theme's secondary container. Set one and the ink is computed
from it by WCAG luminance, the same rule `PillView` uses.

A chip is **not** a `PillView`: a pill is a status badge with no interaction, and only the chip's
remove button takes a tap — tapping the word does nothing.

## Blazor

Parameters mirror MAUI, with these differences:

- `Tags` is `IReadOnlyList<string>` and supports `@bind-Tags`.
- `ReadOnly` and `Disabled` rather than `IsReadOnly` / `IsEnabled`.
- `CommitOnBlur` rather than `CommitOnUnfocus`.
- `TagValidator` (`Func<string, bool>`, return false to refuse) replaces the cancellable `TagAdding`.
- `ChipContent` is a `RenderFragment<string>`.
- `FocusAsync()`, `CommitPendingAsync()`, `AddTagAsync()`, `RemoveTagAsync()`, `ClearAsync()`.
- Unmatched attributes are splatted onto the inner `<input>`, so `aria-invalid` turns the box
  destructive and a `Field` pairing works like any other input.

```razor
<TagEntry @bind-Tags="topics" Placeholder="Add a topic..." MaxTags="5" />

<TagEntry Delimiters="@([",", ";"])" Placeholder="Comma or semicolon commits..." />
<TagEntry Delimiters="@([])"         Placeholder="Only Enter commits..." />

<TagEntry TagValidator="@(tag => tag.Length >= 3)" />

<TagEntry @ref="field" @bind-Tags="topics">
    <ChipContent Context="tag">
        <MotionIcon Icon="tag" Size="12" Trigger="MotionTrigger.Manual" />
        @tag
    </ChipContent>
</TagEntry>

@code {
    TagEntry? field;
    IReadOnlyList<string> topics = ["getting-started"];

    async Task ClearAsync()
    {
        topics = [];
        await field!.FocusAsync();   // focus always lands on the typing area, never a chip
    }
}
```

## Code Generation Guidance

- Bind `Tags` to an `ObservableCollection<string>` on MAUI and it is watched live; a plain `List` works
  too, the control rebuilds the chips itself.
- **Do not pair it with a suggestion list.** If the values come from a list, that is an
  `AutoCompleteEntry` or a multi-select picker.
- Set `MaxTags` when the backend has a limit — a visible cap beats a validation error after submit.
- Delimiter lists are awkward in XAML (`x:Array` plus a type argument); put them on the view model.
- Leave the colour properties unset so the theme carries through.
- Read-only is "these are the values"; disabled is "not right now". Do not use disabled to make a list
  non-editable — the chips go dim and unreadable.
