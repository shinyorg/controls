# Scratch Card

A scratch-off card: a **cover view** over a **hidden view**, scratched away by finger or mouse. Both hosts have it, in the **core** packages (`Shiny.Maui.Controls` namespace on MAUI, `Shiny.Blazor.Controls` on Blazor).

Both layers are live views. The cover can be anything (an `Image`, a gradient `Grid`, a layout); null draws a silver foil.

## MAUI

```xml
<Border StrokeShape="RoundRectangle 16" StrokeThickness="0" HeightRequest="180">
    <shiny:ScratchCard ThresholdPercent="55"
                       StrokeWidth="44"
                       ThresholdReached="OnThresholdReached"
                       IsRevealed="{Binding IsRevealed}">
        <shiny:ScratchCard.Cover>
            <Image Source="foil.png" Aspect="AspectFill" />
        </shiny:ScratchCard.Cover>
        <Label Text="You won!" HorizontalOptions="Center" VerticalOptions="Center" />
    </shiny:ScratchCard>
</Border>
```

- The hidden view is the XAML content property (`HiddenContent`). The cover goes in `<shiny:ScratchCard.Cover>`.
- The card has no size of its own beyond its content. Give it a height (`HeightRequest`, or a `Border`/`Grid` row around it).
- For rounded corners, wrap it in a `Border` with `StrokeThickness="0"`. Never add a second visible border around one that already has one.
- It works inside a `ScrollView`: the scroller is locked for the length of a stroke.
- `ThresholdReached` (`EventHandler<ScratchProgressEventArgs>`, `.Percent` 0–100) and `ThresholdReachedCommand` (the parameter is the `double` percentage) fire once per cover. `AutoReveal` (default true) then clears the rest of the cover and raises `Revealed` / `RevealedCommand`.
- **The content takes no touches until the threshold** (MAUI: the hidden content is `InputTransparent`; Blazor: it is `inert`). Don't add your own blocking. At the threshold scratching stops and the content unlocks, even with `AutoReveal="False"` (the rest of the cover just stays painted).
- `ScratchProgressChanged` reports every change. `ScratchedPercent` is a read-only bindable property, 0–100.
- `IsRevealed` is two-way: set it true to reveal, false to reset. Methods: `Reveal(bool animate = true)`, `Reset()`.

## Blazor

```razor
<ScratchCard ThresholdPercent="55"
             OnThresholdReached="OnWin"
             @bind-IsRevealed="revealed"
             Style="width:320px; height:180px; border-radius:16px">
    <Cover><img src="foil.png" alt="" style="width:100%;height:100%;object-fit:cover" /></Cover>
    <ChildContent><div style="display:grid;place-items:center">You won!</div></ChildContent>
</ScratchCard>
```

- Use `<Cover>` + `<ChildContent>` together. With no cover, the content can go directly inside.
- Size it through `Style`/`Class`. Rounded corners come from `border-radius` on the card (it clips with `overflow:hidden`).
- Callbacks: `OnScratchStarted`, `OnScratchProgress` (`double` 0–100), `OnThresholdReached` (`double`), `OnRevealed`, `IsRevealedChanged`.
- `await card.Reveal()` / `await card.Reset()` through an `@ref`.

## Properties (both hosts)

`StrokeWidth` (44, about a fingertip; `ScratchCard.DefaultStrokeWidth`), `ThresholdPercent` (60, 0–100), `AutoReveal` (true), `IsScratchEnabled` (true), `RevealDuration` (300 ms; zero snaps), `IsRevealed`, `ScratchedPercent` (read-only).
