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
        IEnumerable<ProjectableEntry>? musicEvents = null)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, GameSymbol>();
        var objectTags = ImmutableDictionary.CreateBuilder<string, ImmutableArray<BaselineTag>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in gameObjects)
        {
            var typeName = ResolveTypeName(entry.ClassificationName);
            var tags = entry.Tags ?? [];
            var sym = new GameSymbol(entry.Name, GameSymbolKind.XmlObject, typeName,
                ResolveOrigin(entry.Location), null, ResolveVariantBaseId(tags));
            builder[sym.Id] = sym;
            if (tags.Count > 0)
                objectTags[entry.Name] = [.. tags];
        }

        foreach (var entry in sfxEvents)
        {
            var tags = entry.Tags ?? [];
            var sym = new GameSymbol(entry.Name, GameSymbolKind.XmlObject, "SFXEvent",
                ResolveOrigin(entry.Location), null, ResolveVariantBaseId(tags));
            builder[sym.Id] = sym;
            if (tags.Count > 0)
                objectTags[entry.Name] = [.. tags];
        }

        // STOPGAP: PG.StarWarsGame.Engine has no MusicEvent game manager (unlike SFXEvent's
        // ISfxEventGameManager) — entries come from BaselineBuilder parsing MusicEvents.xml
        // directly. See the big comment in BaselineBuilder/Program.cs for the full rationale and
        // the TODO to replace this once the engine adds first-class support.
        foreach (var entry in musicEvents ?? [])
        {
            var tags = entry.Tags ?? [];
            var sym = new GameSymbol(entry.Name, GameSymbolKind.XmlObject, "MusicEvent",
                ResolveOrigin(entry.Location), null, ResolveVariantBaseId(tags));
            builder[sym.Id] = sym;
            if (tags.Count > 0)
                objectTags[entry.Name] = [.. tags];
        }

        return new BaselineIndex(builder.ToImmutable(), DateTimeOffset.UtcNow,
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