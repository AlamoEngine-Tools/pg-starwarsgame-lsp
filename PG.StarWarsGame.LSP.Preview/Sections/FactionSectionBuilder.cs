// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Preview.Sections;

/// <summary>
///     The factions a scene can be tinted with: every <c>Faction</c> object of the tree in XML
///     order, with its three colours, read once per index snapshot (#314).
/// </summary>
public sealed class FactionSectionBuilder(ISchemaProvider schema, IVariantTagSource tagSource)
{
    private const string FactionTypeName = "Faction";

    private const string FactionsKey = "previewFactions";

    /// <summary>
    ///     Every indexed faction's colours.
    /// </summary>
    /// <remarks>
    ///     All of them travel, not just the object's own. The preview lets an author try a unit in any
    ///     faction's colours and compare two side by side, and shipping the whole set makes that a
    ///     local switch rather than a round trip each time.
    /// </remarks>
    /// <summary>The factions of a snapshot, resolved once per snapshot and shared by every scene built from it.</summary>
    public List<PreviewFaction> Factions(GameIndex index)
    {
        return index.Derived(FactionsKey, i => BuildFactions(i, new EffectiveObjectResolver(i, schema, tagSource)));
    }

    private List<PreviewFaction> BuildFactions(GameIndex index, EffectiveObjectResolver resolver)
    {
        var factions = new List<PreviewFaction>();

        // Workspace first, then the baseline: a mod's own faction shadows a shipped one of the same
        // id, and DistinctBy keeps whichever it saw first.
        //
        // Each group in DECLARATION order, which is the order the palette is drawn in. It used to
        // come straight off the dictionaries, which have no order to give - so the swatches came
        // back arranged differently on every reload and the reader reached for the position they
        // used last time and got someone else's colour. Sorted within the groups rather than across
        // them, so the shadowing above still means what it says.
        var candidates = InXmlOrder(index.WorkspaceDefinitions.SelectMany(kv => kv.Value))
            .Concat(InXmlOrder(index.ShippedSymbols.Values));

        // ReSharper disable once LoopCanBeConvertedToQuery - each faction is resolved and the
        // unresolvable skipped; a query would hide the resolve.
        foreach (var symbol in candidates
                     .Where(s => string.Equals(s.TypeName, FactionTypeName, StringComparison.OrdinalIgnoreCase))
                     .DistinctBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
        {
            // By the symbol, not its id: the shipped game also names an indigenous unit Sarlacc.
            var effective = resolver.Resolve(symbol);
            if (!effective.Found)
                continue;

            factions.Add(new PreviewFaction(
                effective.ObjectId,
                PreviewTagReader.Colour(effective, "Color"),
                PreviewTagReader.Colour(effective, "No_Colorization_Color"),
                PreviewTagReader.Colour(effective, "Display_Font_Color")));
        }

        return factions;
    }

    /// <summary>
    ///     Symbols in the order their file declares them: by file, then by position within it.
    /// </summary>
    /// <remarks>
    ///     A symbol with no file to point at - one out of a MEG archive, or one whose origin was
    ///     never resolved - sorts after the rest and then by id, so it has SOME fixed place instead
    ///     of wherever the dictionary happened to put it. Being last is the honest answer: nothing
    ///     is known about where it was written.
    /// </remarks>
    private static IEnumerable<GameSymbol> InXmlOrder(IEnumerable<GameSymbol> symbols)
    {
        return symbols
            .OrderBy(s => s.Origin is FileOrigin ? 0 : 1)
            .ThenBy(s => (s.Origin as FileOrigin)?.Uri ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => (s.Origin as FileOrigin)?.Line ?? 0)
            .ThenBy(s => (s.Origin as FileOrigin)?.Column ?? 0)
            .ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase);
    }
}