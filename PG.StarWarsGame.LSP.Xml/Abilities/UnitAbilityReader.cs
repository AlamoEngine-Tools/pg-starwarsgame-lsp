// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Xml.Abilities;

/// <summary>
///     One <c>Unit_Ability</c>, with every child tag it declared.
/// </summary>
/// <remarks>
///     Every tag rather than a fixed set of fields, because the two readers want different ones -
///     the encyclopedia takes the GUI name and the icon override, the preview takes the bone, the
///     particle and the recharge - and a record naming only one caller's fields has to grow every
///     time the other learns something.
/// </remarks>
public sealed class UnitAbility
{
    private readonly IReadOnlyDictionary<string, List<string>> _repeated;
    private readonly IReadOnlyDictionary<string, string> _tags;

    internal UnitAbility(
        string type,
        IReadOnlyDictionary<string, string> tags,
        IReadOnlyDictionary<string, List<string>> repeated)
    {
        Type = type;
        _tags = tags;
        _repeated = repeated;
    }

    /// <summary>
    ///     The ability's <c>Type</c>, which is its identity - never empty for a <c>Unit_Ability</c> -
    ///     or a block's <c>Name</c>.
    /// </summary>
    public string Type { get; }

    /// <summary>One child tag's text, or null when it was not declared.</summary>
    public string? Tag(string name)
    {
        return _tags.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>
    ///     EVERY occurrence of a child tag, in document order.
    /// </summary>
    /// <remarks>
    ///     <c>Mod_Multiplier</c> repeats inside one ability - Commander_Akbar_Team's DEFEND carries
    ///     six - so the first-wins <see cref="Tag" /> would describe such an ability wrongly rather
    ///     than partly.
    /// </remarks>
    public IReadOnlyList<string> Tags(string name)
    {
        return _repeated.TryGetValue(name, out var values) ? values : [];
    }
}

/// <summary>
///     Reads the <c>Unit_Abilities_Data</c> sub-object list.
/// </summary>
/// <remarks>
///     <para>
///         From the tag's verbatim FRAGMENT rather than its value: this is a sub-object list, so the
///         children are the data and the value itself is only whitespace.
///     </para>
///     <para>
///         Parsed with HAP through <c>XmlUtility</c>, which is the house rule - no hand-rolled tag
///         scanning - and which lower-cases element names, hence the case-insensitive lookups.
///     </para>
/// </remarks>
public static class UnitAbilityReader
{
    private const string AbilityElement = "unit_ability";
    private const string TypeElement = "type";

    public static IReadOnlyList<UnitAbility> Read(string? fragment)
    {
        return ReadElements(fragment, AbilityElement)
            .Where(ability => ability.Type.Length > 0)
            .ToList();
    }

    /// <summary>
    ///     Every <paramref name="element" /> block in a sub-object list - the type's <c>Abilities</c>
    ///     list keeps its <c>Stealth_Ability</c> and <c>Force_Cloak_Ability</c> there - each with every
    ///     child tag it declared, identified by its <c>Name</c> attribute or, without one, by nothing.
    /// </summary>
    /// <remarks>
    ///     The one reading of a cloak block: the preview and the cloak diagnostics both reach it
    ///     through <see cref="Validation.UnitCloaks" />.
    /// </remarks>
    public static IReadOnlyList<UnitAbility> ReadBlocks(string? fragment, string element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return ReadElements(fragment, element);
    }

    private static List<UnitAbility> ReadElements(string? fragment, string element)
    {
        if (string.IsNullOrWhiteSpace(fragment))
            return [];

        var document = XmlUtility.CreateHtmlDocument(fragment);
        if (!XmlUtility.TryGetRootNode(document, out var root) || root is null)
            return [];

        var abilities = new List<UnitAbility>();

        foreach (var node in root.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element
                                 && string.Equals(n.Name, element,
                                     StringComparison.OrdinalIgnoreCase)))
        {
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var repeated = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var child in node.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var text = HtmlEntity.DeEntitize(child.InnerText).Trim();
                if (text.Length == 0)
                    continue;

                // First occurrence wins for the scalar view; every occurrence is kept alongside,
                // because some children genuinely repeat - `Mod_Multiplier` is a list, not a
                // mistake.
                tags.TryAdd(child.Name, text);

                if (!repeated.TryGetValue(child.Name, out var all))
                {
                    all = [];
                    repeated[child.Name] = all;
                }

                all.Add(text);
            }

            // A Unit_Ability's Type is its identity: an entry without one cannot be bound to a proxy,
            // drawn in a slot, or named in a row - `Read` drops it. A block elsewhere is named by its
            // Name attribute.
            var type = element.Equals(AbilityElement, StringComparison.OrdinalIgnoreCase)
                ? tags.GetValueOrDefault(TypeElement, string.Empty)
                : node.GetAttributeValue("Name", string.Empty).Trim();

            abilities.Add(new UnitAbility(type, tags, repeated));
        }

        return abilities;
    }
}