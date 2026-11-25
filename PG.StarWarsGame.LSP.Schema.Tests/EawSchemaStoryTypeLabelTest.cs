// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Every story event and reward type carries a display name - a few words saying what the type
///     is, for where the editor shows a name and not an identifier. The story graph's palette shows
///     it on a tile by default, so a type without one would be the one tile that reads
///     <c>STORY_...</c> among names, and a new type cannot ship without one.
/// </summary>
public sealed class EawSchemaStoryTypeLabelTest
{
    public static TheoryData<string> Enums => ["StoryEventType", "StoryRewardType"];

    private static IReadOnlyList<RawEnumValueDefinition> Values(string enumName)
    {
        return enumName == "StoryEventType" ? StoryEventSchema.AllEvents() : StoryEventSchema.AllRewards();
    }

    private static string? Label(RawEnumValueDefinition value)
    {
        return value.Label.GetValueOrDefault("en");
    }

    [Theory]
    [MemberData(nameof(Enums))]
    public void EveryType_HasADisplayName(string enumName)
    {
        var missing = Values(enumName)
            .Where(v => string.IsNullOrWhiteSpace(Label(v)))
            .Select(v => v.Name)
            .ToList();

        Assert.True(missing.Count == 0, $"{enumName} types without a label: " + string.Join(", ", missing));
    }

    // Two tiles with one name cannot be told apart, and a search by that name finds both.
    [Theory]
    [MemberData(nameof(Enums))]
    public void NoTwoTypes_ShareADisplayName(string enumName)
    {
        var shared = Values(enumName)
            .Where(v => !string.IsNullOrWhiteSpace(Label(v)))
            .GroupBy(v => Label(v)!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"'{g.Key}' ({string.Join(", ", g.Select(v => v.Name))})")
            .ToList();

        Assert.True(shared.Count == 0, $"{enumName} labels used twice: " + string.Join("; ", shared));
    }

    // A display name is a label: a few words, a capital first, no full stop, plain ASCII. The
    // sentence stays in the description. The length is what a palette tile shows on two lines.
    [Theory]
    [MemberData(nameof(Enums))]
    public void ADisplayName_IsAShortLabel(string enumName)
    {
        var offenders = Values(enumName)
            .Select(v => (v.Name, Label: Label(v)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Label))
            .Where(x => x.Label!.Length > 28
                        || x.Label.Split(' ').Length > 4
                        || x.Label.EndsWith('.')
                        || !char.IsAsciiLetterUpper(x.Label[0])
                        || x.Label.Any(c => c is < ' ' or > '~')
                        || x.Label != x.Label.Trim())
            .Select(x => $"{x.Name} '{x.Label}'")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{enumName} labels that are not a short label: " + string.Join(", ", offenders));
    }

    // The name must not hide what the type is called in XML: a label that IS the identifier, in
    // either spelling, tells the reader nothing the technical name does not.
    [Theory]
    [MemberData(nameof(Enums))]
    public void ADisplayName_IsNotTheIdentifierRespelled(string enumName)
    {
        var offenders = Values(enumName)
            .Where(v => !string.IsNullOrWhiteSpace(Label(v)))
            .Where(v => Label(v)!.Contains('_') || Label(v) == v.Name)
            .Select(v => $"{v.Name} '{Label(v)}'")
            .ToList();

        Assert.True(offenders.Count == 0, $"{enumName} labels that are identifiers: " + string.Join(", ", offenders));
    }
}