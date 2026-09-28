using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Spreadsheet;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet.Calc;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>A defined name: a label for a range, a constant or a formula.</summary>
/// <param name="Name">The name as written in formulas.</param>
/// <param name="Formula">What it refers to, without a leading <c>=</c> — <c>Sheet1!$A$1:$A$10</c>.</param>
/// <param name="Scope">The sheet it is local to, or null for the whole workbook.</param>
/// <param name="Hidden">Hidden names are Excel's own bookkeeping (<c>_xlnm._FilterDatabase</c>) and not offered to the user.</param>
/// <param name="Comment">The description shown in Excel's Name Manager.</param>
public sealed record DefinedNameInfo(string Name, string Formula, string? Scope, bool Hidden = false, string? Comment = null)
{
    /// <summary>True for the names Excel reserves — print areas, print titles, filter databases.</summary>
    public bool IsBuiltIn => this.Name.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase);
}

public sealed partial class Workbook
{
    readonly Dictionary<(string Name, string Sheet), FormulaNode?> nameNodes = new();

    /// <summary>Every defined name in the workbook, in file order.</summary>
    public IReadOnlyList<DefinedNameInfo> DefinedNames
    {
        get
        {
            var order = this.XmlSheetOrder();
            var result = new List<DefinedNameInfo>();

            foreach (var defined in this.workbookElement.DefinedNames?.Elements<DefinedName>() ?? [])
            {
                if (defined.Name?.Value is not { Length: > 0 } name)
                    continue;

                string? scope = null;
                if (defined.LocalSheetId?.Value is { } id && id < order.Count)
                    scope = order[(int)id].Name?.Value;

                result.Add(new DefinedNameInfo(name, defined.Text ?? string.Empty, scope, defined.Hidden?.Value ?? false, defined.Comment?.Value));
            }

            return result;
        }
    }

    /// <summary>The names a user can see and pick — everything but Excel's own hidden and reserved ones.</summary>
    public IEnumerable<DefinedNameInfo> VisibleNames => this.DefinedNames.Where(x => !x.Hidden && !x.IsBuiltIn);

    /// <summary>
    /// The name visible from <paramref name="sheet"/>: one scoped to that sheet first, then a
    /// workbook-wide one. Null when there is neither.
    /// </summary>
    public DefinedNameInfo? FindName(string name, string? sheet = null)
    {
        var names = this.DefinedNames;
        return names.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)
                                          && sheet is not null && string.Equals(x.Scope, sheet, StringComparison.OrdinalIgnoreCase))
               ?? names.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && x.Scope is null);
    }

    /// <summary>A name resolved to the range it covers, when it covers one, for Go To and the name box.</summary>
    public (string Sheet, CellRange Range)? ResolveNameRange(string name, string? sheet = null)
    {
        if (this.FindName(name, sheet) is not { } info)
            return null;

        if (!FormulaParser.TryParse(info.Formula, out var node, out _))
            return null;

        return node switch
        {
            RangeNode range => (range.Sheet ?? info.Scope ?? sheet ?? this.sheets[0].Name, range.Range),
            ReferenceNode reference => (reference.Sheet ?? info.Scope ?? sheet ?? this.sheets[0].Name, new CellRange(reference.Cell.Relative())),
            _ => null
        };
    }

    /// <summary>The parsed formula a name stands for, cached until the names change.</summary>
    internal FormulaNode? ResolveNameNode(string name, string sheet)
    {
        var key = (name.ToUpperInvariant(), sheet.ToUpperInvariant());
        if (this.nameNodes.TryGetValue(key, out var cached))
            return cached;

        FormulaNode? node = null;
        if (this.FindName(name, sheet) is { } info && FormulaParser.TryParse(info.Formula, out var parsed, out _))
            node = parsed;

        this.nameNodes[key] = node;
        return node;
    }

    /// <summary>
    /// Adds, replaces or removes a defined name. The working end of <see cref="SetDefinedNameCommand"/>.
    /// </summary>
    internal DefinedNameInfo? WriteDefinedName(string name, string? scope, DefinedNameInfo? value)
    {
        var order = this.XmlSheetOrder();
        uint? scopeId = null;

        if (scope is not null)
        {
            var index = order.FindIndex(x => string.Equals(x.Name?.Value, scope, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new ArgumentException($"No sheet named '{scope}'.", nameof(scope));

            scopeId = (uint)index;
        }

        var names = this.workbookElement.DefinedNames;
        var existing = names?.Elements<DefinedName>().FirstOrDefault(x =>
            string.Equals(x.Name?.Value, name, StringComparison.OrdinalIgnoreCase) && x.LocalSheetId?.Value == scopeId);

        DefinedNameInfo? previous = existing is null
            ? null
            : new DefinedNameInfo(existing.Name!.Value!, existing.Text ?? string.Empty, scope, existing.Hidden?.Value ?? false, existing.Comment?.Value);

        if (value is null)
        {
            existing?.Remove();
            if (names is not null && !names.Elements<DefinedName>().Any())
                names.Remove();
        }
        else
        {
            if (names is null)
            {
                // The typed setter puts it in CT_Workbook's sequence - after sheets, before calcPr.
                names = new DefinedNames();
                this.workbookElement.DefinedNames = names;
            }

            var element = existing ?? names.AppendChild(new DefinedName());
            element.Name = value.Name;
            element.Text = value.Formula.TrimStart('=');
            element.LocalSheetId = scopeId is { } id ? id : null;
            element.Hidden = value.Hidden ? true : null;
            element.Comment = string.IsNullOrEmpty(value.Comment) ? null : value.Comment;
        }

        this.AfterNamesChanged();
        return previous;
    }

    void AfterNamesChanged()
    {
        this.nameNodes.Clear();
        this.RebuildCalc();
        this.OnContentChanged();
    }

    /// <summary>Evaluates an expression and returns every value it produces — a range comes back whole.</summary>
    public IReadOnlyList<CellValue> EvaluateValues(string formula, string sheetName, CellRef origin)
    {
        this.EnsureFormulasLoaded();
        return this.Calc.EvaluateOnceValues(formula, new RebasedCalcContext(this.calcContext, sheetName, origin));
    }

    /// <summary>Evaluates an expression keeping a range's rows and columns — what a chart series reads.</summary>
    public CalcValue EvaluateRaw(string formula, string sheetName, CellRef origin)
    {
        this.EnsureFormulasLoaded();
        return this.Calc.EvaluateOnceRaw(formula, new RebasedCalcContext(this.calcContext, sheetName, origin));
    }

    /// <summary>Recomputes every formula now — the Formulas tab's Calculate Now.</summary>
    public void RecalculateAll()
    {
        this.EnsureFormulasLoaded();
        this.Calc.RecalculateAll(this.calcContext);
        this.Revision++;
    }
}

/// <summary>What Excel allows a defined name to be.</summary>
public static partial class DefinedNameRules
{
    [GeneratedRegex(@"^[A-Za-z_\\][A-Za-z0-9_.\\]*$")]
    private static partial Regex Shape();

    [GeneratedRegex(@"^[Rr]\d*[Cc]\d*$|^[Rr]$|^[Cc]$")]
    private static partial Regex R1C1();

    public static bool IsValid(string? name, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "A name cannot be empty.";
            return false;
        }

        if (name.Length > 255)
        {
            error = "A name can be at most 255 characters.";
            return false;
        }

        if (!Shape().IsMatch(name))
        {
            error = "A name must start with a letter, underscore or backslash, and contain only letters, numbers, periods and underscores.";
            return false;
        }

        if (CellRef.TryParse(name, out _) || R1C1().IsMatch(name) ||
            name.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || name.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            error = "A name cannot look like a cell reference.";
            return false;
        }

        return true;
    }
}

/// <summary>Adds, changes or removes a defined name, as one undo step.</summary>
/// <param name="Name">The name to set.</param>
/// <param name="Scope">The sheet it is local to, or null for the workbook.</param>
/// <param name="Value">What it should become, or null to delete it.</param>
public sealed class SetDefinedNameCommand(string name, string? scope, DefinedNameInfo? value) : IEditCommand<Workbook>
{
    public string DefinedName { get; } = name;
    public string? Scope { get; } = scope;
    public DefinedNameInfo? Value { get; } = value;

    public string Name => this.Value is null ? "Delete Name" : "Define Name";

    public IEditCommand<Workbook> Apply(Workbook context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var previous = context.WriteDefinedName(this.DefinedName, this.Scope, this.Value);

        // A rename is a delete of the old spelling plus an add of the new one, so the inverse has to
        // name what this wrote rather than what it was asked to write.
        var written = this.Value?.Name ?? this.DefinedName;
        return new SetDefinedNameCommand(written, this.Scope, previous);
    }
}
