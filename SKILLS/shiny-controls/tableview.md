# TableView

## Architecture

The control hierarchy is:
```
shiny:TableView (ContentView > ScrollView > VerticalStackLayout)
  shiny:TableRoot
    shiny:TableSection (Title, FooterText, cells)
      shiny:LabelCell / shiny:SwitchCell / shiny:CommandCell / etc.
```

**Style cascade**: TableView globals -> Section overrides -> Cell overrides. All properties are `BindableProperty` with full MVVM support.

## Cell Types (14 Total)

### LabelCell
Displays title with optional value text on the right.

```xml
<shiny:LabelCell Title="Version" ValueText="1.0.0" Description="Latest release" />
```

| Property | Type | Default | Description |
|---|---|---|---|
| `ValueText` | `string` | `""` | Right-side text |
| `ValueTextColor` | `Color?` | `null` | Value color |
| `ValueTextFontSize` | `double` | `-1` | Value font size |
| `ValueTextFontFamily` | `string?` | `null` | Value font family |
| `ValueTextFontAttributes` | `FontAttributes?` | `null` | Value styling |

### SwitchCell
Toggle switch.

```xml
<shiny:SwitchCell Title="Wi-Fi" On="{Binding WifiEnabled, Mode=TwoWay}" OnColor="#34C759" />
```

| Property | Type | Default |
|---|---|---|
| `On` | `bool` | `false` |
| `OnColor` | `Color?` | `null` |

### CheckboxCell
Native checkbox control.

```xml
<shiny:CheckboxCell Title="Accept Terms" Checked="{Binding Accepted, Mode=TwoWay}" AccentColor="Green" />
```

| Property | Type | Default |
|---|---|---|
| `Checked` | `bool` | `false` |
| `AccentColor` | `Color?` | `null` |

### SimpleCheckCell
Checkmark toggle (no native checkbox, just a checkmark character).

```xml
<shiny:SimpleCheckCell Title="Option A" Checked="{Binding OptionA, Mode=TwoWay}" />
```

| Property | Type | Default |
|---|---|---|
| `Checked` | `bool` | `false` |
| `Value` | `object?` | `null` |
| `AccentColor` | `Color?` | `null` |

### RadioCell
Radio button selection. Use `RadioCell.SelectedValue` attached property on the section.

```xml
<shiny:TableSection Title="Theme" shiny:RadioCell.SelectedValue="{Binding SelectedTheme, Mode=TwoWay}">
    <shiny:RadioCell Title="Light" Value="Light" />
    <shiny:RadioCell Title="Dark" Value="Dark" />
    <shiny:RadioCell Title="System" Value="System" />
</shiny:TableSection>
```

| Property | Type | Default |
|---|---|---|
| `Value` | `object?` | `null` |
| `AccentColor` | `Color?` | `null` |

### CommandCell
Tappable cell with disclosure arrow. Inherits from LabelCell.

```xml
<shiny:CommandCell Title="About" ValueText="Learn more"
                 Command="{Binding AboutCommand}"
                 KeepSelectedUntilBack="True" />
```

| Property | Type | Default |
|---|---|---|
| `Command` | `ICommand?` | `null` |
| `CommandParameter` | `object?` | `null` |
| `ShowArrow` | `bool` | `true` |
| `KeepSelectedUntilBack` | `bool` | `false` |

### ButtonCell
Full-width button-style cell.

```xml
<shiny:ButtonCell Title="Sign Out" Command="{Binding SignOutCommand}" ButtonTextColor="Red" />
```

| Property | Type | Default |
|---|---|---|
| `Command` | `ICommand?` | `null` |
| `CommandParameter` | `object?` | `null` |
| `ButtonTextColor` | `Color?` | `null` |
| `TitleAlignment` | `TextAlignment` | `Center` |

### EntryCell
Inline text input. Shares [TextEntry](./textentry.md)'s **input masking**, **keyboard accessory bar**
and **autocomplete opt-out** — but none of its chrome (no tools, no floating label, no hint text; the
cell already supplies title, description and hint).

```xml
<shiny:EntryCell Title="Email" ValueText="{Binding Email, Mode=TwoWay}"
               Placeholder="user@example.com" Keyboard="Email" />
```

| Property | Type | Default |
|---|---|---|
| `ValueText` | `string` | `""` |
| `Placeholder` | `string` | `""` |
| `PlaceholderColor` | `Color?` | `null` |
| `Keyboard` | `Keyboard` | `Default` |
| `IsPassword` | `bool` | `false` |
| `MaxLength` | `int` | `-1` |
| `TextAlignment` | `TextAlignment` | `End` |
| `CompletedCommand` | `ICommand?` | `null` |
| `ValueTextColor` | `Color?` | `null` |
| `Mask` | `string?` | `null` |
| `FormattedValueText` | `string` | `""` |
| `Accessory` | `KeyboardAccessoryView?` | `null` |
| `AccessoryPreset` | `KeyboardAccessoryPreset` | `None` |
| `FieldGroup` | `string?` | `null` |
| `IsAutoCompleteEnabled` | `bool` | `true` |

**Masking** — identical contract to `TextEntry`: `#` is a digit slot, other characters are literals
inserted as the user types. `ValueText` holds the **raw digits**, `FormattedValueText` holds what is on
screen, and the keyboard and max length are set from the mask.

```xml
<shiny:EntryCell Title="Phone" Mask="(###) ###-####" ValueText="{Binding Phone, Mode=TwoWay}" />
```

**Keyboard accessory (iOS + Android)** — a bar docked to the top of the soft keyboard while the cell
has focus. On a settings form the useful shape is prev/next plus Done, scoped with `FieldGroup` so the
arrows walk that section rather than every input on the page:

```xml
<shiny:TableSection Title="Payment">
    <shiny:EntryCell Title="Card"   Mask="#### #### #### ####" FieldGroup="payment"
                     AccessoryPreset="NavigationAndDone" ValueText="{Binding Card, Mode=TwoWay}" />
    <shiny:EntryCell Title="Expiry" Mask="##/####" FieldGroup="payment"
                     AccessoryPreset="NavigationAndDone" ValueText="{Binding Expiry, Mode=TwoWay}" />
</shiny:TableSection>
```

See [TextEntry](./textentry.md) for the full accessory API — the item types and the platform matrix are
the same, and `TableView` is not virtualized so prev/next reaches every cell on the page.

**Blazor** — `Mask` and `IsAutoCompleteEnabled` are supported; there is no accessory bar (see
[TextEntry](./textentry.md) for why).

### DatePickerCell
Opens native date picker dialog on tap.

```xml
<shiny:DatePickerCell Title="Birthday" Date="{Binding BirthDate, Mode=TwoWay}" Format="D" />
```

| Property | Type | Default |
|---|---|---|
| `Date` | `DateTime?` | `null` |
| `InitialDate` | `DateTime` | `2000-01-01` |
| `MinimumDate` | `DateTime` | `1900-01-01` |
| `MaximumDate` | `DateTime` | `2100-12-31` |
| `Format` | `string` | `"d"` |
| `ValueTextColor` | `Color?` | `null` |

### TimePickerCell
Opens native time picker dialog on tap. Supports 24-hour mode and minute intervals (iOS natively, other platforms snap to nearest interval).

```xml
<shiny:TimePickerCell Title="Alarm" Time="{Binding AlarmTime, Mode=TwoWay}" Format="T"
                      Use24Hour="True" MinuteInterval="15" />
```

| Property | Type | Default |
|---|---|---|
| `Time` | `TimeSpan` | `00:00:00` |
| `Format` | `string` | `"t"` |
| `Use24Hour` | `bool` | `false` |
| `MinuteInterval` | `int` | `1` |
| `ValueTextColor` | `Color?` | `null` |

### DurationPickerCell
Opens hour/minute pickers with hr/min labels in a FloatingPanel on tap. Requires `ShinyContentPage`.

```xml
<shiny:DurationPickerCell Title="Duration" Duration="{Binding Duration, Mode=TwoWay}"
                          MinDuration="0:15:00" MaxDuration="4:00:00" />
```

| Property | Type | Default |
|---|---|---|
| `Duration` | `TimeSpan?` | `null` |
| `MinDuration` | `TimeSpan` | `0:00:00` |
| `MaxDuration` | `TimeSpan` | `24:00:00` |
| `MinuteInterval` | `int` | `5` |
| `Format` | `string` | `@"h\:mm"` |
| `PickerTitle` | `string` | `"Select Duration"` |
| `SelectedCommand` | `ICommand?` | `null` |
| `ValueTextColor` | `Color?` | `null` |

### TextPickerCell
Opens native dropdown/spinner picker on tap. Bind by index (`SelectedIndex`) or by value (`SelectedItem`,
two-way) — a `SelectedItem` set from code/binding is reflected in the cell's value label and picker selection
(it stays in sync even when `ItemsSource` is populated asynchronously). Use `DisplayMember` to show a property
of complex items.

```xml
<!-- by value, with a display member -->
<shiny:TextPickerCell Title="Lens" ItemsSource="{Binding Cameras}"
                    DisplayMember="Name"
                    SelectedItem="{Binding SelectedCamera, Mode=TwoWay}" />

<!-- by index -->
<shiny:TextPickerCell Title="Color" ItemsSource="{Binding Colors}"
                    SelectedIndex="{Binding SelectedColorIndex, Mode=TwoWay}" />
```

| Property | Type | Default |
|---|---|---|
| `ItemsSource` | `IList?` | `null` |
| `SelectedIndex` | `int` | `-1` |
| `SelectedItem` | `object?` (two-way; reflects in the value label) | `null` |
| `DisplayMember` | `string?` | `null` |
| `PickerTitle` | `string?` | `null` |
| `SelectedCommand` | `ICommand?` | `null` |
| `ValueTextColor` | `Color?` | `null` |

### NumberPickerCell
Opens a prompt dialog for numeric input.

```xml
<shiny:NumberPickerCell Title="Font Size" Number="{Binding FontSize, Mode=TwoWay}"
                      Min="8" Max="72" Unit="pt" />
```

| Property | Type | Default |
|---|---|---|
| `Number` | `int?` | `null` |
| `Min` | `int` | `0` |
| `Max` | `int` | `9999` |
| `Unit` | `string` | `""` |
| `PickerTitle` | `string` | `"Enter a number"` |
| `SelectedCommand` | `ICommand?` | `null` |
| `ValueTextColor` | `Color?` | `null` |

### PickerCell
Full-page picker for single or multi-select. Navigates to a selection page.

```xml
<!-- Single select -->
<shiny:PickerCell Title="Country" ItemsSource="{Binding Countries}"
                SelectionMode="Single" SelectedItem="{Binding SelectedCountry, Mode=TwoWay}"
                PageTitle="Select Country" />

<!-- Multi select -->
<shiny:PickerCell Title="Hobbies" ItemsSource="{Binding Hobbies}"
                SelectionMode="Multiple" MaxSelectedNumber="3"
                SelectedItems="{Binding SelectedHobbies, Mode=TwoWay}" />
```

| Property | Type | Default |
|---|---|---|
| `ItemsSource` | `IEnumerable?` | `null` |
| `SelectedItem` | `object?` | `null` |
| `SelectedItems` | `IList?` | `null` |
| `SelectionMode` | `SelectionMode` | `Single` |
| `MaxSelectedNumber` | `int` | `0` (unlimited) |
| `UsePickToClose` | `bool` | `false` |
| `UseAutoValueText` | `bool` | `true` |
| `DisplayMember` | `string?` | `null` |
| `SubDisplayMember` | `string?` | `null` |
| `PageTitle` | `string` | `"Select"` |
| `ShowArrow` | `bool` | `true` |
| `KeepSelectedUntilBack` | `bool` | `false` |
| `SelectedCommand` | `ICommand?` | `null` |
| `AccentColor` | `Color?` | `null` |

### CustomCell
Hosts any custom MAUI view.

```xml
<shiny:CustomCell Title="Progress">
    <shiny:CustomCell.CustomContent>
        <ProgressBar Progress="0.75" />
    </shiny:CustomCell.CustomContent>
</shiny:CustomCell>
```

| Property | Type | Default |
|---|---|---|
| `CustomContent` | `View?` | `null` |
| `UseFullSize` | `bool` | `false` |
| `Command` | `ICommand?` | `null` |
| `LongCommand` | `ICommand?` | `null` |
| `ShowArrow` | `bool` | `false` |
| `KeepSelectedUntilBack` | `bool` | `false` |

## Common Cell Properties (CellBase)

All cells inherit these properties:

| Property | Type | Default | Description |
|---|---|---|---|
| `Title` | `string` | `""` | Primary text |
| `TitleColor` | `Color?` | `null` | Title color |
| `TitleFontSize` | `double` | `-1` | Title font size |
| `TitleFontFamily` | `string?` | `null` | Title font family |
| `TitleFontAttributes` | `FontAttributes?` | `null` | Bold, Italic, None |
| `Description` | `string` | `""` | Subtitle below title |
| `DescriptionColor` | `Color?` | `null` | Description color |
| `DescriptionFontSize` | `double` | `-1` | Description font size |
| `HintText` | `string` | `""` | Right of title hint |
| `HintTextColor` | `Color?` | `null` | Hint color |
| `IconSource` | `ImageSource?` | `null` | Left icon |
| `IconSize` | `double` | `-1` | Icon dimensions |
| `IconRadius` | `double` | `-1` | Icon corner radius |
| `CellBackgroundColor` | `Color?` | `null` | Background color |
| `SelectedColor` | `Color?` | `null` | Tap highlight color |
| `IsSelectable` | `bool` | `true` | Responds to taps |
| `CellHeight` | `double` | `-1` | Fixed height |
| `BorderColor` | `Color?` | `null` | Border color |
| `BorderWidth` | `double` | `-1` | Border width |
| `BorderRadius` | `double` | `-1` | Border corner radius |
| `UseFeedback` | `bool` | `true` | Feedback on cell tap |

## TableSection Properties

```xml
<shiny:TableSection Title="GENERAL" FooterText="These settings apply globally"
                  HeaderBackgroundColor="#F2F2F7" HeaderTextColor="#666666"
                  UseDragSort="False">
    <!-- cells -->
</shiny:TableSection>
```

| Property | Type | Default |
|---|---|---|
| `Title` | `string` | `""` |
| `FooterText` | `string` | `""` |
| `HeaderView` | `View?` | `null` |
| `FooterView` | `View?` | `null` |
| `IsVisible` | `bool` | `true` |
| `FooterVisible` | `bool` | `true` |
| `HeaderBackgroundColor` | `Color?` | `null` |
| `HeaderTextColor` | `Color?` | `null` |
| `HeaderFontSize` | `double` | `-1` |
| `HeaderFontFamily` | `string?` | `null` |
| `HeaderFontAttributes` | `FontAttributes?` | `null` |
| `HeaderHeight` | `double` | `-1` |
| `FooterTextColor` | `Color?` | `null` |
| `FooterFontSize` | `double` | `-1` |
| `FooterBackgroundColor` | `Color?` | `null` |
| `UseDragSort` | `bool` | `false` |
| `ItemsSource` | `IEnumerable?` | `null` |
| `ItemTemplate` | `DataTemplate?` | `null` |

`IsVisible` is live and safe to bind — a section that flips re-renders the table, and a hidden section
draws nothing at all: no header, no cells, and **no section separator on either side of it**. Binding
it to a feature flag or a mode switch is the intended use, **including to a `SwitchCell.On` in the same
table** (a Features switch that reveals that feature's settings section). The re-render is dispatched to
just after the change and coalesced, so no workaround — such as pinning cell BindingContexts — is needed.

> Note the difference from a **cell's** `IsVisible`, which a section reads when it renders and never
> again. A cell whose visibility is set after the section is built may never appear; a section's can
> change whenever you like.
| `TemplateStartIndex` | `int` | `0` |

## Dynamic Cells with ItemTemplate

Generate cells from a data source:

```xml
<shiny:TableSection Title="Items" ItemsSource="{Binding Items}">
    <shiny:TableSection.ItemTemplate>
        <DataTemplate>
            <shiny:LabelCell Title="{Binding Name}" ValueText="{Binding Value}" />
        </DataTemplate>
    </shiny:TableSection.ItemTemplate>
</shiny:TableSection>
```

The `ItemsSource` supports `INotifyCollectionChanged` for live updates.

## Global Styling (TableView Properties)

Apply styles at the TableView level. Individual cell/section properties override globals.

```xml
<shiny:TableView CellTitleColor="#333333"
              CellTitleFontSize="17"
              CellDescriptionColor="#888888"
              CellValueTextColor="#007AFF"
              CellBackgroundColor="White"
              CellSelectedColor="#EFEFEF"
              CellAccentColor="#007AFF"
              CellIconSize="28"
              HeaderTextColor="#666666"
              HeaderFontSize="13"
              HeaderBackgroundColor="#F2F2F7"
              FooterTextColor="#8E8E93"
              SeparatorColor="#C6C6C8"
              SeparatorPadding="16"
              SectionSeparatorHeight="12">
```

### Cell Global Styles

| Property | Type | Description |
|---|---|---|
| `CellTitleColor` | `Color?` | Title color for all cells |
| `CellTitleFontSize` | `double` | Title font size |
| `CellTitleFontFamily` | `string?` | Title font family |
| `CellTitleFontAttributes` | `FontAttributes?` | Title styling |
| `CellDescriptionColor` | `Color?` | Description color |
| `CellDescriptionFontSize` | `double` | Description font size |
| `CellHintTextColor` | `Color?` | Hint text color |
| `CellHintTextFontSize` | `double` | Hint font size |
| `CellValueTextColor` | `Color?` | Value text color |
| `CellValueTextFontSize` | `double` | Value font size |
| `CellBackgroundColor` | `Color?` | Cell background |
| `CellSelectedColor` | `Color?` | Tap highlight color |
| `CellAccentColor` | `Color?` | Switches, checkboxes, radios |
| `CellIconSize` | `double` | Icon dimensions |
| `CellIconRadius` | `double` | Icon corner radius |
| `CellPadding` | `Thickness?` | Cell content padding |
| `CellBorderColor` | `Color?` | Cell border color |
| `CellBorderWidth` | `double` | Cell border width |
| `CellBorderRadius` | `double` | Cell border corner radius |

### Header/Footer Global Styles

| Property | Type | Default |
|---|---|---|
| `HeaderBackgroundColor` | `Color?` | `null` |
| `HeaderTextColor` | `Color?` | `null` |
| `HeaderFontSize` | `double` | `-1` |
| `HeaderFontFamily` | `string?` | `null` |
| `HeaderFontAttributes` | `FontAttributes` | `Bold` |
| `HeaderPadding` | `Thickness` | `14,8,8,8` |
| `HeaderHeight` | `double` | `-1` |
| `HeaderTextVerticalAlign` | `LayoutAlignment` | `End` |
| `FooterTextColor` | `Color?` | `null` |
| `FooterFontSize` | `double` | `-1` |
| `FooterFontAttributes` | `FontAttributes` | `None` |
| `FooterPadding` | `Thickness` | `14,8,8,8` |
| `FooterBackgroundColor` | `Color?` | `null` |

### Separator/Section Styles

| Property | Type | Default |
|---|---|---|
| `SeparatorColor` | `Color?` | `null` |
| `SeparatorHeight` | `double` | `0.5` |
| `SeparatorPadding` | `double` | `16` |
| `ShowSectionSeparator` | `bool` | `true` |
| `SectionSeparatorHeight` | `double` | `8` |
| `SectionSeparatorColor` | `Color?` | `null` |

## Drag & Sort

`UseDragSort="True"` puts a drag handle (☰) on every row in the section. Dragging a handle lifts
the row under the finger, shows an insertion line at the drop position, and auto-scrolls when the
drag reaches the top or bottom edge of the table. Rows only reorder within their own section, and
touches anywhere other than a handle still scroll the table normally.

```xml
<shiny:TableView ItemDroppedCommand="{Binding ItemDroppedCommand}">
    <shiny:TableRoot>
        <shiny:TableSection Title="Reorder" UseDragSort="True">
            <shiny:LabelCell Title="First" ValueText="1" />
            <shiny:LabelCell Title="Second" ValueText="2" />
            <shiny:LabelCell Title="Third" ValueText="3" />
        </shiny:TableSection>
    </shiny:TableRoot>
</shiny:TableView>
```

The `ItemDroppedCommand` receives `ItemDroppedEventArgs` with `Section`, `Cell`, `Item`,
`FromIndex`, and `ToIndex`. The indices are positions among the section's rendered rows.

Cells declared in XAML are reordered by the control itself - the handler is only a notification.

### Drag sort on a templated section

Rows built from `ItemsSource`/`ItemTemplate` are **not** reordered for you: their order lives in your
collection. Move the item yourself using `Item` (the moved row's binding context) and `ToIndex`:

```xml
<shiny:TableSection Title="Players" UseDragSort="True" ItemsSource="{Binding Players}">
    <shiny:TableSection.ItemTemplate>
        <DataTemplate>
            <shiny:LabelCell Title="{Binding Name}" ValueText="{Binding Position}" />
        </DataTemplate>
    </shiny:TableSection.ItemTemplate>
</shiny:TableSection>
```

```csharp
void OnItemDropped(ItemDroppedEventArgs args)
{
    if (args.Item is Player player)
    {
        var from = this.Players.IndexOf(player);
        if (from >= 0 && from != args.ToIndex)
            this.Players.Move(from, args.ToIndex);
    }
}
```

## Scroll Control

```xml
<shiny:TableView ScrollToTop="{Binding ShouldScrollTop}" ScrollToBottom="{Binding ShouldScrollBottom}" />
```

```csharp
await tableView.ScrollToTopAsync();
await tableView.ScrollToBottomAsync();
```

## TableView Events

| Event | Args | Description |
|---|---|---|
| `ItemDropped` | `ItemDroppedEventArgs` | Cell reordered via drag sort |
| `ModelChanged` | `EventArgs` | Root/sections/cells changed |
| `CellPropertyChanged` | `CellPropertyChangedEventArgs` | Cell property changed |

### Section headers

Leave `HeaderBackgroundColor`, `HeaderTextColor`, `FooterTextColor`, `SeparatorColor` and
`SectionSeparatorColor` unset — they resolve from theme tokens and follow a theme swap or an
appearance flip live. Leave `HeaderFontSize` unset too; it resolves to the theme's `BodySmallSize`.

Section titles are **drawn** uppercase on both hosts (MAUI via `HeaderTextTransform`, Blazor via
CSS). Write `Title` in normal sentence case — `Title="Toggle Controls"`, not `"TOGGLE CONTROLS"` —
because the transform is presentation only and the bound string is what bindings, accessibility and
tests see. Set `HeaderTextTransform="None"` on MAUI if a design wants sentence case.
