// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     <c>FILTER_NONE</c> was missing from <c>StoryEventFilter</c>, so shipped story plots drew a
///     false error.
/// </summary>
/// <remarks>
///     <para>
///         Both of the base game's uses land in slots typed against <c>StoryEventFilter</c>:
///         <c>STORY_ENTER</c> position 1 (<c>Event_Param2</c>) and <c>STORY_CONQUER</c> position 2
///         (<c>Event_Param3</c>). The member sat only in <c>StoryContainmentFilter</c>, which no
///         slot carrying this value uses, so every occurrence was reported as an unknown enum
///         value.
///     </para>
///     <para>
///         Added rather than moved. <c>StoryContainmentFilter</c> is referenced by two slots and
///         nothing shows the member is wrong there; removing it could trade one false positive for
///         another, and an enum that accepts a value it never sees costs nothing.
///     </para>
/// </remarks>
public sealed class EawSchemaStoryEventFilterTest
{
    [Fact]
    public void FilterNone_IsAStoryEventFilter()
    {
        Assert.Contains("FILTER_NONE", Members("StoryEventFilter"), StringComparer.Ordinal);
    }

    // The members the shipped plots actually exercise, so the addition cannot have displaced one.
    [Theory]
    [InlineData("FILTER_FRIENDLY_ONLY")]
    [InlineData("FILTER_ENEMY_ONLY")]
    [InlineData("GROUND")]
    [InlineData("SPACE")]
    public void TheExistingMembers_AreStillThere(string member)
    {
        Assert.Contains(member, Members("StoryEventFilter"), StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> Members(string enumName)
    {
        return YamlSchemaParser.ParseEnumFile(File.ReadAllText(Find(enumName + ".yaml")))
            .Values.Select(v => v.Name).ToList();
    }

    private static string Find(string file)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaStoryEventFilterTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw", "enums", file);
                if (File.Exists(candidate)) return candidate;
                break;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"schema/eaw/enums/{file} not found.");
    }
}
