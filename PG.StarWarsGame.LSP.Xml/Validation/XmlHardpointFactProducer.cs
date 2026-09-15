// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <inheritdoc />
public sealed class XmlHardpointFactProducer(ISchemaProvider schema, IVariantTagSource tagSource)
    : IXmlHardpointFactProducer
{
    private const string HardpointTypeName = "HardPoint";
    private const string HardpointsTag = "HardPoints";
    private const string ModelToAttachTag = "Model_To_Attach";
    private const string IsTurretTag = "Is_Turret";
    private const string DestroyableTag = "Is_Destroyable";
    private const string CollisionMeshTag = "Collision_Mesh";
    private const string SpecialAbilityNameTag = "Special_Ability_Name";

    // Bone-tag classification and cross-file attachment/model resolution are shared with the bone-model
    // inlay hint through HardpointBoneModelResolver, so the two can never disagree on a bone's target.

    public IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index)
    {
        if (!index.Documents.TryGetValue(documentUri, out var docIndex))
            return [];

        // No model was indexed at all - the game's models were never catalogued, or this workspace has
        // none. That is a setup condition, not a defect in any particular model, and reporting it per
        // bone reference buries the document (3806 diagnostics on vanilla Hardpoints.xml when measured).
        // Without bone lists nothing here is decidable, so produce nothing.
        if (index.ModelBones.IsEmpty)
            return [];

        var pass = new Pass(document, index, documentUri, schema, tagSource);

        foreach (var symbol in docIndex.Symbols)
        {
            if (!pass.NodesById.TryGetValue(symbol.Id, out var node)) continue;

            if (string.Equals(symbol.TypeName, HardpointTypeName, StringComparison.OrdinalIgnoreCase))
                CheckHardpoint(symbol.Id, node, pass);
            else
                CheckAttachingObject(symbol.Id, node, pass);
        }

        // One diagnostic per unreadable model rather than per bone pointing at it.
        foreach (var (key, info) in pass.Unavailable)
            pass.Facts.Add(new HardpointModelBonesUnavailableFact(documentUri,
                info.Line, info.Column, info.Length, key.Model, key.Owner, info.Count));

        return pass.Facts;
    }

    // ── direction 1: a hardpoint file is open ────────────────────────────────

    private static void CheckHardpoint(string hardpointId, HtmlNode hardpointNode, Pass pass)
    {
        // Before the bone checks, and deliberately before their early return: a hardpoint with no
        // bone tags at all can still be a destroyable one with no collision mesh.
        if (EngineBoolean.IsTrue(SingleValue(hardpointNode, DestroyableTag))
            && SingleValue(hardpointNode, CollisionMeshTag) is null)
            pass.Facts.Add(new HardpointUnhittableFact(pass.DocumentUri,
                XmlUtility.GetLine(hardpointNode),
                XmlUtility.GetTagBracketColumn(hardpointNode),
                XmlUtility.GetOpeningTagLength(hardpointNode),
                hardpointId, null));

        var bones = CollectBoneTags(hardpointNode, pass);
        if (bones.Count == 0) return;

        var isTurret = IsTurret(hardpointNode);
        var turretModel = SingleValue(hardpointNode, ModelToAttachTag);

        // Turret-side bones resolve against the hardpoint's own attached model, so they can be
        // checked without knowing anything about who attaches it.
        foreach (var bone in bones.Where(b => TargetsTurretModel(b.Tag, isTurret)))
            CheckBoneAgainstModels(bone, [turretModel], hardpointId, hardpointId, pass);

        // Parent-side bones and the special ability both need the attaching objects. A Collision_Mesh
        // that lives on the attached weapon model is already satisfied (hull UNION Model_To_Attach), so
        // drop it before the per-hull check rather than flag it against every hull attaching it.
        // Each surviving bone carries the attached model it was ALSO checked against, so the report
        // can say both were examined - and where that model cannot be read, the union cannot be
        // ruled out and the bone is dropped instead of blamed on the hull.
        var parentBones = new List<(BoneReference Bone, string? CheckedAttached)>();
        foreach (var bone in bones.Where(b => !TargetsTurretModel(b.Tag, isTurret)))
        {
            string? checkedAttached = null;
            if (HardpointBoneModelResolver.MayResolveAgainstAttachedModel(bone.Tag)
                && !string.IsNullOrEmpty(turretModel))
            {
                if (HardpointBoneModelResolver.ModelHasBone(pass.Index, turretModel, bone.Value))
                    continue;
                if (!HardpointBoneModelResolver.ModelBonesKnown(pass.Index, turretModel))
                    continue;

                checkedAttached = turretModel;
            }

            parentBones.Add((bone, checkedAttached));
        }
        var abilityNode = hardpointNode.ChildNodes.LastOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            n.Name.Equals(SpecialAbilityNameTag, StringComparison.OrdinalIgnoreCase));
        var ability = abilityNode?.InnerText.Trim();
        if (parentBones.Count == 0 && string.IsNullOrEmpty(ability)) return;

        foreach (var owner in pass.FindAttachingObjects(hardpointId))
        {
            if (!string.IsNullOrEmpty(ability) && abilityNode is not null)
                CheckSpecialAbility(hardpointId, ability, owner.Id,
                    new Position(XmlUtility.GetLine(abilityNode),
                        XmlUtility.GetOpeningTagStartColumn(abilityNode), abilityNode.Name.Length),
                    pass);

            var models = pass.DeclaredModels(owner.Id);
            if (models.Count == 0) continue;

            // Anchored on the tag's VALUE (CollectBoneTags), so a quick fix replacing the range
            // replaces exactly the name that is wrong.
            foreach (var (bone, checkedAttached) in parentBones)
                CheckBoneAgainstModels(bone, models, hardpointId, owner.Id, pass, checkedAttached,
                    anchoredOnValue: true);
        }
    }

    /// <summary>
    ///     The engine enables <c>Special_Ability_Name</c> on the object attaching the hardpoint, so the
    ///     ability has to exist on that object - not merely somewhere in the workspace. Abilities are
    ///     indexed owner-scoped under whichever object declares them, and a variant inherits its base's
    ///     abilities without re-declaring them, so the whole variant chain counts as "this object".
    /// </summary>
    private static void CheckSpecialAbility(string hardpointId, string ability, string ownerId,
        Position position, Pass pass)
    {
        if (pass.OwnerHasAbility(ownerId, ability)) return;

        pass.Facts.Add(new HardpointAbilityNotOnOwnerFact(pass.DocumentUri,
            position.Line, position.Column, position.Length,
            hardpointId, ability, ownerId,
            pass.Index.ResolveOwnerAgnostic(ability) is not null));
    }

    // ── direction 2: a file attaching hardpoints is open ──────────────────────

    private static void CheckAttachingObject(string ownerId, HtmlNode objectNode, Pass pass)
    {
        var hardpointsNode = objectNode.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            n.Name.Equals(HardpointsTag, StringComparison.OrdinalIgnoreCase));
        if (hardpointsNode is null) return;

        // Damage reaches a hardpoint through its collision mesh and the lookup returns the FIRST
        // match, so two destroyable hardpoints claiming one mesh leave the later one unreachable.
        // Per object, because that is the scope the lookup runs in - two units may reuse a mesh name
        // freely.
        var meshClaimedBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var models = pass.DeclaredModels(ownerId);

        foreach (var (hardpointId, offset) in XmlUtility.SplitListWithOffsets(hardpointsNode.InnerText))
        {
            var hardpointTags = pass.TagSource.TryGetTags(hardpointId);
            if (hardpointTags is null) continue; // unresolved hardpoint: the reference pipeline owns that

            var position = pass.TokenPosition(hardpointsNode, offset, hardpointId);

            // Which mesh each hardpoint claims is a question about the LIST, so it is asked before
            // the model gate below: an object with no model declared still routes damage by mesh.
            CheckCollisionMeshClaim(hardpointId, hardpointTags, position, meshClaimedBy, pass);

            // Everything past here compares a bone against a model, so without one there is nothing
            // to check. Unchanged in effect from the early return this replaced.
            if (models.Count == 0) continue;

            var isTurret = IsTurretFromTags(hardpointTags);
            var attachedModel = hardpointTags.LastOrDefault(t =>
                t.TagName.Equals(ModelToAttachTag, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

            // The same ability check from this side: the squiggle lands on the hardpoint token in
            // this object's HardPoints list, which is where the pairing is declared.
            var ability = hardpointTags.LastOrDefault(t =>
                t.TagName.Equals(SpecialAbilityNameTag, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
            if (!string.IsNullOrEmpty(ability))
                CheckSpecialAbility(hardpointId, ability, ownerId, position, pass);

            foreach (var tag in hardpointTags)
            {
                if (!IsBoneTag(tag.TagName) || TargetsTurretModel(tag.TagName, isTurret)) continue;
                var bone = tag.Value.Trim();
                if (bone.Length == 0) continue;

                // Collision_Mesh on the attached weapon model is valid (hull UNION Model_To_Attach).
                string? checkedAttachedModel = null;
                if (HardpointBoneModelResolver.MayResolveAgainstAttachedModel(tag.TagName)
                    && !string.IsNullOrEmpty(attachedModel))
                {
                    if (HardpointBoneModelResolver.ModelHasBone(pass.Index, attachedModel, bone))
                        continue;

                    // Its bones are unreadable, so the union cannot be ruled out. Reporting the
                    // hull here would state as fact something nobody checked.
                    if (!HardpointBoneModelResolver.ModelBonesKnown(pass.Index, attachedModel))
                        continue;

                    checkedAttachedModel = attachedModel;
                }

                CheckBoneAgainstModels(new BoneReference(tag.TagName, bone, position),
                    models, hardpointId, ownerId, pass, checkedAttachedModel);
            }
        }
    }

    // ── shared ───────────────────────────────────────────────────────────────

    /// <param name="anchoredOnValue">
    ///     Whether the bone's position is the tag's own value - true from the hardpoint's file, false
    ///     from the attaching object's, where it is the hardpoint's id in the <c>HardPoints</c> list. A
    ///     suggestion is only ever attached in the first case; see
    ///     <see cref="HardpointBoneNotOnModelFact.SuggestedName" />.
    /// </param>
    private static void CheckBoneAgainstModels(BoneReference bone, IReadOnlyList<string?> models,
        string hardpointId, string ownerId, Pass pass, string? checkedAttachedModel = null,
        bool anchoredOnValue = false)
    {
        foreach (var model in models)
        {
            if (string.IsNullOrEmpty(model)) continue;

            // The bone catalog is keyed by bare filename; XML may spell the model in any case and
            // (rarely) with a path, so reduce to the same key before looking it up.
            if (!pass.Index.ModelBones.TryGetValue(ModelBoneKey.From(model), out var modelBones)
                || modelBones.Length == 0)
            {
                // Accumulate: one diagnostic per unreadable model, anchored at the first bone that
                // could not be checked, rather than one per bone.
                var key = (model, ownerId);
                pass.Unavailable[key] = pass.Unavailable.TryGetValue(key, out var seen)
                    ? seen with { Count = seen.Count + 1 }
                    : new UnavailableModel(bone.Position.Line, bone.Position.Column,
                        bone.Position.Length, 1);
                continue;
            }

            if (modelBones.Contains(bone.Value, StringComparer.OrdinalIgnoreCase)) continue;

            var suggestion = anchoredOnValue
                ? SuggestCollisionMesh(bone, modelBones, checkedAttachedModel, pass)
                : null;

            pass.Facts.Add(new HardpointBoneNotOnModelFact(pass.DocumentUri,
                bone.Position.Line, bone.Position.Column, bone.Position.Length,
                hardpointId, bone.Tag, bone.Value, model, ownerId, checkedAttachedModel, suggestion));
        }
    }

    /// <summary>
    ///     The one name the checked models carry that starts with a truncated <c>Collision_Mesh</c>, or
    ///     null.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The failure this is for is measured, not guessed: all eight Gargantuan hardpoints write
    ///         <c>..._COL</c> or <c>..._COLL</c> and their models carry <c>..._COLLISION</c>. So only
    ///         <c>Collision_Mesh</c>, only a name that EXTENDS what was written, and only when exactly
    ///         one does - a quick fix that picked between two candidates would be a guess dressed as a
    ///         fix.
    ///     </para>
    ///     <para>
    ///         Candidates are the hull being reported and the hardpoint's own model where that was also
    ///         checked, because the tag is valid on either. The model's spelling is returned verbatim,
    ///         since that is what the author should see written back.
    ///     </para>
    /// </remarks>
    private static string? SuggestCollisionMesh(BoneReference bone, ImmutableArray<string> hullNames,
        string? attachedModel, Pass pass)
    {
        if (!HardpointBoneModelResolver.MayResolveAgainstAttachedModel(bone.Tag)) return null;

        IEnumerable<string> names = hullNames;
        if (!string.IsNullOrEmpty(attachedModel)
            && pass.Index.ModelBones.TryGetValue(ModelBoneKey.From(attachedModel), out var attachedNames))
            names = names.Concat(attachedNames);

        var candidates = names
            .Where(n => n.Length > bone.Value.Length
                        && n.StartsWith(bone.Value, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static List<BoneReference> CollectBoneTags(HtmlNode hardpointNode, Pass pass)
    {
        var result = new List<BoneReference>();
        foreach (var child in hardpointNode.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            if (!IsBoneTag(child.Name)) continue;

            var value = child.InnerText.Trim();
            if (value.Length == 0) continue;

            // Anchor on the bone-name value, not the tag name: the squiggle belongs on the thing that
            // is wrong. Uses the document line index (HAP's per-node LinePosition is unreliable for some
            // nested elements, which produced invalid ranges the client silently dropped).
            var (line, column, length) = XmlUtility.GetValuePosition(child, pass.LineIndex);

            // OriginalName, not Name: HAP lower-cases Name, and the tag is quoted back in the message.
            result.Add(new BoneReference(child.OriginalName ?? child.Name, value,
                new Position(line, column, length)));
        }

        return result;
    }

    private static bool IsBoneTag(string tagName)
    {
        return HardpointBoneModelResolver.IsHardpointBoneTag(tagName);
    }

    private static bool TargetsTurretModel(string tagName, bool isTurret)
    {
        return HardpointBoneModelResolver.TargetsTurretModel(tagName, isTurret);
    }

    private static bool IsTurret(HtmlNode hardpointNode)
    {
        return HardpointBoneModelResolver.IsTurret(hardpointNode);
    }

    /// <summary>
    ///     Records which destroyable hardpoint claimed a collision mesh on this object, and reports
    ///     the later ones that can therefore never be hit.
    /// </summary>
    /// <remarks>
    ///     Only destroyable hardpoints compete: an indestructible one is never a damage target, so
    ///     it takes nothing away from the one that matters.
    /// </remarks>
    private static void CheckCollisionMeshClaim(string hardpointId, IReadOnlyList<VariantTag> tags,
        Position position, Dictionary<string, string> meshClaimedBy, Pass pass)
    {
        if (!EngineBoolean.IsTrue(LastTagValue(tags, DestroyableTag))) return;

        var mesh = LastTagValue(tags, CollisionMeshTag);
        if (string.IsNullOrEmpty(mesh)) return;

        if (meshClaimedBy.TryGetValue(mesh, out var first))
        {
            pass.Facts.Add(new HardpointUnhittableFact(pass.DocumentUri,
                position.Line, position.Column, position.Length, hardpointId, first));
            return;
        }

        meshClaimedBy[mesh] = hardpointId;
    }

    private static string? LastTagValue(IReadOnlyList<VariantTag> tags, string tagName)
    {
        return tags.LastOrDefault(t =>
            t.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
    }

    private static bool IsTurretFromTags(IReadOnlyList<VariantTag> tags)
    {
        return EngineBoolean.IsTrue(tags.LastOrDefault(t =>
            t.TagName.Equals(IsTurretTag, StringComparison.OrdinalIgnoreCase))?.Value);
    }

    private static string? SingleValue(HtmlNode objectNode, string tagName)
    {
        var node = objectNode.ChildNodes.LastOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            n.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        var value = node?.InnerText.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private readonly record struct Position(int Line, int Column, int Length);

    /// <summary>A model with no readable bone list, and how many bone references it left unchecked.</summary>
    private readonly record struct UnavailableModel(int Line, int Column, int Length, int Count);

    private readonly record struct BoneReference(string Tag, string Value, Position Position);

    /// <summary>
    ///     Per-document state for one <see cref="Produce" /> call. Exists to hold the work that is
    ///     otherwise repeated per symbol or per token: the object-node lookup, the resolved model list
    ///     of each owner, and the document's line index. A class rather than captured locals so it
    ///     copies only the fields it needs instead of keeping the enclosing scope alive.
    /// </summary>
    private sealed class Pass
    {
        private readonly ParsedXmlDocument _document;
        private readonly Dictionary<string, IReadOnlyList<string>> _chainByOwner =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly HardpointBoneModelResolver _models;
        private readonly EffectiveObjectResolver _resolver;

        public Pass(ParsedXmlDocument document, GameIndex index, string documentUri,
            ISchemaProvider schema, IVariantTagSource tagSource)
        {
            _document = document;
            Index = index;
            DocumentUri = documentUri;
            TagSource = tagSource;
            _resolver = new EffectiveObjectResolver(index, schema, tagSource);
            _models = new HardpointBoneModelResolver(index, schema, tagSource);
            NodesById = BuildNodeIndex(document.Html);
        }

        public GameIndex Index { get; }
        public string DocumentUri { get; }
        public IVariantTagSource TagSource { get; }
        public List<XmlFact> Facts { get; } = [];
        public Dictionary<(string Model, string Owner), UnavailableModel> Unavailable { get; } = [];

        /// <summary>
        ///     Every named object in the document, keyed by id. Built in one pass: resolving each
        ///     symbol with a fresh Descendants() scan made this quadratic (194 hardpoints over a
        ///     6461-line file re-walked the whole tree 194 times).
        /// </summary>
        public Dictionary<string, HtmlNode> NodesById { get; }

        /// <summary>
        ///     Models the object declares, resolved through variant inheritance. Delegated to the shared
        ///     <see cref="HardpointBoneModelResolver" /> so validation and the inlay hint agree.
        /// </summary>
        public IReadOnlyList<string?> DeclaredModels(string ownerId)
        {
            return _models.DeclaredModels(ownerId);
        }

        /// <summary>
        ///     Whether <paramref name="ownerId" /> has an ability of that name, counting the ones it
        ///     inherits. Abilities are indexed owner-scoped (<c>{ownerId}$Name</c>) under the object that
        ///     declares them, and a variant does not re-declare what it inherits, so every link of the
        ///     variant chain is a valid place for the declaration to live.
        /// </summary>
        public bool OwnerHasAbility(string ownerId, string abilityName)
        {
            foreach (var link in Chain(ownerId))
                if (Index.Resolve($"{link}{GameIndex.OwnerScopeSeparator}{abilityName}") is not null)
                    return true;

            return false;
        }

        /// <summary>The object's variant chain, itself first. Memoised alongside the model lookup.</summary>
        private IReadOnlyList<string> Chain(string ownerId)
        {
            if (_chainByOwner.TryGetValue(ownerId, out var cached)) return cached;

            var effective = _resolver.Resolve(ownerId);
            var chain = effective.Found && !effective.Cyclic
                ? effective.Chain
                : (IReadOnlyList<string>) [ownerId];

            return _chainByOwner[ownerId] = chain;
        }

        /// <summary>
        ///     Objects whose <c>HardPoints</c> list attaches <paramref name="hardpointId" />. Delegated to
        ///     the shared <see cref="HardpointBoneModelResolver" />.
        /// </summary>
        public IEnumerable<GameSymbol> FindAttachingObjects(string hardpointId)
        {
            return _models.FindAttachingObjects(hardpointId);
        }

        /// <summary>The document's memoised line index, for value/token range computation.</summary>
        public LineOffsetIndex LineIndex => _document.LineIndex;

        /// <summary>
        ///     Position of one token inside a list element. Uses the document's memoised line index -
        ///     building a fresh <see cref="LineOffsetIndex" /> here re-scanned the whole file per token.
        /// </summary>
        public Position TokenPosition(HtmlNode listNode, int offset, string token)
        {
            var (line, column) = _document.LineIndex.GetPosition(listNode.InnerStartIndex + offset);
            return new Position(line, column, token.Length);
        } 

        private static Dictionary<string, HtmlNode> BuildNodeIndex(HtmlDocument doc)
        {
            var map = new Dictionary<string, HtmlNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in doc.DocumentNode.Descendants())
            {
                if (node.NodeType != HtmlNodeType.Element) continue;

                var id = XmlUtility.GetNameAttributeValue(node);
                if (id is null) continue;

                // First definition wins, matching how the rest of the pipeline treats duplicate ids.
                map.TryAdd(id, node);
            }

            return map;
        }
    }
}
