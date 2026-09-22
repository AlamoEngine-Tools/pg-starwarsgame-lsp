// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Reads the real <c>schema/eaw/enums/StoryEventType.yaml</c>, for tests that pin what a story
///     event's parameters are against the engine's own behaviour.
/// </summary>
internal static class StoryEventSchema
{
    private static readonly Lazy<RawEnumDefinition> Definition = new(() => Load("StoryEventType.yaml"));
    private static readonly Lazy<RawEnumDefinition> Rewards = new(() => Load("StoryRewardType.yaml"));

    /// <summary>The named event, or throws naming it - a typo here should not read as "no params".</summary>
    public static RawEnumValueDefinition Value(string name)
    {
        return Value(Definition.Value, "StoryEventType", name);
    }

    /// <summary>The named reward, or throws naming it.</summary>
    public static RawEnumValueDefinition Reward(string name)
    {
        return Value(Rewards.Value, "StoryRewardType", name);
    }

    public static IReadOnlyList<RawEnumValueDefinition> AllEvents()
    {
        return Definition.Value.Values;
    }

    public static IReadOnlyList<RawEnumValueDefinition> AllRewards()
    {
        return Rewards.Value.Values;
    }

    private static RawEnumValueDefinition Value(RawEnumDefinition definition, string enumName, string name)
    {
        var value = definition.Values.FirstOrDefault(v =>
            string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        return value
               ?? throw new InvalidOperationException($"{enumName}.yaml declares no '{name}'.");
    }

    private static RawEnumDefinition Load(string enumFile)
    {
        var path = Find(enumFile)
                   ?? throw new InvalidOperationException(
                       $"schema/eaw/enums/{enumFile} not found - is the schema checked out?");
        return YamlSchemaParser.ParseEnumFile(File.ReadAllText(path));
    }

    private static string? Find(string enumFile)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(StoryEventSchema).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw", "enums", enumFile);
                return File.Exists(candidate) ? candidate : null;
            }

            dir = dir.Parent;
        }

        return null;
    }
}