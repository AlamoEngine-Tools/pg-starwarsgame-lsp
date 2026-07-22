// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.InlayHints;

/// <summary>
///     Prefixes a bone-name reference value with the <c>.alo</c> model it resolves to, so a bone reads as
///     <c>my_model.alo::Weap_Laser_FP00</c> inline. Bone names carry no model in the source, so the origin
///     is otherwise invisible - this surfaces it without navigating anywhere (go-to on bones is deliberately
///     not wired up).
///     <para>
///         Which model a bone targets is context-dependent. Inside a <c>HardPoint</c> it is resolved with
///         the shared <see cref="HardpointBoneModelResolver" /> - the same role split and cross-file
///         cumulative mounting logic the hardpoint validator uses - so an <c>Attachment_Bone</c> shows the
///         mounting hull(s), a <c>Turret_Bone_Name</c> shows the hardpoint's own attached model, and a
///         fire bone follows <c>Is_Turret</c>. Everywhere else (a bone tag on a GameObject that declares
///         its own model) it falls back to the sibling-model <see cref="BoneModelScopeResolver" /> walk,
///         matching bone completion. In both cases only the model(s) that actually expose the bone (in
///         <see cref="GameIndex.ModelBones" />, the bones-union-mesh-names catalog) are shown; when the
///         value resolves nowhere the hint stays silent - a genuine typo is the bone-not-on-model
///         diagnostic's job. A bone mounted cumulatively on several hulls lists them, capped.
///     </para>
/// </summary>
internal sealed class BoneModelInlayHintProvider : IXmlInlayHintProvider
{
    // A cumulative hardpoint can mount on many hulls; list a couple and summarise the rest so the label
    // stays readable.
    private const int MaxModelsShown = 2;

    // One resolver per index instance carries the mounting/model memoisation across every bone tag in a
    // request; keyed on GameIndex so a re-index drops it rather than serving stale mounts.
    private static readonly ConditionalWeakTable<GameIndex, HardpointBoneModelResolver> Resolvers = new();

    private readonly IVariantTagSource _tagSource;

    public BoneModelInlayHintProvider(IVariantTagSource tagSource)
    {
        _tagSource = tagSource;
    }

    public IEnumerable<InlayHint> Handle(InlayHintContext ctx)
    {
        if (ctx.TagDef.ReferenceKind != ReferenceKind.BoneName)
            return [];

        var value = ctx.Node.InnerText.Trim();
        if (value.Length == 0)
            return [];

        var candidateModels = CandidateModelKeys(ctx);
        if (candidateModels.Count == 0)
            return [];

        // Keep only the models that actually expose the bone, in a stable order for a deterministic label.
        var owning = candidateModels
            .Where(key => ctx.Index.ModelBones.TryGetValue(key, out var bones)
                          && bones.Contains(value, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (owning.Count == 0)
            return [];

        return
        [
            new InlayHint
            {
                Position = ValueStart(ctx),
                Label = ((StringOrInlayHintLabelParts?)$"{FormatModels(owning)}::")!,
                Kind = InlayHintKind.Type
            }
        ];
    }

    // Role-aware for hardpoint bones (mounting hull vs the hardpoint's own model, across files), sibling
    // model walk otherwise - the same dispatch bone completion uses, so hint and completion agree.
    private IReadOnlyList<string> CandidateModelKeys(InlayHintContext ctx)
    {
        return Resolvers.GetValue(ctx.Index, idx => new HardpointBoneModelResolver(idx, ctx.Schema, _tagSource))
            .ResolveModelKeysForBoneTag(ctx.Node);
    }

    private static string FormatModels(IReadOnlyList<string> models)
    {
        if (models.Count <= MaxModelsShown)
            return string.Join(", ", models);
        return string.Join(", ", models.Take(MaxModelsShown)) + $", +{models.Count - MaxModelsShown}";
    }

    // Anchor at the exact source start of the trimmed value when a line index is available (the reliable
    // InnerStartIndex-based path); otherwise fall back to just past the opening tag on the tag's line.
    private static Position ValueStart(InlayHintContext ctx)
    {
        if (ctx.LineIndex is not null)
        {
            var (line, column, _) = XmlUtility.GetValuePosition(ctx.Node, ctx.LineIndex);
            return new Position(line, column);
        }

        return new Position(ctx.Line, XmlUtility.GetOpeningTagEndColumn(ctx.Node) + 1);
    }
}
