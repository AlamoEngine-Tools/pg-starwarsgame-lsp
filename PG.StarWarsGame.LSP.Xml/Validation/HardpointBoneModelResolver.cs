// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     The single source of truth for which model(s) a hardpoint bone reference resolves against.
///     A hardpoint's bone tags do not all target the same model: attachment/collision/decal bones live
///     on the <em>mounting object's</em> hull, turret pivot bones live on the hardpoint's <em>own</em>
///     <c>Model_To_Attach</c>, and fire bones switch sides depending on <c>Is_Turret</c>. The mounting
///     hull is cross-file and cumulative - one hardpoint is mounted by several objects (e.g. the Underworld
///     stations mount the same hardpoint on UB_01..UB_05), each with its own model.
///     <para>
///         Shared by <see cref="XmlHardpointFactProducer" /> (validation) and the bone-model inlay hint so
///         the two can never disagree on a bone's target model. Model references are reduced through
///         <see cref="ModelBoneKey" />, matching how <see cref="GameIndex.ModelBones" /> is keyed.
///     </para>
/// </summary>
public sealed class HardpointBoneModelResolver
{
    public const string HardpointElementName = "HardPoint";

    /// <summary>Bones that live on the model of the object mounting the hardpoint.</summary>
    public static readonly string[] ParentModelBoneTags =
        ["Attachment_Bone", "Collision_Mesh", "Damage_Decal", "Damage_Particles", "Engine_Particles"];

    /// <summary>Bones that live on the hardpoint's own attached model (turret pivot/elevation).</summary>
    public static readonly string[] TurretModelBoneTags = ["Turret_Bone_Name", "Barrel_Bone_Name"];

    /// <summary>Fire origin bones - on a turret they belong to the attached model, else the parent.</summary>
    public static readonly string[] FireBoneTags = ["Fire_Bone_A", "Fire_Bone_B"];

    // Collision_Mesh is the one parent-side tag that commonly lives on the attached weapon model rather
    // than the mounting hull: a cross-check of all 194 vanilla hardpoints found the mesh on the
    // Model_To_Attach in ~90% of hardpoints that have one (every Star Destroyer / Nebulon weapon), and on
    // the hull in only a handful. So it resolves against hull UNION Model_To_Attach - valid on either.
    private static readonly string[] HullOrAttachedBoneTags = ["Collision_Mesh"];

    // Only the tactical models are checked. Galactic_Model_Name and the destroyed models are excluded on
    // purpose: a starbase's low-detail galactic mesh legitimately lacks the HP bones.
    private static readonly string[] MountingObjectModelTags =
        ["Space_Model_Name", "Land_Model_Name", "Model_Name"];

    private const string ModelToAttachTag = "Model_To_Attach";
    private const string IsTurretTag = "Is_Turret";
    private const string HardpointsTag = "HardPoints";
    private static readonly char[] ListSeparators = [',', ' ', '\t', '\r', '\n'];

    private readonly GameIndex _index;
    private readonly Dictionary<string, IReadOnlyList<string?>> _modelsByOwner =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly EffectiveObjectResolver _resolver;
    private readonly ISchemaProvider _schema;
    private readonly IVariantTagSource _tagSource;

    public HardpointBoneModelResolver(GameIndex index, ISchemaProvider schema, IVariantTagSource tagSource)
    {
        _index = index;
        _schema = schema;
        _tagSource = tagSource;
        _resolver = new EffectiveObjectResolver(index, schema, tagSource);
    }

    public static bool IsHardpointBoneTag(string tagName)
    {
        return ParentModelBoneTags.Contains(tagName, StringComparer.OrdinalIgnoreCase)
               || TurretModelBoneTags.Contains(tagName, StringComparer.OrdinalIgnoreCase)
               || FireBoneTags.Contains(tagName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether <paramref name="tagName" /> resolves against the hardpoint's own attached model.</summary>
    public static bool TargetsTurretModel(string tagName, bool isTurret)
    {
        if (TurretModelBoneTags.Contains(tagName, StringComparer.OrdinalIgnoreCase)) return true;
        return isTurret && FireBoneTags.Contains(tagName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Whether a parent-side <paramref name="tagName" /> may also legitimately resolve against the
    ///     hardpoint's <c>Model_To_Attach</c> (currently only <c>Collision_Mesh</c>). Such a bone is valid
    ///     if present on the attached model OR the mounting hull.
    /// </summary>
    public static bool MayResolveAgainstAttachedModel(string tagName)
    {
        return HullOrAttachedBoneTags.Contains(tagName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether <paramref name="modelReference" />'s bone/mesh catalog contains <paramref name="value" />.</summary>
    public static bool ModelHasBone(GameIndex index, string? modelReference, string value)
    {
        return !string.IsNullOrEmpty(modelReference)
               && index.ModelBones.TryGetValue(ModelBoneKey.From(modelReference), out var names)
               && names.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsTurret(HtmlNode hardpointNode)
    {
        return EngineBoolean.IsTrue(SingleValue(hardpointNode, IsTurretTag));
    }

    /// <summary>
    ///     The <see cref="ModelBoneKey" />-normalised model keys that <paramref name="boneTagName" /> on the
    ///     given hardpoint targets, in a stable, de-duplicated order. Turret-side bones yield the single
    ///     <c>Model_To_Attach</c>; parent-side bones yield the union of every mounting object's tactical
    ///     models (cross-file, variant-resolved). Empty when nothing is in scope.
    /// </summary>
    public IReadOnlyList<string> ResolveModelKeysForBone(
        HtmlNode hardpointNode, string hardpointId, string boneTagName)
    {
        if (TargetsTurretModel(boneTagName, IsTurret(hardpointNode)))
        {
            var model = SingleValue(hardpointNode, ModelToAttachTag);
            return model is null ? [] : [ModelBoneKey.From(model)];
        }

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var owner in FindMountingObjects(hardpointId))
        foreach (var model in DeclaredModels(owner.Id))
            AddKey(model);

        // Collision_Mesh may live on the attached weapon model as well as (or instead of) the hull.
        if (MayResolveAgainstAttachedModel(boneTagName))
            AddKey(SingleValue(hardpointNode, ModelToAttachTag));

        return keys;

        void AddKey(string? model)
        {
            if (string.IsNullOrEmpty(model)) return;
            var key = ModelBoneKey.From(model);
            if (seen.Add(key)) keys.Add(key);
        }
    }

    /// <summary>
    ///     The model keys a bone-name reference tag targets, dispatching on context: a tag inside a
    ///     <c>HardPoint</c> resolves by role (<see cref="ResolveModelKeysForBone" />); any other bone tag
    ///     resolves against the sibling/ancestor models declared on its own object
    ///     (<see cref="BoneModelScopeResolver.FindModelKeys" />). The single entry point shared by the
    ///     bone-model inlay hint and bone-name completion so both agree with the validator.
    /// </summary>
    public IReadOnlyList<string> ResolveModelKeysForBoneTag(HtmlNode boneTag)
    {
        var parent = boneTag.ParentNode;
        if (parent is { NodeType: HtmlNodeType.Element } &&
            parent.Name.Equals(HardpointElementName, StringComparison.OrdinalIgnoreCase))
        {
            var hardpointId = XmlUtility.GetNameAttributeValue(parent);
            return hardpointId is null ? [] : ResolveModelKeysForBone(parent, hardpointId, boneTag.Name);
        }

        return BoneModelScopeResolver.FindModelKeys(boneTag, _schema);
    }

    /// <summary>
    ///     Models the object declares, resolved through variant inheritance so a variant that inherits its
    ///     model is still checked. Restricted to the tactical <see cref="MountingObjectModelTags" />.
    /// </summary>
    public IReadOnlyList<string?> DeclaredModels(string ownerId)
    {
        if (_modelsByOwner.TryGetValue(ownerId, out var cached)) return cached;

        var effective = _resolver.Resolve(ownerId);
        var models = !effective.Found || effective.Cyclic
            ? []
            : effective.Tags
                .Where(t => MountingObjectModelTags.Contains(t.TagName, StringComparer.OrdinalIgnoreCase))
                .Select(t => t.Value.Trim())
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string?>()
                .ToList();

        return _modelsByOwner[ownerId] = models;
    }

    /// <summary>
    ///     Objects whose <c>HardPoints</c> list mounts <paramref name="hardpointId" />. Reached through the
    ///     reference index, so only documents that actually mention it are inspected.
    /// </summary>
    public IEnumerable<GameSymbol> FindMountingObjects(string hardpointId)
    {
        if (!_index.WorkspaceReferences.TryGetValue(hardpointId, out var references))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var uri in references.Select(r => r.DocumentUri).Distinct(StringComparer.Ordinal))
        {
            if (!_index.Documents.TryGetValue(uri, out var doc)) continue;

            foreach (var symbol in doc.Symbols)
            {
                if (!seen.Add(symbol.Id)) continue;

                var tags = _tagSource.TryGetTags(symbol.Id);
                if (tags is null) continue;

                var mounts = tags.Any(t =>
                    t.TagName.Equals(HardpointsTag, StringComparison.OrdinalIgnoreCase) &&
                    t.Value.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries)
                        .Any(v => v.Equals(hardpointId, StringComparison.OrdinalIgnoreCase)));
                if (mounts) yield return symbol;
            }
        }
    }

    private static string? SingleValue(HtmlNode objectNode, string tagName)
    {
        var node = objectNode.ChildNodes.LastOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            n.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        var value = node?.InnerText.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
