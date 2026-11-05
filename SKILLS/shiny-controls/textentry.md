# TextEntry

A text entry control with a Material 3 floating label, customizable border, left/right tool slots,
hint text for validation errors, character count, input masking, an autofill/autocorrect opt-out, and
(MAUI, iOS + Android) a bar docked to the top of the soft keyboard. Available on both MAUI and Blazor.

Two things to know before generating code:

- **`Variant="Floating"` is the M3 outlined notch.** The label rides up ONTO the top border stroke and
  sits in a gap cut out of the outline. It never overlaps the text being typed. `Classic` (the default)
  leaves the placeholder to the native control.
- **Tools are inline by default.** An icon or label sits on the field itself with no separator and no
  grey block, tinted with the on-surface-variant token. Set `ToolStyle="Addon"` for the older Bootstrap
  input-group look (filled block + hairline separator).

## MAUI

**Namespace**: `Shiny.Maui.Controls`
**XAML Namespace**: `http://shiny.net/maui/controls` (prefix: `shiny`)

### Basic Usage

```xml
<shiny:TextEntry Placeholder="Email"
                 Text="{Binding Email, Mode=TwoWay}"
                 Keyboard="Email"
                 HasError="{Binding HasEmailError}"
                 HintText="{Binding EmailError}">
    <shiny:ClearButtonTool />
</shiny:TextEntry>
```

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| Text | string | "" | Current text value (TwoWay) |
| Placeholder | string | "" | Placeholder / floating label text |
| Variant | TextEntryVariant | Classic | `Classic` (native placeholder) or `Floating` (M3 notched outline) |
| ToolStyle | TextEntryToolStyle | Inline | `Inline` (bare tinted glyph on the field) or `Addon` (filled block + separator) |
| PlaceholderColor | Color | Grey | Placeholder color when unfocused |
| FocusedPlaceholderColor | Color | #007AFF | Placeholder color when focused/floating |
| BorderColor | Color | #CCCCCC | Border color when unfocused |
| FocusedBorderColor | Color | #007AFF | Border color when focused |
| BorderThickness | double | 1.0 | Border thickness when unfocused |
| FocusedBorderThickness | double | 2.0 | Border thickness when focused |
| CornerRadius | CornerRadius | 8 | Border corner radius |
| EntryBackgroundColor | Color | Transparent | Background color inside the border |
| FontSize | double | 15 | Text and placeholder font size |
| FontFamily | string? | null | Font family |
| FontAttributes | FontAttributes | None | Bold/Italic |
| TextColor | Color | Black | Input text color |
| IsReadOnly | bool | false | Read-only mode |
| IsPassword | bool | false | Password masking |
| ReturnType | ReturnType | Default | Keyboard return button type |
| Keyboard | Keyboard | Default | Keyboard type (Email, Numeric, etc.) |
| MaxLength | int | int.MaxValue | Maximum character count |
| HintText | string? | null | Hint/helper text below the field |
| HintColor | Color | Grey | Hint text color (when no error) |
| HasError | bool | false | Error state — turns border and hint red |
| ErrorColor | Color | #DC3545 | Color used when HasError is true |
| ShowCharacterCount | bool | false | Show "N/MaxLength" counter |
| IsAutoCompleteEnabled | bool | true | False switches off autofill, autocorrect, predictive text and spell check together |
| IsSpellCheckEnabled | bool | true | Spell check (forced off when IsAutoCompleteEnabled is false) |
| IsTextPredictionEnabled | bool | true | Suggestion strip (forced off when IsAutoCompleteEnabled is false) |
| Accessory | KeyboardAccessoryView? | null | Bar docked to the top of the soft keyboard (iOS + Android) |
| AccessoryPreset | KeyboardAccessoryPreset | None | Stock bar: `Done`, `Navigation`, `NavigationAndDone` |
| FieldGroup | string? | null | Groups fields for accessory prev/next navigation |
| LeftTools | IList<TextEntryTool> | [] | Tools on the left side |
| RightTools | IList<TextEntryTool> | [] | Tools on the right side (ContentProperty) |
| Mask | string? | null | Input mask pattern (`#` = digit slot, other chars are literals auto-inserted) |
| FormattedText | string | "" | Read-only formatted display value when Mask is set |
| TextChangedCommand | ICommand? | null | Fires on text change |
| CompletedCommand | ICommand? | null | Fires on return key |

### Events

| Event | Args | Description |
|---|---|---|
| TextChanged | TextChangedEventArgs | Fired when text changes |
| Completed | EventArgs | Fired on return key press |

### Tools

TextEntry supports left and right tool slots. Tools are `TextEntryTool` instances (or subclasses) placed in XAML.

**Built-in tools:**
- `ClearButtonTool` — Shows ✕ when text is non-empty, clears on tap. Auto-hides when empty.
- `TextEntryStepperTool` — Increments/decrements the numeric entry value by `Step` on each tap. Auto-displays `+N` or `-N` if `Text` is not explicitly set.
- `TextEntrySpeechToTextTool` — (Shiny.Maui.Controls.SpeechAddins) Voice input that backfills entry text.

**Stepper tool:**
```xml
<shiny:TextEntry Placeholder="Quantity"
                 Text="{Binding Quantity, Mode=TwoWay}"
                 Keyboard="Numeric">
    <shiny:TextEntry.LeftTools>
        <shiny:TextEntryStepperTool Step="-1" />
    </shiny:TextEntry.LeftTools>
    <shiny:TextEntryStepperTool Step="1" />
</shiny:TextEntry>
```

**TextEntryStepperTool properties:**

| Property | Type | Default | Description |
|---|---|---|---|
| Step | double | 1 | Amount to add (negative = subtract) |
| Text | string? | null | Override button text (defaults to "+N" or "-N") |

**Custom tool:**
```xml
<shiny:TextEntry Placeholder="Amount">
    <shiny:TextEntry.LeftTools>
        <shiny:TextEntryTool Text="$" ToolColor="#059669" />
    </shiny:TextEntry.LeftTools>
    <shiny:ClearButtonTool />
</shiny:TextEntry>
```

**TextEntryTool properties:**

| Property | Type | Description |
|---|---|---|
| Icon | ImageSource? | Tool icon |
| Text | string? | Tool text label |
| ToolColor | Color? | Tint. Unset follows the on-surface-variant theme token. Applies to the label and, when `Icon` is a `FontImageSource`, the glyph — a bitmap `Image` cannot be tinted by MAUI |
| FontSize | double | Label size (default 14) |
| IconSize | double | Icon box size (default 20) |
| Command | ICommand? | Tap command |
| CommandParameter | object? | Command parameter |

**ITextEntryAwareTool** — Implement this interface on a TextEntryTool subclass to get Attach/Detach lifecycle calls with access to the parent TextEntry.

### Turning off autocomplete

Autofill, autocorrect, predictive text and spell check are three separate platform switches, and any
one of them will happily rewrite a half-typed serial number. `IsAutoCompleteEnabled="False"` turns off
all of them at once — that is the property to reach for. The individual switches are there when only
one of them is in the way.

```xml
<shiny:TextEntry Placeholder="Serial number"
                 Text="{Binding Serial, Mode=TwoWay}"
                 IsAutoCompleteEnabled="False" />
```

What it actually does per platform:

| Platform | Effect |
|---|---|
| **iOS / Catalyst** | `AutocorrectionType = No`, `SpellCheckingType = No`, and an empty `TextContentType` — the documented opt-out from AutoFill and the strong-password sheet |
| **Android** | `InputTypes.TextFlagNoSuggestions` plus `ImportantForAutofill = NoExcludeDescendants` (API 26+). Password fields are left alone: assigning `InputType` there would clear the mask, and autofill on a password field is wanted |
| **Windows** | Spell check off; there is no autofill API to opt out of |

### Keyboard accessory bar (iOS + Android)

A bar docked to the **top edge of the soft keyboard** while the field has focus. It is not a tool inside
the entry — it belongs to the keyboard, appears when the keyboard comes up and goes away with it.

The usual reason to want one: **the iOS numeric keypad has no return key**, so without a Done button
there is no way for the user to dismiss it.

```xml
<!-- Stock bar -->
<shiny:TextEntry Placeholder="Amount"
                 Keyboard="Numeric"
                 Text="{Binding Amount, Mode=TwoWay}"
                 AccessoryPreset="NavigationAndDone" />

<!-- Your own bar -->
<shiny:TextEntry Placeholder="Notes" Text="{Binding Notes, Mode=TwoWay}">
    <shiny:TextEntry.Accessory>
        <shiny:KeyboardAccessoryView>
            <shiny:KeyboardNavigationItem Direction="Previous" />
            <shiny:KeyboardNavigationItem Direction="Next" />
            <shiny:KeyboardAccessorySpacer />
            <shiny:KeyboardAccessoryItem Text="#tag" Command="{Binding InsertTagCommand}" />
            <shiny:KeyboardDismissItem />
        </shiny:KeyboardAccessoryView>
    </shiny:TextEntry.Accessory>
</shiny:TextEntry>
```

**Types** (all in `Shiny.Maui.Controls`, the `shiny` xmlns):

| Type | Purpose |
|---|---|
| `KeyboardAccessoryView` | The bar. `Items` is the content property; `BarContent` replaces the whole row with your own layout |
| `KeyboardAccessoryItem` | Icon/label button — `Icon`, `Text`, `ToolColor`, `Command`, `Clicked` |
| `KeyboardNavigationItem` | `Direction="Previous"`/`"Next"`. Moves focus across the page and disables itself at the ends |
| `KeyboardDismissItem` | "Done" — puts the keyboard away |
| `KeyboardAccessorySpacer` | Flexible gap; everything after it is pushed to the right |

`KeyboardAccessoryView` properties: `BarHeight` (44), `BarBackgroundColor`, `BarBorderColor`,
`ItemSpacing` (4), `CurrentOwner` (the field being served, read-only).

`Items` lays out as one Auto column per item, so more items than fit the bar get squeezed rather than
scrolled. When there are many — a formatting toolbar, say — use `BarContent` with your own layout
(typically a horizontal `ScrollView` plus a pinned `KeyboardDismissItem`). Any `KeyboardAccessoryItem`
nested inside `BarContent` is wired to the focused field exactly like one in `Items`.

The bar is not only for single-line entries: it serves any `InputView`, which is how
[MarkdownEditor](./markdown.md) puts its formatting toolbar on the keyboard. On a multi-line editor a
`KeyboardDismissItem` is close to mandatory — the return key inserts a newline, so nothing else puts
the keyboard away.

`KeyboardAccessoryPreset`: `None` (default), `Done`, `Navigation`, `NavigationAndDone`.

**Platform matrix — this is a deliberately platform-only feature:**

| Platform | Behaviour |
|---|---|
| **iOS / Catalyst** | The real `UIResponder.InputAccessoryView`. Rides the keyboard animation exactly, because the OS owns it |
| **Android** | No accessory API exists (the IME is a separate process), so the same bar is rendered in the activity's content view and driven by the IME window insets — frame-synced with `WindowInsetsAnimation` on API 30+. Shown only while the IME is genuinely up, so a hardware keyboard correctly shows no bar |
| **Windows / macOS / Linux / Blazor** | Nothing. The property compiles and does nothing |

**Field navigation** is resolved by `KeyboardFieldNavigator`: every enabled, visible, non-read-only
`TextEntry`/`InputView` on the page, in depth-first visual-tree order (MAUI has no `TabIndex`). Set
`FieldGroup` on the fields to navigate within a subset. Virtualized containers only realize visible
rows, so navigation cannot reach a field that has not been realized yet.

Do **not** confuse this with [On-Screen Keyboard](./onscreen-keyboard.md) — that one *draws keys* for
kiosks. This one never draws a key; it decorates the OS keyboard.

### Input Masking

Set `Mask` to automatically format input as the user types. `#` represents a digit slot; all other characters are literal separators inserted automatically.

When `Mask` is set:
- `Text` always contains **raw digits only** (e.g., `"5551234567"`)
- `FormattedText` contains the display value (e.g., `"(555) 123-4567"`)
- `Keyboard` auto-sets to `Numeric`
- `MaxLength` is auto-calculated from the mask length

```xml
<!-- Phone number -->
<shiny:TextEntry Placeholder="Phone Number"
                 Mask="(###) ###-####"
                 Text="{Binding Phone, Mode=TwoWay}" />

<!-- Credit card -->
<shiny:TextEntry Placeholder="Credit Card"
                 Mask="#### #### #### ####"
                 Text="{Binding CardNumber, Mode=TwoWay}" />

<!-- Date -->
<shiny:TextEntry Placeholder="MM/DD/YYYY"
                 Mask="##/##/####"
                 Text="{Binding DateString, Mode=TwoWay}" />

<!-- SSN -->
<shiny:TextEntry Placeholder="SSN"
                 Mask="###-##-####"
                 Text="{Binding SSN, Mode=TwoWay}" />

<!-- ZIP+4 -->
<shiny:TextEntry Placeholder="ZIP Code"
                 Mask="#####-####"
                 Text="{Binding ZipCode, Mode=TwoWay}" />
```

### Full Example

```xml
<shiny:TextEntry Placeholder="Username"
                 Text="{Binding Username, Mode=TwoWay}"
                 MaxLength="30"
                 ShowCharacterCount="True"
                 HasError="{Binding HasUsernameError}"
                 HintText="{Binding UsernameHint}"
                 FocusedBorderColor="#7C3AED"
                 FocusedPlaceholderColor="#7C3AED"
                 CornerRadius="12">
    <shiny:ClearButtonTool />
</shiny:TextEntry>
```

### Native chrome (Material 3, Windows, macOS)

Never strip platform chrome off TextEntry yourself (no handler mappers, `Style` swaps or `OnHandlerChanged` tweaks). The internal field is already borderless on every head. On Android with `<UseMaterial3>true</UseMaterial3>` it stays on MAUI's classic `EntryHandler`/`EditorHandler`, so no `TextInputLayout` box or native floating hint shows, and `Variant` is never changed for you. On Windows the hover/focus/disabled underline and fill are removed via theme-resource overrides. On macOS AppKit, reference `Shiny.Maui.Controls.Desktop`, which polishes the `NSTextField`. The same applies to TagEntry, AutoCompleteEntry, EntryCell and the quick entry prompt.

## Blazor

**Namespace**: `Shiny.Blazor.Controls`

### Basic Usage

```razor
<TextEntry Placeholder="Email"
           @bind-Text="email"
           HasError="@hasError"
           HintText="@errorMessage"
           RightTools="emailTools" />

@code {
    string email = "";
    bool hasError;
    string? errorMessage;
    List<TextEntryTool> emailTools = [new ClearButtonTool()];
}
```

### Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| Text | string | "" | Current text (two-way via TextChanged) |
| Placeholder | string | "" | Placeholder / floating label |
| Variant | TextEntryVariant | Classic | `Classic` (browser placeholder) or `Floating` (M3 notched outline) |
| ToolStyle | TextEntryToolStyle | Inline | `Inline` or `Addon` |
| PlaceholderColor | string? | on-surface-variant token | CSS color |
| FocusedPlaceholderColor | string? | primary token | CSS color |
| BorderColor | string? | outline token | CSS color |
| FocusedBorderColor | string? | primary token | CSS color |
| BorderThickness | double | 1 | Border width (px) |
| FocusedBorderThickness | double | 2 | Focused border width (px) |
| CornerRadius | string | 8px | CSS border-radius |
| EntryBackgroundColor | string? | surface token | CSS color |
| FontSize | double | 15 | Font size (px) |
| FontFamily | string | inherit | CSS font-family |
| TextColor | string? | on-surface token | CSS color |
| IsReadOnly | bool | false | Read-only mode |
| IsPassword | bool | false | Password masking |
| MaxLength | int | 0 | Max characters (0 = unlimited) |
| HintText | string? | null | Hint text below field |
| HintColor | string? | on-surface-variant token | CSS color |
| HasError | bool | false | Error state |
| ErrorColor | string? | error token | CSS color |
| ShowCharacterCount | bool | false | Show counter |
| IsAutoCompleteEnabled | bool | true | False emits `autocomplete="off"`, `autocorrect="off"`, `autocapitalize="off"`, `spellcheck="false"` plus `data-lpignore` / `data-1p-ignore` / `data-form-type="other"` for the password managers |
| IsSpellCheckEnabled | bool | true | Spell check |
| IsTextPredictionEnabled | bool | true | Autocorrect / auto-capitalisation |
| LeftTools | List\<TextEntryTool\>? | null | Left tool list |
| RightTools | List\<TextEntryTool\>? | null | Right tool list |
| CssClass | string? | null | Additional CSS class |
| Completed | EventCallback | - | Return key event |

### Blazor Tools

Tools use the same `TextEntryTool` base class as MAUI. Pass them as `List<TextEntryTool>` to `LeftTools` or `RightTools`.

**Built-in tools:**
- `ClearButtonTool` — Shows ✕ when text is non-empty, clears on click. Auto-hides when empty.
- `TextEntryStepperTool` — Increments/decrements numeric entry value by `Step`. Auto-displays `+N`/`-N` if `Text` not set.
- `SpeechToTextTool` — (Shiny.Blazor.Controls.SpeechAddins) Voice input via Web Speech API.

**Stepper tool example:**
```razor
<TextEntry Placeholder="Quantity"
           @bind-Text="qty"
           LeftTools="decTools"
           RightTools="incTools" />

@code {
    string qty = "0";
    List<TextEntryTool> decTools = [new TextEntryStepperTool { Step = -1 }];
    List<TextEntryTool> incTools = [new TextEntryStepperTool { Step = 1 }];
}
```

**TextEntryTool properties:**

| Property | Type | Description |
|---|---|---|
| Icon | string? | Icon text/emoji |
| Text | string? | Tool text label |
| ToolColor | string? | CSS color. Null follows the on-surface-variant theme token |
| IsVisible | bool | Whether the tool is shown (default: true) |
| Clicked | Action? | Click callback |
| CssClass | string? | Additional CSS class |

**Lifecycle methods** — Override in subclasses:
- `OnTextChanged(string? text)` — Called when entry text changes
- `OnAttached(TextEntryContext context)` / `OnDetached()` — Lifecycle hooks
- `SetEntryText(string text)` / `GetEntryText()` — Read/write parent text

### Blazor Example

```razor
<TextEntry Placeholder="Search"
           @bind-Text="query"
           RightTools="tools" />

@code {
    string query = "";
    List<TextEntryTool> tools = [new ClearButtonTool()];
}
```

```razor
<TextEntry Placeholder="Amount"
           @bind-Text="amount"
           LeftTools="leftTools"
           RightTools="rightTools" />

@code {
    string amount = "";
    List<TextEntryTool> leftTools = [new TextEntryTool { Text = "$" }];
    List<TextEntryTool> rightTools = [new ClearButtonTool()];
}
```

### Blazor Mask Example

```razor
<TextEntry Placeholder="Phone Number"
           Mask="(###) ###-####"
           @bind-Text="phone" />

<TextEntry Placeholder="Credit Card"
           Mask="#### #### #### ####"
           @bind-Text="card" />

@code {
    string phone = "";
    string card = "";
}
```

Parameters added for masking:

| Parameter | Type | Default | Description |
|---|---|---|---|
| Mask | string? | null | Input mask pattern (`#` = digit, others are literals) |
| FormattedText | string | "" | Read-only formatted display value |

### Blazor: no keyboard accessory

There is no Blazor equivalent of the MAUI keyboard accessory bar, on purpose. iOS Safari already draws
its own bar above the keyboard and will not let a page remove it, so ours would stack on top of it and
the user would see two; Chrome on Android draws none, so the behaviour would diverge across the only
two browsers that matter. Do not generate one.

## Automation ids (MAUI)

Set `AutomationId` on the control to name its parts for UI tests (no id → no part ids). Never assign `AutomationId` twice in generated code; MAUI throws.

- `TextEntry`: `<id>_Entry` (the native entry), `<id>_Hint`, `<id>_Clear` (a `ClearButtonTool`), other tools `<id>_LeftTool_<n>` / `<id>_RightTool_<n>` (a tool with its own id keeps it). A `KeyboardAccessoryView` names its items after the **bar's** id: `<bar>_Previous`, `<bar>_Next`, `<bar>_Done`, `<bar>_Item_<n>`.
