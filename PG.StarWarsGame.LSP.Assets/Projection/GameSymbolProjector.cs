// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using PG.StarWarsGame.Files.XML;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Projection;

public sealed class GameSymbolProjector(ISchemaProvider schema)
{
    // Matches a word boundary between an uppercase letter following a lowercase letter,
    // or between a run of uppercase letters and a final uppercase+lowercase pair.
    // "CombatBonusAbility" → "COMBAT_BONUS_ABILITY" (via ToUpperInvariant after split)
    private static readonly Regex PascalWordBoundary =
        new(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);

    public BaselineIndex Project(
        IEnumerable<ProjectableEntry> gameObjects,
        IEnumerable<ProjectableEntry> sfxEvents,
        string sourceManifestHash,
        IEnumerable<ProjectableEntry>? musicEvents = null,
        IEnumerable<ProjectableEntry>? shadowBlobMaterials = null,
        IEnumerable<ProjectableEntry>? singletons = null,
        IEnumerable<ProjectableEntry>? factions = null)
    {
        // One list per id, one entry per type in it: a later definition of the same type replaces the
        // earlier, a definition of another type sits beside it - the shipped game has a Sarlacc faction
        // and a Sarlacc indigenous unit, and each type is an id space of its own. Projection order
        // within the list is what a bare id resolves to, so game objects come first. Keyed as the game
        // reads names: case folded.
        var builder = new Dictionary<string, List<GameSymbol>>(StringComparer.OrdinalIgnoreCase);
        var objectTags = ImmutableDictionary.CreateBuilder<string, ImmutableArray<BaselineTag>>(
            StringComparer.OrdinalIgnoreCase);

        void Add(GameSymbol sym)
        {
            if (!builder.TryGetValue(sym.Id, out var list))
                builder[sym.Id] = list = [];
            var same = list.FindIndex(s => string.Equals(s.TypeName, sym.TypeName, StringComparison.OrdinalIgnoreCase));
            if (same >= 0) list[same] = sym;
            else list.Add(sym);
        }

        foreach (var entry in gameObjects)
        {
            var typeName = ResolveTypeName(entry.ClassificationName);
            var tags = entry.Tags ?? [];
            var sym = new GameSymbol(entry.Name, GameSymbolKind.XmlObject, typeName,
                ResolveOrigin(entry.Location), null, ResolveVariantBaseId(tags),
                ResolveBehaviors(entry), entry.Flags);
            Add(sym);
            if (tags.Count > 0)
                objectTags[entry.Name] = [.. tags];
        }

        foreach (var entry in sfxEvents)
        {
            var tags = entry.Tags ?? [];
            var sym = new GameSymbol(entry.Name, GameSymbolKind.XmlObject, "SFXEvent",
                ResolveOrigin(entry.Location), null, ResolveVariantBaseId(tags));
            Add(sym);
            if (tags.Count > 0)
                objectTags[entry.Name] = [.. tags];
        }

        // STOPGAP: PG.StarWarsGame.Engine has no MusicEvent game manager (unlike SFXEvent's
        // ISfxEventGameManager) - entries come from BaselineBuilder parsing MusicEvents.xml
        // directly. See the big comment in BaselineBuilder/Program.cs for the full rationale and
        // the TODO to replace this once the engine adds first-class support.
        foreach (var entry in musicEvents ?? [])
        {
            var tags = entry.Tags ?? [];
            var sym = new GameSymbol(entry.Name, GameSymbolKind.XmlObject, "MusicEvent",
                ResolveOrigin(entry.Location), null, ResolveVariantBaseId(tags));
            Add(sym);
            if (tags.Count > 0)
                objectTags[entry.Name] = [.. tags];
        }

        // Same stopgap as music events: no engine-level manager exists for shadow blob
        // materials - entries come from BaselineBuilder parsing Shadowblobmaterials.xml directly.
        foreach (var entry in shadowBlobMaterials ?? [])
        {
            var sym = new GameSymbol(entry.Name, GameSymbolKind.XmlObject, "ShadowBlobMaterial",
                ResolveOrigin(entry.Location), null);
            Add(sym);
        }

        // Same stopgap again: the engine keeps factions in a manager of their own that
        // PG.StarWarsGame.Engine does not expose - entries come from FactionReader over the files the
        // FactionFiles registry lists. Without them no baseline carried a faction, and a project that
        // ships no Factions.xml of its own had vanilla's Rebel and Empire reported as missing.
        foreach (var entry in factions ?? [])
        {
            var sym = new GameSymbol(entry.Name, GameSymbolKind.XmlObject, GameTypeNames.Faction,
                ResolveOrigin(entry.Location), null);
            Add(sym);
        }

        // SINGLETONS - GameConstants, AudioConstants. A type declared with no `nameTag` has exactly
        // one instance and no Name attribute, so its TYPE NAME is its id; the same rule the
        // workspace parser applies, which is what lets a mod's own file shadow the base game's.
        //
        // Their TAGS are the reason this exists: a symbol alone would leave every value in the file
        // as unreachable as before. GameConstants is where ShipNameTextFiles, the Encyclopedia_*
        // geometry and the Corruption_* block live.
        foreach (var entry in singletons ?? [])
        {
            var tags = entry.Tags ?? [];
            // The classification IS the type name here - it must not go through the
            // classification-to-PascalCase conversion, which would mangle it.
            var sym = new GameSymbol(entry.Name, GameSymbolKind.XmlObject, entry.ClassificationName,
                ResolveOrigin(entry.Location), null);
            Add(sym);
            if (tags.Count > 0)
                objectTags[entry.Name] = [.. tags];
        }

        return new BaselineIndex(
            builder.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow,
            sourceManifestHash,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty)
        {
            ObjectTags = objectTags.ToImmutable()
        };
    }

    /// <summary>
    ///     Returns the base object id from a <c>Variant_Of_Existing_Type</c> tag among the object's tags,
    ///     identified via the schema's <see cref="TagSemanticType.VariantParent" /> semantic type, or null.
    /// </summary>
    private string? ResolveVariantBaseId(IReadOnlyList<BaselineTag> tags)
    {
        foreach (var tag in tags)
            if (schema.GetTag(tag.TagName)?.SemanticType == TagSemanticType.VariantParent)
                return tag.Value;
        return null;
    }

    /// <summary>
    ///     The behaviour tokens a shipped object declares itself, or null when it declares none.
    ///     Taken from the entry when it was given them, otherwise from its captured tags - and
    ///     always through the same tokenizer as the workspace parser, since the two must never
    ///     disagree about what an object is.
    /// </summary>
    private static string[]? ResolveBehaviors(ProjectableEntry entry)
    {
        if (entry.Behaviors is { Length: > 0 }) return entry.Behaviors;

        var tokens = ObjectBehaviors.FromTags((entry.Tags ?? []).Select(t => (t.TagName, t.Value)));
        return tokens.Length == 0 ? null : tokens;
    }

    private string ResolveTypeName(string classificationName)
    {
        var candidate = ClassificationToTypeName(classificationName);
        return schema.GetObjectType(candidate) is not null ? candidate : "GameObjectType";
    }

    // "COMBAT_BONUS_ABILITY" → "CombatBonusAbility"
    internal static string ClassificationToTypeName(string classification)
    {
        return string.Concat(
            classification.Split('_')
                .Where(w => w.Length > 0)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    // "CombatBonusAbility" → "COMBAT_BONUS_ABILITY"  (used in BaselineBuilder adapter)
    internal static string TypeNameToClassification(string typeName)
    {
        return PascalWordBoundary.Replace(typeName, "_").ToUpperInvariant();
    }

    private static SymbolOrigin ResolveOrigin(in XmlLocationInfo location)
    {
        if (string.IsNullOrEmpty(location.XmlFile))
            return new UnknownOrigin("no source location");
        return new FileOrigin(location.XmlFile, location.Line ?? 0, null);
    }
}