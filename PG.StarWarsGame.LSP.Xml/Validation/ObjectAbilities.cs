// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     The ability TYPES a resolved object declares.
/// </summary>
/// <remarks>
///     <para>
///         Abilities are not a flat tag value: they live in a <c>Unit_Abilities_Data</c> sub-object
///         list, one <c>Unit_Ability</c> element each, and the thing worth matching is that entry's
///         <c>Type</c>. So this reads the effective tag's FRAGMENT - its verbatim outer XML - and
///         parses it, rather than testing whether the name appears somewhere in the inner text,
///         which would also match a name mentioned in some other element of the same block.
///     </para>
///     <para>
///         Lives in the Xml project rather than beside <see cref="ObjectBehaviors" /> in Core
///         because it needs a parser, and parsing goes through HAP via <c>XmlUtility</c> - never
///         hand-rolled tag scanning.
///     </para>
/// </remarks>
public static class ObjectAbilities
{
    private const string AbilitiesTag = "Unit_Abilities_Data";
    private const string AbilityElement = "Unit_Ability";
    private const string TypeElement = "Type";

    /// <summary>Whether the object declares an ability of the given type, case-insensitively.</summary>
    public static bool Has(EffectiveObject obj, string abilityType)
    {
        return TypesOf(obj).Contains(abilityType);
    }

    /// <summary>Every ability type on the object, from every ability block it effectively has.</summary>
    public static IReadOnlySet<string> TypesOf(EffectiveObject obj)
    {
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!obj.Found || obj.Tags.IsDefaultOrEmpty) return types;

        foreach (var tag in obj.Tags)
        {
            if (!string.Equals(tag.TagName, AbilitiesTag, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(tag.Fragment)) continue;

            var doc = XmlUtility.CreateHtmlDocument(tag.Fragment);
            foreach (var ability in doc.DocumentNode.Descendants()
                         .Where(n => n.Name.Equals(AbilityElement, StringComparison.OrdinalIgnoreCase)))
            {
                var type = ability.ChildNodes
                    .FirstOrDefault(c => c.Name.Equals(TypeElement, StringComparison.OrdinalIgnoreCase));
                var value = type?.InnerText.Trim();
                if (!string.IsNullOrEmpty(value)) types.Add(value);
            }
        }

        return types;
    }
}
