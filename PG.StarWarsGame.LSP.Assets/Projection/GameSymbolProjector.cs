// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
        string? gameConstantsXml,
        string sourceManifestHash)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, GameSymbol>();

        foreach (var entry in gameObjects)
        {
            var typeName = ResolveTypeName(entry.ClassificationName);
            var sym = new GameSymbol(entry.Name, GameSymbolKind.XmlObject, typeName,
                ResolveOrigin(entry.Location), null);
            builder[sym.Id] = sym;
        }

        foreach (var entry in sfxEvents)
        {
            var sym = new GameSymbol(entry.Name, GameSymbolKind.XmlObject, "SFXEvent",
                ResolveOrigin(entry.Location), null);
            builder[sym.Id] = sym;
        }

        var (dynamicEnums, hardcodedEnums) = ExtractDynamicEnums(gameConstantsXml);

        return new BaselineIndex(builder.ToImmutable(), DateTimeOffset.UtcNow,
            sourceManifestHash, dynamicEnums, hardcodedEnums);
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

    private static (
        ImmutableDictionary<string, ImmutableArray<string>> dynamic,
        ImmutableDictionary<string, ImmutableArray<string>> hardcoded
        ) ExtractDynamicEnums(string? gameConstantsXml)
    {
        var empty = ImmutableDictionary<string, ImmutableArray<string>>.Empty;
        if (string.IsNullOrEmpty(gameConstantsXml))
            return (empty, empty);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(gameConstantsXml);
        }
        catch
        {
            return (empty, empty);
        }

        var dyn = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>();
        var hard = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>();

        var (dmgAll, dmgHard) = ParseNameListWithBoundary(doc, "Damage_Types");
        if (dmgAll.Length > 0) dyn["DamageType"] = dmgAll;
        if (dmgHard.Length > 0) hard["DamageType"] = dmgHard;

        var (armorAll, armorHard) = ParseNameListWithBoundary(doc, "Armor_Types");
        if (armorAll.Length > 0) dyn["ArmorType"] = armorAll;
        if (armorHard.Length > 0) hard["ArmorType"] = armorHard;

        return (dyn.ToImmutable(), hard.ToImmutable());
    }

    private static (ImmutableArray<string> all, ImmutableArray<string> hardcoded)
        ParseNameListWithBoundary(XDocument doc, string tagName)
    {
        var el = doc.Descendants(tagName).FirstOrDefault();
        if (el is null) return ([], []);

        var all = new List<string>();
        var hardcoded = new List<string>();
        var pastBoundary = false;

        foreach (var node in el.Nodes())
        {
            if (node is XComment comment && IsBoundaryComment(comment.Value))
            {
                pastBoundary = true;
                continue;
            }

            IEnumerable<string> tokens;
            if (node is XText text)
            {
                tokens = text.Value.Split((char[])[' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries);
            }
            else if (node is XElement child)
            {
                var v = child.Value.Trim();
                tokens = v.Length > 0 ? [v] : [];
            }
            else
            {
                continue;
            }

            foreach (var t in tokens)
            {
                all.Add(t);
                if (pastBoundary) hardcoded.Add(t);
            }
        }

        return ([..all], [..hardcoded]);
    }

    internal static bool IsBoundaryComment(string commentText)
    {
        return commentText.Contains("ABOVE this point", StringComparison.OrdinalIgnoreCase);
    }
}