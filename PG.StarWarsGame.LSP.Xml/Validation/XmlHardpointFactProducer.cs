// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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
    private const string SpecialAbilityNameTag = "Special_Ability_Name";

    // Bones that always live on the model of the object mounting the hardpoint.
    private static readonly string[] ParentModelBoneTags =
        ["Attachment_Bone", "Collision_Mesh", "Damage_Decal", "Damage_Particles", "Engine_Particles"];

    // Bones that always live on the hardpoint's own attached model. Turret_* tags only apply to a
    // turret at all; Turret_Bone_Name and Barrel_Bone_Name are where rotation and elevation are
    // applied, so they must exist on the turret model itself.
    private static readonly string[] TurretModelBoneTags = ["Turret_Bone_Name", "Barrel_Bone_Name"];

    // Fire bones switch sides: on a turret they belong to the attached turret model, otherwise to
    // the mounting object's model.
    private static readonly string[] FireBoneTags = ["Fire_Bone_A", "Fire_Bone_B"];

    private static readonly char[] ListSeparators = [',', ' ', '\t', '\r', '\n'];

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
                CheckMountingObject(symbol.Id, node, pass);
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
        var bones = CollectBoneTags(hardpointNode);
        if (bones.Count == 0) return;

        var isTurret = IsTurret(hardpointNode);
        var turretModel = SingleValue(hardpointNode, ModelToAttachTag);

        // Turret-side bones resolve against the hardpoint's own attached model, so they can be
        // checked without knowing anything about who mounts it.
        foreach (var bone in bones.Where(b => TargetsTurretModel(b.Tag, isTurret)))
            CheckBoneAgainstModels(bone, [turretModel], hardpointId, hardpointId, pass);

        // Parent-side bones and the special ability both need the mounting objects.
        var parentBones = bones.Where(b => !TargetsTurretModel(b.Tag, isTurret)).ToList();
        var abilityNode = hardpointNode.ChildNodes.LastOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            n.Name.Equals(SpecialAbilityNameTag, StringComparison.OrdinalIgnoreCase));
        var ability = abilityNode?.InnerText.Trim();
        if (parentBones.Count == 0 && string.IsNullOrEmpty(ability)) return;

        foreach (var owner in pass.FindMountingObjects(hardpointId))
        {
            if (!string.IsNullOrEmpty(ability) && abilityNode is not null)
                CheckSpecialAbility(hardpointId, ability, owner.Id,
                    new Position(XmlUtility.GetLine(abilityNode),
                        XmlUtility.GetOpeningTagStartColumn(abilityNode), abilityNode.Name.Length),
                    pass);

            var models = pass.DeclaredModels(owner.Id);
            if (models.Count == 0) continue;

            foreach (var bone in parentBones)
                CheckBoneAgainstModels(bone, models, hardpointId, owner.Id, pass);
        }
    }

    /// <summary>
    ///     The engine enables <c>Special_Ability_Name</c> on the object mounting the hardpoint, so the
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

    // ── direction 2: a file mounting hardpoints is open ──────────────────────

    private static void CheckMountingObject(string ownerId, HtmlNode objectNode, Pass pass)
    {
        var hardpointsNode = objectNode.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            n.Name.Equals(HardpointsTag, StringComparison.OrdinalIgnoreCase));
        if (hardpointsNode is null) return;

        var models = pass.DeclaredModels(ownerId);
        if (models.Count == 0) return;

        foreach (var (hardpointId, offset) in XmlUtility.SplitListWithOffsets(hardpointsNode.InnerText))
        {
            var hardpointTags = pass.TagSource.TryGetTags(hardpointId);
            if (hardpointTags is null) continue; // unresolved hardpoint: the reference pipeline owns that

            var isTurret = IsTurretFromTags(hardpointTags);
            var position = pass.TokenPosition(hardpointsNode, offset, hardpointId);

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

                CheckBoneAgainstModels(new BoneReference(tag.TagName, bone, position),
                    models, hardpointId, ownerId, pass);
            }
        }
    }

    // ── shared ───────────────────────────────────────────────────────────────

    private static void CheckBoneAgainstModels(BoneReference bone, IReadOnlyList<string?> models,
        string hardpointId, string ownerId, Pass pass)
    {
        foreach (var model in models)
        {
            if (string.IsNullOrEmpty(model)) continue;

            if (!pass.Index.ModelBones.TryGetValue(model, out var modelBones) || modelBones.Length == 0)
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

            pass.Facts.Add(new HardpointBoneNotOnModelFact(pass.DocumentUri,
                bone.Position.Line, bone.Position.Column, bone.Position.Length,
                hardpointId, bone.Tag, bone.Value, model, ownerId));
        }
    }

    private static List<BoneReference> CollectBoneTags(HtmlNode hardpointNode)
    {
        var result = new List<BoneReference>();
        foreach (var child in hardpointNode.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            if (!IsBoneTag(child.Name)) continue;

            var value = child.InnerText.Trim();
            if (value.Length == 0) continue;

            var line = XmlUtility.GetLine(child);
            result.Add(new BoneReference(child.Name, value,
                new Position(line, XmlUtility.GetOpeningTagStartColumn(child), child.Name.Length)));
        }

        return result;
    }

    private static bool IsBoneTag(string tagName)
    {
        return ParentModelBoneTags.Contains(tagName, StringComparer.OrdinalIgnoreCase)
               || TurretModelBoneTags.Contains(tagName, StringComparer.OrdinalIgnoreCase)
               || FireBoneTags.Contains(tagName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TargetsTurretModel(string tagName, bool isTurret)
    {
        if (TurretModelBoneTags.Contains(tagName, StringComparer.OrdinalIgnoreCase)) return true;
        return isTurret && FireBoneTags.Contains(tagName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsTurret(HtmlNode hardpointNode)
    {
        return EngineBoolean.IsTrue(SingleValue(hardpointNode, IsTurretTag));
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

        private readonly Dictionary<string, IReadOnlyList<string?>> _modelsByOwner =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly EffectiveObjectResolver _resolver;
        private readonly ISchemaProvider _schema;

        public Pass(ParsedXmlDocument document, GameIndex index, string documentUri,
            ISchemaProvider schema, IVariantTagSource tagSource)
        {
            _document = document;
            _schema = schema;
            Index = index;
            DocumentUri = documentUri;
            TagSource = tagSource;
            _resolver = new EffectiveObjectResolver(index, schema, tagSource);
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
        ///     Models the object declares, resolved through variant inheritance so a variant that
        ///     inherits its model is still checked. Memoised: the same owner is reached once per
        ///     hardpoint it mounts, and each miss walks and merges the whole variant chain.
        /// </summary>
        public IReadOnlyList<string?> DeclaredModels(string ownerId)
        {
            if (_modelsByOwner.TryGetValue(ownerId, out var cached)) return cached;

            var effective = _resolver.Resolve(ownerId);
            var models = !effective.Found || effective.Cyclic
                ? []
                : effective.Tags
                    .Where(t => _schema.GetTag(t.TagName)?.ReferenceKind == ReferenceKind.ModelFile)
                    .Select(t => t.Value.Trim())
                    .Where(v => v.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Cast<string?>()
                    .ToList();

            return _modelsByOwner[ownerId] = models;
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
        ///     Objects whose <c>HardPoints</c> list mounts <paramref name="hardpointId" />. Reached
        ///     through the reference index, so only documents that actually mention it are inspected.
        /// </summary>
        public IEnumerable<GameSymbol> FindMountingObjects(string hardpointId)
        {
            if (!Index.WorkspaceReferences.TryGetValue(hardpointId, out var references))
                yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var uri in references.Select(r => r.DocumentUri).Distinct(StringComparer.Ordinal))
            {
                if (!Index.Documents.TryGetValue(uri, out var doc)) continue;

                foreach (var symbol in doc.Symbols)
                {
                    if (!seen.Add(symbol.Id)) continue;

                    var tags = TagSource.TryGetTags(symbol.Id);
                    if (tags is null) continue;

                    var mounts = tags.Any(t =>
                        t.TagName.Equals(HardpointsTag, StringComparison.OrdinalIgnoreCase) &&
                        t.Value.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries)
                            .Any(v => v.Equals(hardpointId, StringComparison.OrdinalIgnoreCase)));
                    if (mounts) yield return symbol;
                }
            }
        }

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
