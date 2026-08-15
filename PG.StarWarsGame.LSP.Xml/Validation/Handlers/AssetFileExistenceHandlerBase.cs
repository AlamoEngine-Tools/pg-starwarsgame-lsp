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