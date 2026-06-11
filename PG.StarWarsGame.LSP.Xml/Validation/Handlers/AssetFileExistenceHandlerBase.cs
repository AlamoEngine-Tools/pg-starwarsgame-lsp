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

    protected sealed override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ReferenceKind != TargetKind)
            return [];

        var value = fact.RawValue.Trim();
        if (string.IsNullOrEmpty(value))
            return [];

        var normalised = Normalize(value);
        return (from se in normalised
                where !Exists(ctx.Index.AssetFiles, se)
                select new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"{AssetNoun} file '{se}' was not found in the game data or workspace asset files."))
            .ToList();
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