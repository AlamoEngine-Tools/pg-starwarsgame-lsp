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

        var facts = new List<XmlFact>();
        var unavailable = new Dictionary<(string Model, string Owner), UnavailableModel>();
        var hapDoc = document.Html;

        foreach (var symbol in docIndex.Symbols)
        {
            var node = FindObjectNode(hapDoc, symbol.Id);
            if (node is null) continue;

            if (string.Equals(symbol.TypeName, HardpointTypeName, StringComparison.OrdinalIgnoreCase))
                CheckHardpoint(symbol.Id, node, index, documentUri, facts, unavailable);
            else
                CheckMountingObject(symbol.Id, node, index, documentUri, facts, unavailable);
        }

        // One diagnostic per unreadable model rather than per bone pointing at it.
        foreach (var (key, info) in unavailable)
            facts.Add(new HardpointModelBonesUnavailableFact(documentUri,
                info.Line, info.Column, info.Length, key.Model, key.Owner, info.Count));

        return facts;
    }

    // ── direction 1: a hardpoint file is open ────────────────────────────────

    private void CheckHardpoint(string hardpointId, HtmlNode hardpointNode, GameIndex index,
        string documentUri, List<XmlFact> facts,
        Dictionary<(string Model, string Owner), UnavailableModel> unavailable)
    {
        var bones = CollectBoneTags(hardpointNode);
        if (bones.Count == 0) return;

        var isTurret = IsTurret(hardpointNode);
        var turretModel = SingleValue(hardpointNode, ModelToAttachTag);

        // Turret-side bones resolve against the hardpoint's own attached model, so they can be
        // checked without knowing anything about who mounts it.
        foreach (var bone in bones.Where(b => TargetsTurretModel(b.Tag, isTurret)))
            CheckBoneAgainstModels(bone, [turretModel], hardpointId, hardpointId, index, documentUri, facts, unavailable);

        // Parent-side bones need the mounting objects' models.
        var parentBones = bones.Where(b => !TargetsTurretModel(b.Tag, isTurret)).ToList();
        if (parentBones.Count == 0) return;

        foreach (var owner in FindMountingObjects(hardpointId, index))
        {
            var models = DeclaredModels(owner, index);
            if (models.Count == 0) continue;

            foreach (var bone in parentBones)
                CheckBoneAgainstModels(bone, models, hardpointId, owner.Id, index, documentUri, facts, unavailable);
        }
    }

    // ── direction 2: a file mounting hardpoints is open ──────────────────────

    private void CheckMountingObject(string ownerId, HtmlNode objectNode, GameIndex index,
        string documentUri, List<XmlFact> facts,
        Dictionary<(string Model, string Owner), UnavailableModel> unavailable)
    {
        var hardpointsNode = objectNode.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            n.Name.Equals(HardpointsTag, StringComparison.OrdinalIgnoreCase));
        if (hardpointsNode is null) return;

        var owner = index.Resolve(ownerId);
        if (owner is null) return;

        var models = DeclaredModels(owner, index);
        if (models.Count == 0) return;

        foreach (var (hardpointId, offset) in XmlUtility.SplitListWithOffsets(hardpointsNode.InnerText))
        {
            var hardpointTags = tagSource.TryGetTags(hardpointId);
            if (hardpointTags is null) continue; // unresolved hardpoint: the reference pipeline owns that

            var isTurret = IsTurretFromTags(hardpointTags);
            var position = TokenPosition(hardpointsNode, offset, hardpointId, documentUri);

            foreach (var tag in hardpointTags)
            {
                if (!IsBoneTag(tag.TagName) || TargetsTurretModel(tag.TagName, isTurret)) continue;
                var bone = tag.Value.Trim();
                if (bone.Length == 0) continue;

                CheckBoneAgainstModels(
                    new BoneReference(tag.TagName, bone, position),
                    models, hardpointId, ownerId, index, documentUri, facts, unavailable);
            }
        }
    }

    // ── shared ───────────────────────────────────────────────────────────────

    private static void CheckBoneAgainstModels(BoneReference bone, IReadOnlyList<string?> models,
        string hardpointId, string ownerId, GameIndex index, string documentUri, List<XmlFact> facts,
        Dictionary<(string Model, string Owner), UnavailableModel> unavailable)
    {
        foreach (var model in models)
        {
            if (string.IsNullOrEmpty(model)) continue;

            if (!index.ModelBones.TryGetValue(model, out var modelBones) || modelBones.Length == 0)
            {
                // Accumulate: one diagnostic per unreadable model, anchored at the first bone that
                // could not be checked, rather than one per bone.
                var key = (model, ownerId);
                unavailable[key] = unavailable.TryGetValue(key, out var seen)
                    ? seen with { Count = seen.Count + 1 }
                    : new UnavailableModel(bone.Position.Line, bone.Position.Column,
                        bone.Position.Length, 1);
                continue;
            }

            if (modelBones.Contains(bone.Value, StringComparer.OrdinalIgnoreCase)) continue;

            facts.Add(new HardpointBoneNotOnModelFact(documentUri,
                bone.Position.Line, bone.Position.Column, bone.Position.Length,
                hardpointId, bone.Tag, bone.Value, model, ownerId));
        }
    }

    /// <summary>
    ///     Objects whose <c>HardPoints</c> list mounts <paramref name="hardpointId" />. Reached through
    ///     the reference index, so only documents that actually mention the hardpoint are inspected.
    /// </summary>
    private IEnumerable<GameSymbol> FindMountingObjects(string hardpointId, GameIndex index)
    {
        if (!index.WorkspaceReferences.TryGetValue(hardpointId, out var references))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var uri in references.Select(r => r.DocumentUri).Distinct(StringComparer.Ordinal))
        {
            if (!index.Documents.TryGetValue(uri, out var doc)) continue;

            foreach (var symbol in doc.Symbols)
            {
                if (!seen.Add(symbol.Id)) continue;

                var tags = tagSource.TryGetTags(symbol.Id);
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
    ///     Every model the object declares, resolved through variant inheritance so a variant that
    ///     inherits its model is still checked. All of them must carry the bone.
    /// </summary>
    private IReadOnlyList<string?> DeclaredModels(GameSymbol owner, GameIndex index)
    {
        var effective = new EffectiveObjectResolver(index, schema, tagSource).Resolve(owner.Id);
        if (!effective.Found || effective.Cyclic) return [];

        return effective.Tags
            .Where(t => schema.GetTag(t.TagName)?.ReferenceKind == ReferenceKind.ModelFile)
            .Select(t => t.Value.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string?>()
            .ToList();
    }

    private List<BoneReference> CollectBoneTags(HtmlNode hardpointNode)
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
        return IsAffirmative(SingleValue(hardpointNode, IsTurretTag));
    }

    private static bool IsTurretFromTags(IReadOnlyList<VariantTag> tags)
    {
        return IsAffirmative(tags.LastOrDefault(t =>
            t.TagName.Equals(IsTurretTag, StringComparison.OrdinalIgnoreCase))?.Value);
    }

    private static bool IsAffirmative(string? value)
    {
        value = value?.Trim();
        return value is not null
               && (value.Equals("Yes", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("True", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("1", StringComparison.Ordinal));
    }

    private static string? SingleValue(HtmlNode objectNode, string tagName)
    {
        var node = objectNode.ChildNodes.LastOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            n.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        var value = node?.InnerText.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static Position TokenPosition(HtmlNode listNode, int offset, string token, string documentUri)
    {
        var absolute = listNode.InnerStartIndex + offset;
        var (line, column) = new LineOffsetIndex(listNode.OwnerDocument.Text).GetPosition(absolute);
        return new Position(line, column, token.Length);
    }

    private static HtmlNode? FindObjectNode(HtmlDocument doc, string objectId)
    {
        return doc.DocumentNode.Descendants()
            .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element &&
                                 n.Attributes.Any(a =>
                                     a.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(a.Value?.Trim(), objectId, StringComparison.OrdinalIgnoreCase)));
    }

    private readonly record struct Position(int Line, int Column, int Length);

    /// <summary>A model with no readable bone list, and how many bone references it left unchecked.</summary>
    private readonly record struct UnavailableModel(int Line, int Column, int Length, int Count);

    private readonly record struct BoneReference(string Tag, string Value, Position Position);
}
