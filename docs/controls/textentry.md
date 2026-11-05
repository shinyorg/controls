# TextEntry

[← All Shiny Controls](../../README.md)

A text entry control with a Material 3 floating label, customizable border, left/right tool slots, hint text for validation errors, character count display, input masking, an autofill/autocorrect opt-out, and — on iOS and Android — a bar docked to the top of the soft keyboard.

`Variant="Floating"` is the **M3 outlined notch**: the label rides up onto the top border stroke and sits in a gap cut out of the outline, so it never overlaps the text being typed. Tools are **inline** by default — a tinted glyph on the field with no grey block and no separator; `ToolStyle="Addon"` brings back the Bootstrap input-group look.

```xml
<shiny:TextEntry Placeholder="Email"
                 Text="{Binding Email, Mode=TwoWay}"
                 Keyboard="Email"
                 HasError="{Binding HasEmailError}"
                 HintText="{Binding EmailError}">
    <shiny:ClearButtonTool />
</shiny:TextEntry>
```

| Property | Type | Default | Description |
|---|---|---|---|
| Text | string | "" | Current text value (TwoWay). When Mask is set, contains raw digits only |
| Placeholder | string | "" | Placeholder / floating label |
| Variant | TextEntryVariant | Classic | `Classic` (native placeholder) or `Floating` (M3 notched outline) |
| ToolStyle | TextEntryToolStyle | Inline | `Inline` (glyph on the field) or `Addon` (filled block + separator) |
| PlaceholderColor | Color | Grey | Placeholder color unfocused |
| FocusedPlaceholderColor | Color | #007AFF | Placeholder color focused |
| BorderColor | Color | #CCCCCC | Border color unfocused |
| FocusedBorderColor | Color | #007AFF | Border color focused |
| BorderThickness | double | 1 | Unfocused border thickness |
| FocusedBorderThickness | double | 2 | Focused border thickness |
| CornerRadius | CornerRadius | 8 | Corner radius |
| EntryBackgroundColor | Color | Transparent | Background fill |
| IsReadOnly | bool | false | Read-only mode |
| IsPassword | bool | false | Password masking |
| Keyboard | Keyboard | Default | Keyboard type (auto-set to Numeric when Mask is active) |
| MaxLength | int | unlimited | Character limit |
| Mask | string? | null | Input mask pattern (`#` = digit slot, other chars are auto-inserted literals) |
| FormattedText | string | "" | Read-only display value with mask applied |
| HintText | string? | null | Hint/error text below field |
| HasError | bool | false | Error state |
| ErrorColor | Color | #DC3545 | Error color |
| ShowCharacterCount | bool | false | Show counter |
| IsAutoCompleteEnabled | bool | true | False switches off autofill, autocorrect, predictive text and spell check together |
| IsSpellCheckEnabled | bool | true | Spell check (forced off when IsAutoCompleteEnabled is false) |
| IsTextPredictionEnabled | bool | true | Suggestion strip (forced off when IsAutoCompleteEnabled is false) |
| Accessory | KeyboardAccessoryView? | null | Bar docked to the top of the soft keyboard (iOS + Android) |
| AccessoryPreset | KeyboardAccessoryPreset | None | Stock bar: `Done`, `Navigation`, `NavigationAndDone` |
| FieldGroup | string? | null | Groups fields for accessory prev/next navigation |
| LeftTools | IList&lt;TextEntryTool&gt; | empty | Left tool slot |
| RightTools | IList&lt;TextEntryTool&gt; | empty | Right tool slot (ContentProperty) |

**Input Masking:**

```xml
<shiny:TextEntry Placeholder="Phone Number" Mask="(###) ###-####" Text="{Binding Phone}" />
<shiny:TextEntry Placeholder="Credit Card" Mask="#### #### #### ####" Text="{Binding Card}" />
<shiny:TextEntry Placeholder="Date" Mask="##/##/####" Text="{Binding DateStr}" />
```

When `Mask` is set, `Text` always contains raw digits (e.g., `"5551234567"`), while the user sees formatted text (e.g., `"(555) 123-4567"`). Keyboard auto-sets to Numeric and literal characters are inserted automatically as the user types.

**Built-in tools:** `ClearButtonTool` (auto-shows ✕ when text present), `TextEntryStepperTool` (increment/decrement numeric values), `TextEntrySpeechToTextTool` (voice input, in SpeechAddins package).

**Stepper Tool:**

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

`TextEntryStepperTool` increments or decrements the numeric text value by `Step` on each tap. If `Text` is not set, it auto-displays the step value with sign (e.g. "+1", "-5").

**No autocomplete:**

```xml
<shiny:TextEntry Placeholder="Serial number" Text="{Binding Serial}" IsAutoCompleteEnabled="False" />
```

Turns off autofill (iOS `TextContentType`, Android autofill hints), autocorrect, predictive text and spell check in one switch — the combination that otherwise rewrites serials, coupon codes and SKUs mid-entry.

**Keyboard accessory (MAUI, iOS + Android only):**

A bar docked to the **top edge of the soft keyboard** while the field has focus — it belongs to the keyboard, not to the entry, and comes and goes with it. The reason it exists: the iOS numeric keypad has no return key, so without a Done button there is no way to dismiss it.

```xml
<shiny:TextEntry Placeholder="Amount" Keyboard="Numeric"
                 Text="{Binding Amount}"
                 AccessoryPreset="NavigationAndDone" />

<shiny:TextEntry Placeholder="Notes" Text="{Binding Notes}">
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

iOS uses the real `UIResponder.InputAccessoryView`, so it rides the keyboard animation exactly. Android has no accessory API at all (the IME is a separate process), so the same bar is rendered in the activity's content view and driven by the IME window insets — frame-synced on API 30+, and shown only while the IME is genuinely up, so a hardware keyboard correctly shows no bar. Windows, macOS, Linux and Blazor have no soft keyboard to decorate; the property compiles and does nothing. This is *not* the [on-screen keyboard](desktop.md#on-screen-keyboard) — that one draws keys; this one decorates the OS keyboard.

The same bar also serves multi-line inputs — it is what puts the markdown editor's formatting toolbar on the keyboard (see [Markdown Controls](markdown.md)). `KeyboardAccessoryView.BarContent` replaces the item row with a layout of your own (a horizontal scroller, say, when there are more items than fit); `KeyboardAccessoryItem`s inside it are wired to the focused field exactly like the ones in `Items`.

### Native chrome and Android Material 3 (MAUI)

TextEntry draws its own border, fill and floating label, so the native text field inside it is stripped of all of its own chrome. The same applies to every Shiny control built on that internal field: `TagEntry`, `AutoCompleteEntry`, `EntryCell`, the quick entry prompt and the multi-line borderless editors.

- **Android with `<UseMaterial3>true</UseMaterial3>`**: MAUI swaps `Entry` and `Editor` onto Material 3 handlers (a `TextInputLayout` with a filled box, underline and its own floating hint). Shiny's internal fields stay on the classic handlers whatever the app opts into, so the box never appears and `Variant` behaves the same as on every other platform. Your own `Entry`s and `Editor`s still get Material 3.
- **Windows**: the hover, focus and disabled states have no underline or fill either, not just the resting state. This is done by overriding the TextBox's theme resources on the field, so the template and any TextBox styling in your app are left alone.
- **macOS (AppKit)**: the bezel, background and focus ring are removed by the `Shiny.Maui.Controls.Desktop` add-on, which is the package that supports the AppKit head.

### Automation ids (MAUI)

Set `AutomationId` on the control and its parts are named after it, for UI tests (Appium, UI Automation, DevFlow). Without an id, the parts get none.

| Part | Id |
|---|---|
| The text field | `<id>_Entry` |
| Hint / error / character-count line | `<id>_Hint` |
| A `ClearButtonTool` | `<id>_Clear` |
| Any other left tool, by position | `<id>_LeftTool_0` |
| Any other right tool, by position | `<id>_RightTool_0` |

A tool you give an `AutomationId` of its own keeps it.

A `KeyboardAccessoryView` is named after **its own** `AutomationId`, not the field's — the bar lives outside the field's tree and can be shared between fields: `<bar id>_Previous`, `<bar id>_Next`, `<bar id>_Done` for the stock items, `<bar id>_Item_0` (by position) for any other `KeyboardAccessoryItem` without an id of its own.
