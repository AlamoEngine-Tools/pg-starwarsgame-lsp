// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;
using LspCodeLens = OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeLens;

/// <summary>
///     Emits a "preview unit" lens on every object that declares a tactical model, opening the model
///     preview for that object. Gated on <c>features.tools.modelPreview</c>.
/// </summary>
/// <remarks>
///     <para>
///         It carries the OBJECT id, not the model file, and that is the whole point. Opening the
///         <c>.alo</c> instead - by double-clicking it, or through <em>Preview Model</em> - builds a
///         scene of kind <c>Model</c>, which never reads any XML and so has no hardpoints, no
///         weapons, no projectiles and no reticles. Its Gameplay lens is permanently empty. Only an
///         <c>objectId</c> request runs the assembly, and before this lens the only way to make one
///         was the command palette plus typing the object's name from memory.
///     </para>
///     <para>
///         One title for every case, which the user chose: a land unit, a space unit, a structure
///         and a prop all open the same viewer in the same state, so a title that changed with the
///         subject would give a reader something new to interpret on every line - and it would sit
///         next to <c>preview encyclopedia</c>, which is already one fixed phrase.
///     </para>
/// </remarks>
internal sealed class PreviewUnitCodeLensProvider : IXmlCodeLensProvider
{
    public const string PreviewUnitCommand = "aet-eaw-edit.lsp.previewAssembledUnit";

    private readonly ILspConfigurationProvider _config;
    private readonly IVariantTagSource _tagSource;

    public PreviewUnitCodeLensProvider(IVariantTagSource tagSource, ILspConfigurationProvider config)
    {
        _tagSource = tagSource;
        _config = config;
    }

    public LspCodeLens? Handle(CodeLensSymbolContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (!_config.Current.Features.Tools.ModelPreview)
            return null;

        // Assets and localisation keys reach the same registry, and only an XML object can be
        // assembled into a scene.
        if (ctx.Symbol.Kind != GameSymbolKind.XmlObject)
            return null;

        if (!DeclaresTacticalModel(ctx.Index, ctx.Symbol))
            return null;

        return new LspCodeLens
        {
            Range = new LspRange(new Position(ctx.Origin.Line, 0), new Position(ctx.Origin.Line, 0)),
            Command = new Command
            {
                Title = "preview unit",
                Name = PreviewUnitCommand,
                Arguments = JArray.FromObject(new object[] { ctx.Symbol.Id })
            }
        };
    }

    /// <summary>
    ///     Whether this object, or anything it varies, names a tactical model.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A tag scan up the variant chain rather than a full
    ///         <c>EffectiveObjectResolver.Resolve</c>: a code lens request runs over every symbol in
    ///         the document and fires again on every edit, so the per-symbol cost has to stay at the
    ///         level the encyclopedia lens already pays. The question here is only "is there a model
    ///         at all", which needs no schema merge to answer.
    ///     </para>
    ///     <para>
    ///         The chain matters. A variant typically restates only what it changes, so most objects
    ///         in a mod inherit their model; gating on restating it would leave the lens off exactly
    ///         the objects whose assembled result is worth looking at.
    ///     </para>
    /// </remarks>
    private bool DeclaresTacticalModel(GameIndex index, GameSymbol symbol)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = symbol;

        // `visited` also terminates a cycle. A mod can write one, and the preview reports it as a
        // problem rather than refusing - but a lens that hung the editor would be unforgivable.
        while (current is not null && visited.Add(current.Id))
        {
            if (LayerDeclaresModel(index, current))
                return true;

            current = string.IsNullOrEmpty(current.VariantBaseId)
                ? null
                : index.Resolve(current.VariantBaseId);
        }

        return false;
    }

    private bool LayerDeclaresModel(GameIndex index, GameSymbol symbol)
    {
        // The workspace SHADOWS the baseline rather than adding to it - a mod that redefines an
        // object replaces its tags, so a baseline model must not answer for a workspace object that
        // dropped it.
        var workspace = _tagSource.TryGetTags(symbol.Id);
        if (workspace is not null)
            return workspace.Any(tag => IsModelTag(tag.TagName));

        return index.Baseline.ObjectTags.TryGetValue(symbol.Id, out var baseline)
               && baseline.Any(tag => IsModelTag(tag.TagName));
    }

    private static bool IsModelTag(string tagName)
    {
        return HardpointBoneModelResolver.MountingObjectModelTags
            .Contains(tagName, StringComparer.OrdinalIgnoreCase);
    }
}
