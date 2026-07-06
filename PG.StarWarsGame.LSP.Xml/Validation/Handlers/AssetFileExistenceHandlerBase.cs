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
            if (Exists(ctx.Index.AssetFiles, se))
                continue;

            var alternates = AlternateNames(se).ToList();
            if (alternates.Any(a => Exists(ctx.Index.AssetFiles, a)))
                continue;

            var alsoChecked = alternates.Count > 0
                ? $" Also checked {string.Join(", ", alternates.Select(a => $"'{a}'"))} (the game treats these formats interchangeably)."
                : string.Empty;
            results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"{AssetNoun} file '{se}' was not found in the game data or workspace asset files.{alsoChecked}"));
        }

        return results;
    }

    // The same name with each other interchangeable extension, when the value's own extension is
    // part of the interchangeable set.
    private IEnumerable<string> AlternateNames(string normalised)
    {
        var ext = Path.GetExtension(normalised);
        if (string.IsNullOrEmpty(ext) ||
            !InterchangeableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            yield break;

        foreach (var other in InterchangeableExtensions)
            if (!other.Equals(ext, StringComparison.OrdinalIgnoreCase))
                yield return normalised[..^ext.Length] + other;
    }

    private bool Exists(IAssetFileIndex index, string normalised)
    {
        // Exact relative-path match (e.g. "data/art/textures/foo.tga").
        if (index.Contains(normalised))
            return true;

        // Bare filename or partial path (e.g. "foo.tga"): match any catalog entry of the right
        // asset type whose path ends with "/<value>".
        var suffix = "/" + normalised;
        foreach (var ext in AllowedExtensions)
        foreach (var path in index.GetByExtension(ext))
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, normalised, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static IEnumerable<string> Normalize(string raw)
    {
        return ListValueConstants.PrepareValueForSplit(raw)
            .Split(ListValueConstants.GetListSeparators(), StringSplitOptions.RemoveEmptyEntries);
    }
}