// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     The GameObject tags that feed the in-game encyclopedia popup, and the chain walk that says
///     whether an object has a popup at all.
/// </summary>
/// <remarks>
///     In Core because both sides of the feature need them and neither owns the other: the Xml code
///     lens decides whether to offer the preview, and the Server handler resolves it.
/// </remarks>
public static class EncyclopediaTags
{
    /// <summary>Localisation key for the popup's name line.</summary>
    public const string TextId = "Text_ID";

    /// <summary>Localisation key for the class line, under the name.</summary>
    public const string UnitClass = "Encyclopedia_Unit_Class";

    /// <summary>Localisation key list; the engine draws one body line per key.</summary>
    public const string Body = "Encyclopedia_Text";

    /// <summary>Replaces <see cref="Body" /> in multiplayer, but only when it is non-empty.</summary>
    public const string MultiplayerBody = "MP_Encyclopedia_Text";

    /// <summary>
    ///     Population cost, drawn as the blip at the popup's top-left. When absent the game shifts
    ///     the header left into the space the blip would have taken, so its presence is a layout
    ///     input and not only a value.
    /// </summary>
    public const string PopulationValue = "Population_Value";

    /// <summary>GameObject references whose own <see cref="TextId" /> supplies the label.</summary>
    public const string GoodAgainst = "Encyclopedia_Good_Against";

    /// <inheritdoc cref="GoodAgainst" />
    public const string VulnerableTo = "Encyclopedia_Vulnerable_To";

    /// <summary>Sub-object list of Unit_Ability entries; GUI-activated ones get a popup slot.</summary>
    public const string UnitAbilities = "Unit_Abilities_Data";

    /// <summary>
    ///     Whether <paramref name="symbol" /> would show a popup - that is, whether it or anything in
    ///     its <c>Variant_Of_Existing_Type</c> chain defines body text.
    /// </summary>
    /// <remarks>
    ///     Checks for the tag's presence only, without merging the chain: the caller just needs to
    ///     know whether there is anything to preview, and a full effective-object resolve per symbol
    ///     is far too much work for a code lens that runs over every object in a document.
    ///     Workspace tags shadow baseline tags for an id, matching <see cref="EffectiveObjectResolver" />.
    /// </remarks>
    public static bool DefinesBody(GameIndex index, IVariantTagSource workspaceSource, GameSymbol symbol)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var current = symbol;
        while (current is not null && visited.Add(current.Id))
        {
            if (LayerDefinesBody(index, workspaceSource, current))
                return true;

            current = string.IsNullOrEmpty(current.VariantBaseId)
                ? null
                : index.Resolve(current.VariantBaseId);
        }

        return false;
    }

    private static bool LayerDefinesBody(
        GameIndex index, IVariantTagSource workspaceSource, GameSymbol symbol)
    {
        var workspace = workspaceSource.TryGetTags(symbol.Id);
        if (workspace is not null)
            return workspace.Any(t => IsBodyTag(t.TagName));

        return index.Baseline.ObjectTags.TryGetValue(symbol.Id, out var baselineTags)
               && baselineTags.Any(t => IsBodyTag(t.TagName));
    }

    private static bool IsBodyTag(string tagName)
    {
        return string.Equals(tagName, Body, StringComparison.OrdinalIgnoreCase)
               || string.Equals(tagName, MultiplayerBody, StringComparison.OrdinalIgnoreCase);
    }
}
