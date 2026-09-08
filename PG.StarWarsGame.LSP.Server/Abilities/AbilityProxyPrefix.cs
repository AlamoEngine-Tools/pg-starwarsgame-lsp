// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Abilities;

/// <summary>
///     Which ability a particle proxy's NAME suggests it belongs to.
/// </summary>
/// <remarks>
///     <para>
///         Measured over both shipped trees: 39 proxies carry one of these prefixes against
///         <strong>5404</strong> ordinary <c>p_*</c> ones. That ratio is why a lookup table is the
///         right size of solution and why a general "parse the name" scheme would be inventing
///         structure the data does not have.
///     </para>
///     <para>
///         <strong>The prefix is a HINT, not a rule.</strong> <c>Nv_ipv1</c> carries
///         <c>PTE_IPV1engine</c> while the object mounting it declares POWER_TO_WEAPONS and no TURBO;
///         <c>Ev_mdu_fieldgen</c> carries <c>prs_at-aa_fx</c> and declares no ability at all. So a
///         caller BINDS only when the object actually declares the type this names, and reports the
///         rest as unbound - which is normal authoring, not a problem worth a diagnostic.
///     </para>
/// </remarks>
public static class AbilityProxyPrefix
{
    /// <summary>
    ///     Prefix to ability type, with the shipped counts as foc / eaw.
    /// </summary>
    /// <remarks>
    ///     Ordered longest-first is unnecessary here - no prefix is a prefix of another - but the
    ///     lookup does not depend on order either way.
    /// </remarks>
    private static readonly (string Prefix, string Ability)[] Table =
    [
        ("PPTW_", "POWER_TO_WEAPONS"), // 20 / 8, e.g. Ev_2mtank:pptw_2mtank
        ("PTE_", "TURBO"), //             8 / 8,  e.g. Rv_corvette:PTE_Corvetteengines
        ("PRS_", "MISSILE_SHIELD"), //    4 / 2,  e.g. Ev_at-aa:prs_at-aa_fx
        ("PAS_", "SPRINT"), //            3 / 2,  e.g. Ri_chewbacca:pas_sprint
        ("PEM_", "INVULNERABILITY"), //   3 / 3,  e.g. Rv_mfalcon:pem_invulnerability
        ("PGW_", "INTERDICT") //          1 / 1,  e.g. Ev_interdictor:pgw_grav_well
    ];

    /// <summary>Every ability type a proxy prefix can name.</summary>
    public static IReadOnlyList<string> Abilities { get; } = [.. Table.Select(row => row.Ability)];

    /// <summary>
    ///     Matches a subject's particle proxies against the abilities it declares.
    /// </summary>
    /// <remarks>
    ///     The rule in one place, because it is a rule rather than plumbing: a proxy is BOUND only
    ///     when the object declares the type its prefix names. Everything else is either an ordinary
    ///     proxy - 5404 of them - which is ignored entirely, or an unbound one, which is reported.
    /// </remarks>
    public static AbilityProxyBinding Bind(
        IEnumerable<string> proxyNames, IEnumerable<string> declaredTypes)
    {
        ArgumentNullException.ThrowIfNull(proxyNames);
        ArgumentNullException.ThrowIfNull(declaredTypes);

        var declared = declaredTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suggested = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in proxyNames)
        {
            var ability = AbilityFor(name);
            if (ability is null)
                continue;

            if (!suggested.TryGetValue(ability, out var names))
            {
                names = [];
                suggested[ability] = names;
            }

            names.Add(name);
        }

        var bound = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var unbound = new List<(string, IReadOnlyList<string>)>();

        foreach (var (ability, names) in suggested)
            if (declared.Contains(ability))
                bound[ability] = names;
            else
                unbound.Add((ability, names));

        return new AbilityProxyBinding(bound, unbound);
    }

    /// <summary>
    ///     The ability <paramref name="proxyName" /> suggests, or null for the overwhelming
    ///     majority that suggest nothing.
    /// </summary>
    /// <remarks>
    ///     Case-insensitive throughout: the shipped names are written both ways - <c>pptw_2mtank</c>
    ///     and <c>PTE_Corvetteengines</c> - and <c>Ev_acclamator</c> writes its ability type in lower
    ///     case too.
    /// </remarks>
    public static string? AbilityFor(string? proxyName)
    {
        if (string.IsNullOrWhiteSpace(proxyName))
            return null;

        var name = proxyName.Trim();

        foreach (var (prefix, ability) in Table)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return ability;

        return null;
    }
}

/// <summary>
///     Which proxies each declared ability owns, and which belong to nothing.
/// </summary>
public sealed class AbilityProxyBinding
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _byAbility;

    internal AbilityProxyBinding(
        IReadOnlyDictionary<string, IReadOnlyList<string>> byAbility,
        IReadOnlyList<(string Ability, IReadOnlyList<string> ProxyNames)> unbound)
    {
        _byAbility = byAbility;
        Unbound = unbound;
    }

    /// <summary>
    ///     Proxies whose prefix names an ability the object does NOT declare.
    /// </summary>
    /// <remarks>
    ///     Normal authoring, not a defect - report at <c>info</c> and never as a warning. Populated
    ///     even when the object declares no abilities at all, which is the case the effect is most
    ///     likely to be a genuine mistake in.
    /// </remarks>
    public IReadOnlyList<(string Ability, IReadOnlyList<string> ProxyNames)> Unbound { get; }

    /// <summary>The proxies bound to one ability type, in the order they were seen.</summary>
    public IReadOnlyList<string> For(string abilityType)
    {
        return _byAbility.TryGetValue(abilityType, out var names) ? names : [];
    }
}
