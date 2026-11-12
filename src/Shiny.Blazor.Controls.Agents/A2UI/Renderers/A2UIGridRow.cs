using System.Text.Json.Nodes;

namespace Shiny.Blazor.Controls.Agents.Renderers;

/// <summary>One DataGrid row: the bound item, where it lives in the data model, and its typed cell values for sorting.</summary>
/// <param name="Index">The row's index in the bound array.</param>
/// <param name="Path">The row's absolute data model path, or null when the rows were a literal.</param>
/// <param name="Item">The row object.</param>
/// <param name="Values">The cell values, one per column: numbers as double, dates as DateTimeOffset, the rest as strings.</param>
public sealed record A2UIGridRow(int Index, string? Path, JsonObject Item, object?[] Values);
