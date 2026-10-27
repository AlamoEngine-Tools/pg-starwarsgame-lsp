// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <inheritdoc />
public sealed class XmlHardpointFactProducer(ISchemaProvider schema, IVariantTagSource tagSource)
    : IXmlFactProducer
{
    public string Name => "hardpoint";

    public bool AppliesTo(string documentUri)
    {
        return true;
    }

    private const string HardpointTypeName = "HardPoint";
    private const string HardpointsTag = "HardPoints";
    private const string ModelToAttachTag = "Model_To_Attach";
    private const string IsTurretTag = "Is_Turret";
    private const string DestroyableTag = "Is_Destroyable";
    private const string CollisionMeshTag = "Collision_Mesh";
    private const string SpecialAbilityNameTag = "Special_Ability_Name";
    private const string FireWhenInRocketAttackModeTag = "Fire_When_In_Rocket_Attack_Mode";
    private const string FireWhenInNormalAttackModeTag = "Fire_When_In_Normal_Attack_Mode";
    private const string RocketAttackAbility = "ROCKET_ATTACK";

    /// <summary>
    ///     The unit-mode rows a hardpoint can set, and the ability type each one needs on the
    ///     attaching object. Measured on the game: the rows are read for the owner's first and
    ///     second unit ability only, by that ability's type, while it is active (#140).
    /// </summary>
    private static readonly Dictionary<string, string> ModeRowAbility =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Defend_Mode_Weapon_Delay_Multiplier"] = "DEFEND",
            ["Turbo_Mode_Weapon_Delay_Multiplier"] = "TURBO",
            ["Power_To_Weapons_Mode_Weapon_Delay_Multiplier"] = "POWER_TO_WEAPONS",
            ["Full_Salvo_Weapon_Delay_Multiplier"] = "FULL_SALVO",
            ["Fire_When_In_Defend_Mode"] = "DEFEND",
            ["Fire_When_In_Turbo_Mode"] = "TURBO",
            ["Fire_When_In_Power_To_Weapons_Mode"] = "POWER_TO_WEAPONS"
        };

    // Bone-tag classification and cross-file attachment/model resolution are shared with the bone-model
    // inlay hint through HardpointBoneModelResolver, so the two can never disagree on a bone's target.

    public IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index)
    {
        if (!index.Documents.TryGetValue(documentUri, out var docIndex))
            return [];

        // No model indexed at all - the game's models were never catalogued, or this workspace has
        // none - is a setup condition, not a defect in any particular model, and reporting it per bone
        // reference buries the document (3806 diagnostics on vanilla Hardpoints.xml when measured).
        // The bone checks therefore run only against a catalogue (Pass.ModelsCatalogued); the ability
        // and unit-mode checks need no model and run either way.
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

        // Bones are checked only against a catalogue; the ability and mode-row checks below need
        // none, so a workspace without catalogued models still gets them.
        var bones = pass.ModelsCatalogued ? CollectBoneTags(hardpointNode, pass) : [];

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
        var modeRows = CollectModeRows(hardpointNode);
        CheckDeadWeapon(hardpointId, modeRows, pass);
        if (parentBones.Count == 0 && string.IsNullOrEmpty(ability) && modeRows.Count == 0) return;

        foreach (var owner in pass.FindAttachingObjects(hardpointId))
        {
            if (!string.IsNullOrEmpty(ability) && abilityNode is not null)
                CheckSpecialAbility(hardpointId, ability, owner.Id,
                    new Position(XmlUtility.GetLine(abilityNode),
                        XmlUtility.GetOpeningTagStartColumn(abilityNode), abilityNode.Name.Length),
                    pass);
            CheckModeRows(hardpointId, modeRows, owner.Id, pass);

            var models = pass.DeclaredModels(owner.Id);
            if (models.Count == 0) continue;

            // Anchored on the tag's VALUE (CollectBoneTags), so a quick fix replacing the range
            // replaces exactly the name that is wrong.
            foreach (var (bone, checkedAttached) in parentBones)
                CheckBoneAgainstModels(bone, models, hardpointId, owner.Id, pass, checkedAttached,
                    true);
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

    // ── unit-mode rows (#140) ─────────────────────────────────────────────────

    private static bool IsModeRow(string tagName)
    {
        return ModeRowAbility.ContainsKey(tagName)
               || tagName.Equals(FireWhenInRocketAttackModeTag, StringComparison.OrdinalIgnoreCase)
               || tagName.Equals(FireWhenInNormalAttackModeTag, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The hardpoint's mode rows from its own element, each anchored on its tag name.</summary>
    private static List<ModeRow> CollectModeRows(HtmlNode hardpointNode)
    {
        // OriginalName: HAP lower-cases Name, and the tag is quoted back in the message.
        return
        [
            .. hardpointNode.ChildNodes
                .Where(child => child.NodeType == HtmlNodeType.Element && IsModeRow(child.Name))
                .Select(child => new ModeRow(child.OriginalName ?? child.Name, child.InnerText.Trim(),
                    new Position(XmlUtility.GetLine(child), XmlUtility.GetOpeningTagStartColumn(child),
                        child.Name.Length)))
        ];
    }

    /// <summary>
    ///     Both rocket flags off: the hardpoint fires neither while ROCKET_ATTACK is active nor while
    ///     it is not, whoever attaches it - so it is reported once, naming no owner.
    /// </summary>
    private static void CheckDeadWeapon(string hardpointId, IReadOnlyList<ModeRow> rows, Pass pass)
    {
        var normal = rows.FirstOrDefault(r =>
            r.Tag.Equals(FireWhenInNormalAttackModeTag, StringComparison.OrdinalIgnoreCase));
        var rocket = rows.FirstOrDefault(r =>
            r.Tag.Equals(FireWhenInRocketAttackModeTag, StringComparison.OrdinalIgnoreCase));
        if (normal is null || rocket is null
                           || EngineBoolean.IsTrue(normal.Value) || EngineBoolean.IsTrue(rocket.Value)) return;

        pass.Facts.Add(new HardpointNeverFiresFact(pass.DocumentUri, normal.Position.Line,
            normal.Position.Column, normal.Position.Length, hardpointId, null, true));
    }

    /// <summary>
    ///     Measured on the game: a <c>*_Mode_Weapon_Delay_Multiplier</c> or <c>Fire_When_In_*_Mode</c>
    ///     row is read for the owner's first and second unit ability only, by that ability's type,
    ///     while it is active - so it applies only with the matching type in slot 0 or 1. The rocket
    ///     pair is read against ROCKET_ATTACK being active, in any slot: with the normal flag off
    ///     the hardpoint fires only in rocket attack, so an owner without the ability never fires
    ///     it; with both flags off nothing does.
    /// </summary>
    private static void CheckModeRows(string hardpointId, IReadOnlyList<ModeRow> rows, string ownerId,
        Pass pass)
    {
        if (rows.Count == 0) return;
        var abilities = pass.AbilityTypesInOrder(ownerId);
        var hasRocketAttack = abilities.Contains(RocketAttackAbility, StringComparer.OrdinalIgnoreCase);

        var normal = rows.FirstOrDefault(r =>
            r.Tag.Equals(FireWhenInNormalAttackModeTag, StringComparison.OrdinalIgnoreCase));
        var rocket = rows.FirstOrDefault(r =>
            r.Tag.Equals(FireWhenInRocketAttackModeTag, StringComparison.OrdinalIgnoreCase));
        var normalOff = normal is not null && !EngineBoolean.IsTrue(normal.Value);
        var rocketOff = rocket is not null && !EngineBoolean.IsTrue(rocket.Value);

        // Both off is owner-independent and reported once per hardpoint (CheckDeadWeapon); it is
        // the finding, and an inert rocket flag beside it is the same defect.
        if (!(normalOff && rocketOff) && !hasRocketAttack)
        {
            if (normalOff)
            {
                var at = normal!.Position;
                pass.Facts.Add(new HardpointNeverFiresFact(pass.DocumentUri, at.Line, at.Column, at.Length,
                    hardpointId, ownerId, false));
            }
            else if (rocket is not null)
            {
                pass.Facts.Add(new HardpointModeRowWithoutAbilityFact(pass.DocumentUri,
                    rocket.Position.Line, rocket.Position.Column, rocket.Position.Length,
                    hardpointId, rocket.Tag, RocketAttackAbility, ownerId, -1, abilities));
            }
        }

        foreach (var row in rows)
        {
            if (!ModeRowAbility.TryGetValue(row.Tag, out var needed)) continue;
            var slot = IndexOf(abilities, needed);
            if (slot is 0 or 1) continue;
            pass.Facts.Add(new HardpointModeRowWithoutAbilityFact(pass.DocumentUri,
                row.Position.Line, row.Position.Column, row.Position.Length,
                hardpointId, row.Tag, needed, ownerId, slot, abilities));
        }
    }

    private static int IndexOf(IReadOnlyList<string> abilities, string type)
    {
        for (var i = 0; i < abilities.Count; i++)
            if (abilities[i].Equals(type, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
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

            // The same ability check from this side: the squiggle lands on the hardpoint token in
            // this object's HardPoints list, which is where the pairing is declared.
            var ability = hardpointTags.LastOrDefault(t =>
                t.TagName.Equals(SpecialAbilityNameTag, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
            if (!string.IsNullOrEmpty(ability))
                CheckSpecialAbility(hardpointId, ability, ownerId, position, pass);

            // The mode rows from this side too, all anchored on the hardpoint token.
            var modeRows = hardpointTags.Where(t => IsModeRow(t.TagName))
                .Select(t => new ModeRow(t.TagName, t.Value.Trim(), position)).ToList();
            CheckDeadWeapon(hardpointId, modeRows, pass);
            CheckModeRows(hardpointId, modeRows, ownerId, pass);

            // Everything past here compares a bone against a model, so without one - or without a
            // catalogue to look it up in - there is nothing to check.
            if (models.Count == 0 || !pass.ModelsCatalogued) continue;

            var isTurret = IsTurretFromTags(hardpointTags);
            var attachedModel = hardpointTags.LastOrDefault(t =>
                t.TagName.Equals(ModelToAttachTag, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

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

    /// <summary>
    ///     Reports the bone against each model that lacks it, and counts it against each model whose
    ///     bones are unknown.
    /// </summary>
    /// <param name="bone">The bone tag, its value and where to report it.</param>
    /// <param name="models">The models the bone must exist on; a null entry is skipped.</param>
    /// <param name="hardpointId">The hardpoint declaring the bone.</param>
    /// <param name="ownerId">The object whose model is checked - the hardpoint itself for its turret model.</param>
    /// <param name="pass">The per-document state.</param>
    /// <param name="checkedAttachedModel">
    ///     The hardpoint's attached model when the bone was also looked for there and is missing, so
    ///     the report names both; null otherwise.
    /// </param>
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

    /// <summary>One unit-mode row as written on a hardpoint, anchored where it is reported.</summary>
    private sealed record ModeRow(string Tag, string Value, Position Position);

    /// <summary>
    ///     Per-document state for one <see cref="Produce" /> call. Exists to hold the work that is
    ///     otherwise repeated per symbol or per token: the object-node lookup, the resolved model list
    ///     of each owner, and the document's line index. A class rather than captured locals so it
    ///     copies only the fields it needs instead of keeping the enclosing scope alive.
    /// </summary>
    private sealed class Pass
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _chainByOwner =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ParsedXmlDocument _document;

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

        /// <summary>Whether any model was catalogued; without one, no bone can be checked.</summary>
        public bool ModelsCatalogued => !Index.ModelBones.IsEmpty;

        public IVariantTagSource TagSource { get; }
        public List<XmlFact> Facts { get; } = [];
        public Dictionary<(string Model, string Owner), UnavailableModel> Unavailable { get; } = [];

        /// <summary>
        ///     Every named object in the document, keyed by id. Built in one pass: resolving each
        ///     symbol with a fresh Descendants() scan made this quadratic (194 hardpoints over a
        ///     6461-line file re-walked the whole tree 194 times).
        /// </summary>
        public Dictionary<string, HtmlNode> NodesById { get; }

        /// <summary>The document's memoised line index, for value/token range computation.</summary>
        public LineOffsetIndex LineIndex => _document.LineIndex;

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

        /// <summary>
        ///     The owner's ability types in slot order, through its variant chain: a variant's own
        ///     list replaces the base's, as the game's loader does on an overlay. Memoised, because
        ///     one owner is asked once per hardpoint row.
        /// </summary>
        public IReadOnlyList<string> AbilityTypesInOrder(string ownerId)
        {
            if (_abilitiesByOwner.TryGetValue(ownerId, out var cached)) return cached;
            return _abilitiesByOwner[ownerId] = ObjectAbilities.TypesInOrder(_resolver.Resolve(ownerId));
        }

        private readonly Dictionary<string, IReadOnlyList<string>> _abilitiesByOwner =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The object's variant chain, itself first. Memoised alongside the model lookup.</summary>
        private IReadOnlyList<string> Chain(string ownerId)
        {
            if (_chainByOwner.TryGetValue(ownerId, out var cached)) return cached;

            var effective = _resolver.Resolve(ownerId);
            var chain = effective is { Found: true, Cyclic: false }
                ? effective.Chain
                : (IReadOnlyList<string>)[ownerId];

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