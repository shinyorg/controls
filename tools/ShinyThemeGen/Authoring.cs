namespace Shiny.ThemeGen;

/// <summary>
/// The bridge between the small set a human writes and the 55 roles every control consumes.
/// </summary>
/// <remarks>
/// Two directions, and both are needed.
/// <list type="bullet">
/// <item><see cref="FromScheme"/> reads the authoring values <em>out</em> of a Material scheme. That is
/// how the five existing packs migrate without anyone retyping them, and how the composer's "generate
/// from a brand colour" helper keeps working: seeds still build a full scheme, the authoring layer is
/// then lifted from it.</item>
/// <item><see cref="ToRoles"/> puts them back, resolving every role from the authoring values. The web
/// does not need this — CSS computes the same mixes itself, live, which is the whole point — but MAUI
/// holds resolved <c>Color</c>s in a dictionary and has nowhere to do the arithmetic at paint time.</item>
/// </list>
/// The two hosts therefore run the same derivation table, one at build time and one in the browser.
/// </remarks>
static class AuthoringLayer
{
    /// <summary>Lifts the authoring values out of a built scheme, then applies the theme's own overrides.</summary>
    public static Dictionary<string, string> FromScheme(
        IReadOnlyList<(string Role, string Hex)> scheme,
        IReadOnlyDictionary<string, string>? overrides
    )
    {
        var roles = scheme.ToDictionary(x => x.Role, x => x.Hex, StringComparer.Ordinal);
        var authoring = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var token in Tokens.AuthoringColors)
            authoring[token.Name] = roles[token.FromRole];

        if (overrides is not null)
        {
            foreach (var (name, value) in overrides)
            {
                if (!authoring.ContainsKey(name))
                    throw new InvalidOperationException($"'{name}' is not an authoring token. Known: {String.Join(", ", authoring.Keys.Order())}");

                authoring[name] = value;
            }
        }

        return authoring;
    }


    /// <summary>Resolves every role from the authoring values, in <see cref="Tokens.ColorRoles"/> order.</summary>
    public static IReadOnlyList<(string Role, string Hex)> ToRoles(IReadOnlyDictionary<string, string> authoring)
    {
        var derived = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (role, from) in Tokens.RoleDerivations)
        {
            derived[role] = from switch
            {
                Tokens.Alias a => authoring[a.Token],
                Tokens.MixInto m => ColorMath.Mix(authoring[m.Token], m.Percent, authoring[m.Into]),
                _ => throw new InvalidOperationException($"Unhandled derivation for {role}.")
            };
        }

        // Ordered by the contract rather than by the derivation table, so the emitted dictionaries and
        // the CSS keep the order every existing diff is against.
        return [.. Tokens.ColorRoles.Select(role => (role, derived[role]))];
    }


    /// <summary>The CSS value for a role — a plain reference, or the mix that produces it.</summary>
    public static string Css(Tokens.Derive from) => from switch
    {
        Tokens.Alias a => $"var(--shiny-{a.Token})",
        Tokens.MixInto m => $"color-mix(in oklab, var(--shiny-{m.Token}) {Pct(m.Percent)}%, var(--shiny-{m.Into}))",
        _ => throw new InvalidOperationException("Unhandled derivation.")
    };


    static string Pct(double v) => (v * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);


    /// <summary>
    /// Guards the table against the contract: every role derived exactly once, and no derivation
    /// naming a token that does not exist. A missing role would emit a CSS file that silently drops a
    /// variable every control reads.
    /// </summary>
    public static void Validate()
    {
        var known = Tokens.AuthoringColors.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var derived = Tokens.RoleDerivations.Select(x => x.Role).ToList();

        var missing = Tokens.ColorRoles.Except(derived, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"No derivation for: {String.Join(", ", missing)}");

        var extra = derived.Except(Tokens.ColorRoles, StringComparer.Ordinal).ToList();
        if (extra.Count > 0)
            throw new InvalidOperationException($"Derivation for unknown role: {String.Join(", ", extra)}");

        var duplicated = derived.GroupBy(x => x, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicated.Count > 0)
            throw new InvalidOperationException($"Derived more than once: {String.Join(", ", duplicated)}");

        foreach (var (role, from) in Tokens.RoleDerivations)
        {
            foreach (var token in from switch
            {
                Tokens.Alias a => new[] { a.Token },
                Tokens.MixInto m => [m.Token, m.Into],
                _ => []
            })
            {
                if (!known.Contains(token))
                    throw new InvalidOperationException($"{role} derives from unknown authoring token '{token}'.");
            }
        }
    }
}
