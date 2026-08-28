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
///     <see cref="DiagnosticsContext.Index" />. Gates on <see cref="TargetKind" />; subclasses supply
///     the <see cref="ReferenceKind" />, the user-facing <see cref="AssetNoun" /> and the allowed
///     extensions (so a bare filename can be matched against full catalog paths of the right type).
/// </summary>
public abstract class AssetFileExistenceHandlerBase : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected abstract ReferenceKind TargetKind { get; }
    protected abstract string AssetNoun { get; }
    protected abstract IReadOnlyList<string> AllowedExtensions { get; }

    /// <summary>
    ///     Extensions the engine treats as ONE asset: a reference to any of them is satisfied by a
    ///     file with any other (textures: a .tga reference falls back to the .dds and vice versa;
    ///     the TGA wins at runtime when both exist). Empty = exact-extension matching only.
    /// </summary>
    protected virtual IReadOnlyList<string> InterchangeableExtensions => [];

    /// <summary>
    ///     Whether art packed into a mega texture satisfies this reference.
    /// </summary>
    /// <remarks>
    ///     Off by default, and deliberately narrow. A mega texture holds GUI art and nothing else,
    ///     so excusing a model, a sound or a map because a <c>.mtd</c> happens to name something
    ///     similar would turn a real missing-asset warning into silence.
    /// </remarks>
    protected virtual bool ResolvesFromMegaTexture => false;

    protected sealed override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ReferenceKind != TargetKind)
            return [];

        var value = fact.RawValue.Trim();
        if (string.IsNullOrEmpty(value))
            return [];

        var results = new List<XmlDiagnosticResult>();
        foreach (var se in Normalize(value))
        {
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
            results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"{AssetNoun} file '{se}' was not found in the game data or workspace asset files.{alsoChecked}"));
        }

        return results;
    }

    private static IEnumerable<string> Normalize(string raw)
    {
        return ListValueConstants.PrepareValueForSplit(raw)
            .Split(ListValueConstants.GetListSeparators(), StringSplitOptions.RemoveEmptyEntries);
    }
}