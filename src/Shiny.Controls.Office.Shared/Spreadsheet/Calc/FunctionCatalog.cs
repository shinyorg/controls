namespace Shiny.Controls.Office.Spreadsheet.Calc;

/// <summary>What the Insert Function dialog and formula autocomplete say about a function.</summary>
/// <param name="Name">The function name, upper case.</param>
/// <param name="Category">Excel's Function Library category.</param>
/// <param name="Arguments">The argument names in order; optional ones in square brackets.</param>
/// <param name="Description">One line, in Excel's own wording where it has one.</param>
public sealed record FunctionInfo(string Name, string Category, IReadOnlyList<string> Arguments, string Description)
{
    /// <summary><c>NAME(arg1, [arg2], ...)</c> — the signature a tooltip shows.</summary>
    public string Signature => $"{this.Name}({string.Join(", ", this.Arguments)})";
}

/// <summary>
/// The catalogue behind the Formulas tab's Function Library, the Insert Function dialog and formula
/// autocomplete: every function the engine implements, with a category, its arguments and a line of
/// description.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="FunctionRegistry"/> on purpose — the registry is what evaluates, this is
/// what is shown. A unit test holds the two together, so a function cannot be added to one and not the
/// other.
/// </remarks>
public static class FunctionCatalog
{
    public const string Financial = "Financial";
    public const string Logical = "Logical";
    public const string Text = "Text";
    public const string DateTime = "Date & Time";
    public const string Lookup = "Lookup & Reference";
    public const string Math = "Math & Trig";
    public const string Statistical = "Statistical";
    public const string Information = "Information";

    /// <summary>The categories, in the order Excel's Function Library lays them out.</summary>
    public static IReadOnlyList<string> Categories { get; } = [Financial, Logical, Text, DateTime, Lookup, Math, Statistical, Information];

    // Name | category | arguments separated by ';' | description
    const string Table = """
        PMT|Financial|rate;nper;pv;[fv];[type]|Calculates the payment for a loan based on constant payments and a constant interest rate.
        PV|Financial|rate;nper;pmt;[fv];[type]|Returns the present value of an investment.
        FV|Financial|rate;nper;pmt;[pv];[type]|Returns the future value of an investment based on periodic, constant payments and a constant interest rate.
        NPV|Financial|rate;value1;[value2];...|Returns the net present value of an investment based on a discount rate and a series of future payments.
        IRR|Financial|values;[guess]|Returns the internal rate of return for a series of cash flows.
        RATE|Financial|nper;pmt;pv;[fv];[type];[guess]|Returns the interest rate per period of a loan or an annuity.
        NPER|Financial|rate;pmt;pv;[fv];[type]|Returns the number of periods for an investment.
        IPMT|Financial|rate;per;nper;pv;[fv];[type]|Returns the interest payment for a given period of an investment.
        PPMT|Financial|rate;per;nper;pv;[fv];[type]|Returns the payment on the principal for a given period of an investment.
        SLN|Financial|cost;salvage;life|Returns the straight-line depreciation of an asset for one period.
        IF|Logical|logical_test;[value_if_true];[value_if_false]|Checks whether a condition is met, and returns one value if TRUE and another if FALSE.
        IFS|Logical|logical_test1;value_if_true1;...|Checks whether one or more conditions are met and returns the value of the first TRUE condition.
        IFERROR|Logical|value;value_if_error|Returns value_if_error if the expression is an error, and the value of the expression otherwise.
        IFNA|Logical|value;value_if_na|Returns the value you specify if the expression resolves to #N/A.
        AND|Logical|logical1;[logical2];...|Checks whether all arguments are TRUE.
        OR|Logical|logical1;[logical2];...|Checks whether any of the arguments are TRUE.
        XOR|Logical|logical1;[logical2];...|Returns a logical exclusive OR of all arguments.
        NOT|Logical|logical|Changes FALSE to TRUE, or TRUE to FALSE.
        TRUE|Logical||Returns the logical value TRUE.
        FALSE|Logical||Returns the logical value FALSE.
        SWITCH|Logical|expression;value1;result1;[default]|Evaluates an expression against a list of values and returns the result of the first match.
        LEN|Text|text|Returns the number of characters in a text string.
        LOWER|Text|text|Converts all letters in a text string to lowercase.
        UPPER|Text|text|Converts a text string to all uppercase letters.
        PROPER|Text|text|Capitalizes the first letter of each word in a text string.
        TRIM|Text|text|Removes all spaces from a text string except for single spaces between words.
        LEFT|Text|text;[num_chars]|Returns the specified number of characters from the start of a text string.
        RIGHT|Text|text;[num_chars]|Returns the specified number of characters from the end of a text string.
        MID|Text|text;start_num;num_chars|Returns the characters from the middle of a text string, given a starting position and length.
        CONCAT|Text|text1;[text2];...|Concatenates a list or range of text strings.
        CONCATENATE|Text|text1;[text2];...|Joins several text strings into one text string.
        TEXTJOIN|Text|delimiter;ignore_empty;text1;[text2];...|Concatenates a list or range of text strings using a delimiter.
        REPT|Text|text;number_times|Repeats text a given number of times.
        EXACT|Text|text1;text2|Checks whether two text strings are exactly the same, case-sensitively.
        FIND|Text|find_text;within_text;[start_num]|Returns the starting position of one text string within another, case-sensitively.
        SEARCH|Text|find_text;within_text;[start_num]|Returns the position of a character or text string, reading left to right, not case-sensitive.
        SUBSTITUTE|Text|text;old_text;new_text;[instance_num]|Replaces existing text with new text in a text string.
        REPLACE|Text|old_text;start_num;num_chars;new_text|Replaces part of a text string with a different text string.
        VALUE|Text|text|Converts a text string that represents a number to a number.
        TEXT|Text|value;format_text|Converts a value to text in a specific number format.
        T|Text|value|Returns the text referred to by value.
        CHAR|Text|number|Returns the character specified by the code number.
        CODE|Text|text|Returns a numeric code for the first character in a text string.
        TODAY|Date & Time||Returns the current date formatted as a date.
        NOW|Date & Time||Returns the current date and time formatted as a date and time.
        DATE|Date & Time|year;month;day|Returns the number that represents the date.
        TIME|Date & Time|hour;minute;second|Converts hours, minutes and seconds given as numbers to a serial number.
        YEAR|Date & Time|serial_number|Returns the year of a date, an integer in the range 1900-9999.
        MONTH|Date & Time|serial_number|Returns the month, a number from 1 (January) to 12 (December).
        DAY|Date & Time|serial_number|Returns the day of the month, a number from 1 to 31.
        HOUR|Date & Time|serial_number|Returns the hour as a number from 0 (12:00 A.M.) to 23 (11:00 P.M.).
        MINUTE|Date & Time|serial_number|Returns the minute, a number from 0 to 59.
        SECOND|Date & Time|serial_number|Returns the second, a number from 0 to 59.
        WEEKDAY|Date & Time|serial_number;[return_type]|Returns a number from 1 to 7 identifying the day of the week of a date.
        EOMONTH|Date & Time|start_date;months|Returns the serial number of the last day of the month before or after a specified number of months.
        EDATE|Date & Time|start_date;months|Returns the serial number of the date that is the indicated number of months before or after the start date.
        DAYS|Date & Time|end_date;start_date|Returns the number of days between the two dates.
        DATEVALUE|Date & Time|date_text|Converts a date in the form of text to a number that represents the date.
        XLOOKUP|Lookup & Reference|lookup_value;lookup_array;return_array;[if_not_found];[match_mode];[search_mode]|Searches a range for a match and returns the corresponding item from a second range.
        XMATCH|Lookup & Reference|lookup_value;lookup_array;[match_mode];[search_mode]|Returns the relative position of an item in an array.
        VLOOKUP|Lookup & Reference|lookup_value;table_array;col_index_num;[range_lookup]|Looks for a value in the leftmost column of a table, and returns a value in the same row from a column you specify.
        HLOOKUP|Lookup & Reference|lookup_value;table_array;row_index_num;[range_lookup]|Looks for a value in the top row of a table and returns the value in the same column from a row you specify.
        INDEX|Lookup & Reference|array;row_num;[column_num]|Returns a value from a table or range, by row and column position.
        MATCH|Lookup & Reference|lookup_value;lookup_array;[match_type]|Returns the relative position of an item in an array that matches a specified value.
        CHOOSE|Lookup & Reference|index_num;value1;[value2];...|Chooses a value from a list of values, based on an index number.
        ROW|Lookup & Reference|[reference]|Returns the row number of a reference.
        COLUMN|Lookup & Reference|[reference]|Returns the column number of a reference.
        ROWS|Lookup & Reference|array|Returns the number of rows in a reference or array.
        COLUMNS|Lookup & Reference|array|Returns the number of columns in a reference or array.
        SUM|Math & Trig|number1;[number2];...|Adds all the numbers in a range of cells.
        SUMIF|Math & Trig|range;criteria;[sum_range]|Adds the cells specified by a given condition or criteria.
        SUMIFS|Math & Trig|sum_range;criteria_range1;criteria1;...|Adds the cells specified by a given set of conditions or criteria.
        SUMPRODUCT|Math & Trig|array1;[array2];...|Returns the sum of the products of corresponding ranges or arrays.
        SUBTOTAL|Math & Trig|function_num;ref1;[ref2];...|Returns a subtotal in a list or database, skipping other subtotals.
        AGGREGATE|Math & Trig|function_num;options;ref1;...|Returns an aggregate in a list or database, optionally ignoring hidden rows and errors.
        PRODUCT|Math & Trig|number1;[number2];...|Multiplies all the numbers given as arguments.
        ABS|Math & Trig|number|Returns the absolute value of a number.
        SIGN|Math & Trig|number|Returns the sign of a number: 1 if positive, zero if zero, -1 if negative.
        INT|Math & Trig|number|Rounds a number down to the nearest integer.
        TRUNC|Math & Trig|number;[num_digits]|Truncates a number to an integer by removing the fractional part.
        ROUND|Math & Trig|number;num_digits|Rounds a number to a specified number of digits.
        ROUNDUP|Math & Trig|number;num_digits|Rounds a number up, away from zero.
        ROUNDDOWN|Math & Trig|number;num_digits|Rounds a number down, toward zero.
        MOD|Math & Trig|number;divisor|Returns the remainder after a number is divided by a divisor.
        POWER|Math & Trig|number;power|Returns the result of a number raised to a power.
        SQRT|Math & Trig|number|Returns the square root of a number.
        EXP|Math & Trig|number|Returns e raised to the power of a given number.
        LN|Math & Trig|number|Returns the natural logarithm of a number.
        LOG10|Math & Trig|number|Returns the base-10 logarithm of a number.
        LOG|Math & Trig|number;[base]|Returns the logarithm of a number to the base you specify.
        PI|Math & Trig||Returns the value of Pi, 3.14159265358979, accurate to 15 digits.
        SIN|Math & Trig|number|Returns the sine of an angle.
        COS|Math & Trig|number|Returns the cosine of an angle.
        TAN|Math & Trig|number|Returns the tangent of an angle.
        ATAN|Math & Trig|number|Returns the arctangent of a number, in radians.
        ATAN2|Math & Trig|x_num;y_num|Returns the arctangent of the specified x- and y-coordinates, in radians.
        DEGREES|Math & Trig|angle|Converts radians to degrees.
        RADIANS|Math & Trig|angle|Converts degrees to radians.
        CEILING|Math & Trig|number;significance|Rounds a number up, to the nearest multiple of significance.
        FLOOR|Math & Trig|number;significance|Rounds a number down, toward zero, to the nearest multiple of significance.
        AVERAGE|Statistical|number1;[number2];...|Returns the average (arithmetic mean) of its arguments.
        AVERAGEIF|Statistical|range;criteria;[average_range]|Finds the average for the cells specified by a given condition or criteria.
        AVERAGEIFS|Statistical|average_range;criteria_range1;criteria1;...|Finds the average for the cells specified by a given set of conditions or criteria.
        COUNT|Statistical|value1;[value2];...|Counts the number of cells in a range that contain numbers.
        COUNTA|Statistical|value1;[value2];...|Counts the number of cells in a range that are not empty.
        COUNTBLANK|Statistical|range|Counts the number of empty cells in a specified range of cells.
        COUNTIF|Statistical|range;criteria|Counts the number of cells within a range that meet the given condition.
        COUNTIFS|Statistical|criteria_range1;criteria1;...|Counts the number of cells specified by a given set of conditions or criteria.
        MAX|Statistical|number1;[number2];...|Returns the largest value in a set of values.
        MAXIFS|Statistical|max_range;criteria_range1;criteria1;...|Returns the maximum value among cells specified by a given set of conditions.
        MIN|Statistical|number1;[number2];...|Returns the smallest number in a set of values.
        MINIFS|Statistical|min_range;criteria_range1;criteria1;...|Returns the minimum value among cells specified by a given set of conditions.
        MEDIAN|Statistical|number1;[number2];...|Returns the median, or the number in the middle of the set of given numbers.
        LARGE|Statistical|array;k|Returns the k-th largest value in a data set.
        SMALL|Statistical|array;k|Returns the k-th smallest value in a data set.
        STDEV|Statistical|number1;[number2];...|Estimates standard deviation based on a sample.
        STDEVP|Statistical|number1;[number2];...|Calculates standard deviation based on the entire population.
        STDEV.S|Statistical|number1;[number2];...|Estimates standard deviation based on a sample.
        STDEV.P|Statistical|number1;[number2];...|Calculates standard deviation based on the entire population.
        VAR|Statistical|number1;[number2];...|Estimates variance based on a sample.
        VARP|Statistical|number1;[number2];...|Calculates variance based on the entire population.
        VAR.S|Statistical|number1;[number2];...|Estimates variance based on a sample.
        VAR.P|Statistical|number1;[number2];...|Calculates variance based on the entire population.
        RANK|Statistical|number;ref;[order]|Returns the rank of a number in a list of numbers.
        RANK.EQ|Statistical|number;ref;[order]|Returns the rank of a number in a list of numbers.
        ISERROR|Information|value|Checks whether a value is an error, and returns TRUE or FALSE.
        ISERR|Information|value|Checks whether a value is an error other than #N/A, and returns TRUE or FALSE.
        ISNA|Information|value|Checks whether a value is #N/A, and returns TRUE or FALSE.
        ISBLANK|Information|value|Checks whether a reference is to an empty cell, and returns TRUE or FALSE.
        ISNUMBER|Information|value|Checks whether a value is a number, and returns TRUE or FALSE.
        ISTEXT|Information|value|Checks whether a value is text, and returns TRUE or FALSE.
        ISNONTEXT|Information|value|Checks whether a value is not text, and returns TRUE or FALSE.
        ISLOGICAL|Information|value|Checks whether a value is a logical value, and returns TRUE or FALSE.
        ISEVEN|Information|number|Returns TRUE if the number is even.
        ISODD|Information|number|Returns TRUE if the number is odd.
        NA|Information||Returns the error value #N/A.
        N|Information|value|Converts a non-number value to a number, dates to serial numbers, TRUE to 1, anything else to 0.
        TYPE|Information|value|Returns an integer representing the data type of a value.
        ERROR.TYPE|Information|error_val|Returns a number matching an error value.
        """;

    static readonly Lazy<IReadOnlyDictionary<string, FunctionInfo>> ByName = new(() =>
    {
        var map = new Dictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in Table.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var parts = line.Split('|');
            var arguments = parts[2].Length == 0 ? [] : parts[2].Split(';');
            map[parts[0]] = new FunctionInfo(parts[0], parts[1], arguments, parts[3]);
        }

        return map;
    });

    /// <summary>Every catalogued function, alphabetically.</summary>
    public static IReadOnlyList<FunctionInfo> All => ByName.Value.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();

    public static FunctionInfo? Find(string name) => ByName.Value.GetValueOrDefault(name);

    /// <summary>The functions in one category, alphabetically.</summary>
    public static IReadOnlyList<FunctionInfo> InCategory(string category)
        => All.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// The Insert Function dialog's search: names starting with the text first, then names containing
    /// it, then descriptions mentioning it.
    /// </summary>
    public static IReadOnlyList<FunctionInfo> Search(string? text, string? category = null)
    {
        var pool = category is null ? All : InCategory(category);
        if (string.IsNullOrWhiteSpace(text))
            return pool;

        var query = text.Trim();
        return pool
            .Select(x => (Info: x, Score:
                x.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 :
                x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 :
                x.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : 3))
            .Where(x => x.Score < 3)
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Info.Name, StringComparer.Ordinal)
            .Select(x => x.Info)
            .ToList();
    }
}
