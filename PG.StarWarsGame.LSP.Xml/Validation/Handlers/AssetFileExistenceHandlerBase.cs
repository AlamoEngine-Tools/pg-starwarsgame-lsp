// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Base for handlers that warn when an asset-file reference (texture, model, audio, map) does
///     not resolve against the merged <see cref="IAssetFileIndex" /> on the
///     <see cref="DiagnosticsContext.Index" />. Gates on <see cref="TargetKind" />; what satisfies a
///     name of that kind, and what a missing one costs, comes from <see cref="AssetKindRules" /> -
///     the same rules an asset item of a tuple is checked by.
/// </summary>
public abstract class AssetFileExistenceHandlerBase : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected abstract ReferenceKind TargetKind { get; }

    private AssetKindRule Rule => AssetKindRules.For(TargetKind)
                                  ?? throw new InvalidOperationException($"{TargetKind} names no asset kind.");

    private string AssetNoun => Rule.Noun;
    private IReadOnlyList<string> AllowedExtensions => Rule.AllowedExtensions;
    private IReadOnlyList<string> InterchangeableExtensions => Rule.InterchangeableExtensions;
    private bool ResolvesFromMegaTexture => Rule.ResolvesFromMegaTexture;
    private XmlDiagnosticSeverity MissingSeverity => Rule.MissingSeverity;

    protected sealed override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ReferenceKind != TargetKind)
            return [];

        if (string.IsNullOrWhiteSpace(fact.RawValue))
            return [];

        var isList = IsListValued(fact.Tag.ValueType);
        var results = new List<XmlDiagnosticResult>();
        foreach (var (se, offset, length) in Normalize(fact.RawValue, fact.Tag.ValueType))
        {
            // A slot filler, not a name. Only inside a list, where slots are what the value IS.
            if (isList && se.Equals(UnusedSlotMarker, StringComparison.OrdinalIgnoreCase))
                continue;

            if (AssetFileLookup.Resolves(
                    ctx.Index.AssetFiles, se, AllowedExtensions, InterchangeableExtensions))
                continue;

            // Only now, with the file lookup already failed, is it worth asking the mega textures.
            // Unresolved references are the rare case, so the cost of opening a .mtd is paid on the
            // path that was about to warn rather than on every reference in the document.
            if (ResolvesFromMegaTexture && ctx.IconNames?.Contains(se) == true)
                continue;

            var alternates = AssetFileLookup.AlternateNames(se, InterchangeableExtensions).ToList();
            var alsoChecked = alternates.Count > 0
                ? $" Also checked {string.Join(", ", alternates.Select(a => $"'{a}'"))} (the game treats these formats interchangeably)."
                : string.Empty;
            var result = new XmlDiagnosticResult(MissingSeverity,
                $"{AssetNoun} file '{se}' was not found in the game data or workspace asset files.{alsoChecked}");

            // Each name in a list carries its own squiggle. A single value already covers exactly
            // the span the fact does, so it is left alone.
            if (isList)
            {
                var (line, column) =
                    XmlUtility.AdvancePosition(fact.Line, fact.Column, fact.RawValue, offset);
                result = result with
                {
                    OverrideLine = line, OverrideColumn = column, OverrideLength = length
                };
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    ///     Vanilla's "this slot carries no art" filler. <c>&lt;Icon_Alternate_Texture_Name&gt;</c>
    ///     is indexed BY ABILITY SLOT, so a slot with no icon still needs a word in it, and vanilla
    ///     writes this one - 23 occurrences in <c>commandbarcomponents.xml</c>, in both corpora. The
    ///     engine has no such string in it and simply fails to load the texture, which is what the
    ///     author meant to happen.
    /// </summary>
    private const string UnusedSlotMarker = "NOT_USED";

    private static bool IsListValued(XmlValueType valueType)
    {
        return valueType is XmlValueType.NameReferenceList or XmlValueType.TypeReferenceList
            or XmlValueType.GameObjectTypeReferenceList;
    }

    /// <summary>
    ///     The names in this value with where each one sits inside <paramref name="raw" />: several
    ///     for a list-typed tag, and exactly ONE otherwise - spaces included.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Splitting every asset value on space is what made a model called
    ///         <c>CIS_Vazus Mandrake.alo</c> read as two missing files (issue #124). A space is not a
    ///         separator in an asset name: <c>Mt_commandbar.mtd</c> ships
    ///         <c>I_BUTTON_EV_MDU_GRENADE MORTAR.TGA</c>, and four vanilla <c>Icon_Name</c> values
    ///         carry one. The tag's own type is what says whether several names are allowed, so that
    ///         is what decides - asset tags are only ever <c>NameReference</c> (84),
    ///         <c>NameReferenceList</c> (16) or <c>TypeReferenceList</c> (6).
    ///     </para>
    ///     <para>
    ///         The offsets are into the UNTRIMMED raw value, so they stay valid against the fact's
    ///         own position even where the list runs across several lines. Tokenising the raw value
    ///         rather than the whitespace-collapsed one is what makes that possible; the tokens
    ///         themselves are identical either way, because collapsing runs of whitespace and then
    ///         splitting on space selects the same words.
    ///     </para>
    /// </remarks>
    private static IEnumerable<(string Name, int Offset, int Length)> Normalize(
        string raw, XmlValueType valueType)
    {
        if (!IsListValued(valueType))
        {
            var prepared = ListValueConstants.PrepareValueForSplit(raw);
            if (prepared.Length == 0)
                yield break;

            var start = 0;
            while (start < raw.Length && char.IsWhiteSpace(raw[start])) start++;
            var end = raw.Length;
            while (end > start && char.IsWhiteSpace(raw[end - 1])) end--;
            yield return (prepared, start, end - start);
            yield break;
        }

        var separators = ListValueConstants.GetListSeparators();
        var i = 0;
        while (i < raw.Length)
        {
            while (i < raw.Length && IsSeparator(raw[i])) i++;
            if (i >= raw.Length) yield break;

            var tokenStart = i;
            while (i < raw.Length && !IsSeparator(raw[i])) i++;
            yield return (raw[tokenStart..i], tokenStart, i - tokenStart);
        }

        bool IsSeparator(char c)
        {
            return char.IsWhiteSpace(c) || Array.IndexOf(separators, c) >= 0;
        }
    }
}