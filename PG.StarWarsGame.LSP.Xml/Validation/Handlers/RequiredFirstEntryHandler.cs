// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Named handler (ID: <c>required-first-entry</c>) for a list whose FIRST entry the engine
///     requires to be a particular name, because index 0 is the fallback it hands out when a lookup
///     misses.
/// </summary>
/// <remarks>
///     <para>
///         From the assert seam rather than from an engine message - these rules have no message at
///         all, which is why neither message harvest could see them. Both are assert-shaped, so the
///         expression is the condition that must HOLD (the polarity of an assert expression cannot
///         be read from its text - see <c>data/assert-harvest.md</c> in the decompile repo).
///     </para>
///     <para>
///         Additive rather than <c>replace</c>: the tag's own <c>NameReferenceList</c> handling still
///         has to run. This only adds the first-entry constraint on top of it.
///     </para>
/// </remarks>
public sealed class RequiredFirstEntryHandler : XmlDiagnosticsHandler<XmlTagValueFact>,
    IXmlNamedDiagnosticsHandler
{
    /// <summary>
    ///     Keyed on (owning type, tag), because the required name differs per list and is an ENGINE
    ///     fact read out of the binary - not something a schema author could author correctly. Same
    ///     reasoning as <see cref="EngineValueRepairs" />: the address that proves it belongs next to
    ///     it, and the schema only opts the tag in.
    /// </summary>
    private static readonly Dictionary<(string Owner, string Tag), string> RequiredFirst =
        new(FirstEntryKeyComparer.Instance)
        {
            // GameConstants.cpp:1170 - DamageTypeNames[ 0 ] == "Damage_Default"
            [("GameConstants", "Damage_Types")] = "Damage_Default",
            // GameConstants.cpp:1180 - ArmorTypeNames[ 0 ] == "Armor_Default"
            [("GameConstants", "Armor_Types")] = "Armor_Default"
        };

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.RequiredFirstListEntry;

    public string ValidationId => "required-first-entry";

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.OwningType is not { } owner) return [];
        if (!RequiredFirst.TryGetValue((owner, fact.Tag.Tag), out var required)) return [];

        var first = fact.RawValue.Split(',')[0].Trim();

        // An empty list has no first entry to be wrong; the list handler reports what is wrong here.
        if (first.Length == 0) return [];

        // The engine uppercases every name before hashing it, so casing is not a difference it can
        // see - and this check must not be stricter than the comparison it models.
        if (string.Equals(first, required, StringComparison.OrdinalIgnoreCase)) return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"<{fact.Tag.Tag}> must list '{required}' first - it is entry 0, which the engine "
                + $"hands out whenever a lookup misses, so putting '{first}' there silently "
                + "repoints every default that falls back to it")
        ];
    }

    /// <summary>Ordinal-ignore-case on both halves of the key, matching how tags resolve.</summary>
    private sealed class FirstEntryKeyComparer : IEqualityComparer<(string Owner, string Tag)>
    {
        public static readonly FirstEntryKeyComparer Instance = new();

        public bool Equals((string Owner, string Tag) x, (string Owner, string Tag) y)
        {
            return string.Equals(x.Owner, y.Owner, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.Tag, y.Tag, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Owner, string Tag) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Owner),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Tag));
        }
    }
}