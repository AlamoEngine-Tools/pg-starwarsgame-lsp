// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Warns when a model resolves but a texture it names INSIDE itself does not.
/// </summary>
/// <remarks>
///     <para>
///         The gap this closes: every other asset-existence handler validates a reference written in
///         the document, because that is what a diagnostic can be anchored to. A model's own skins,
///         and a particle system's sprite sheets, are written in the binary - so nothing validated
///         them, and the only code that ever asked was the 3D preview, at draw time, in the webview.
///         A modder learned a texture was missing by opening the preview and noticing.
///     </para>
///     <para>
///         Reported against the tag that pulls the model in, since that is the only place in the
///         document the problem exists. The message therefore has to name BOTH: the model, because
///         the tag's own value is what the reader sees, and the texture, because that is the thing
///         to go and fix.
///     </para>
/// </remarks>
public sealed class ModelTextureExistenceHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    /// <summary>The extensions a texture may be found under. Same set the XML-facing handler uses.</summary>
    private static readonly string[] TextureExtensions = [".tga", ".dds"];

    /// <summary>Models, so a bare model name matches a catalog entry of the right kind.</summary>
    private static readonly string[] ModelExtensions = [".alo"];

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.ModelTextureExistence;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ReferenceKind != ReferenceKind.ModelFile || ctx.ModelTextures is null)
            return [];

        var value = fact.RawValue.Trim();
        if (string.IsNullOrEmpty(value))
            return [];

        var results = new List<XmlDiagnosticResult>();

        foreach (var model in Normalize(value))
        {
            // A model that does not resolve is ModelFileExistence's business. Following "this model
            // is missing" with "and so is every texture in it" buries the one useful line.
            if (!AssetFileLookup.Resolves(ctx.Index.AssetFiles, model, ModelExtensions, []))
                continue;

            // One entry per texture, not per sub-mesh: a model binds one skin across many of them,
            // and the reader has one thing to fix either way.
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var texture in ctx.ModelTextures.TexturesOf(model))
            {
                var name = texture.Trim();

                if (string.IsNullOrEmpty(name) || !reported.Add(name))
                    continue;

                if (AssetFileLookup.Resolves(
                        ctx.Index.AssetFiles, name, TextureExtensions, TextureExtensions))
                    continue;

                results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    $"Model '{model}' references texture '{name}', which was not found in the game "
                    + "data or workspace asset files. The reference is inside the model, so the fix "
                    + "is in the art rather than on this line."));
            }
        }

        return results;
    }

    private static IEnumerable<string> Normalize(string raw)
    {
        return ListValueConstants.PrepareValueForSplit(raw)
            .Split(ListValueConstants.GetListSeparators(), StringSplitOptions.RemoveEmptyEntries);
    }
}
