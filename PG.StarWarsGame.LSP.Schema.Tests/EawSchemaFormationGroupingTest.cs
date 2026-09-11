// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     <c>FormationGrouping</c> spelled its middle member <c>Same_Order</c>, which the engine does
///     not accept.
/// </summary>
/// <remarks>
///     <para>
///         Measured in the 2018 binary: the member table is three consecutive strings at
///         <c>01535250</c>, <c>0153525c</c> and <c>01535268</c> - <c>Standard</c>, <c>SameOrder</c>,
///         <c>Solo</c>. There is no <c>Same_Order</c> anywhere in the image.
///     </para>
///     <para>
///         Shipped data cannot arbitrate this one: vanilla only ever writes <c>Solo</c> (20 uses)
///         and <c>Standard</c> (102), so the third member is exercised by mods alone. That is
///         exactly why it stayed wrong - a modder writing <c>Same_Order</c> got no complaint from
///         us and nothing from the engine.
///     </para>
///     <para>
///         The engine's ORDER differs too (Standard, SameOrder, Solo against our Solo, SameOrder,
///         Standard). That is left alone deliberately: nothing reads this enum's ordinals, so the
///         order carries no meaning here and churning it would only cost a diff.
///     </para>
/// </remarks>
public sealed class EawSchemaFormationGroupingTest
{
    [Fact]
    public void TheMiddleMember_IsSpelledAsTheEngineReadsIt()
    {
        Assert.Contains("SameOrder", Members(), StringComparer.Ordinal);
    }

    [Fact]
    public void TheUnderscoredSpelling_IsGone()
    {
        Assert.DoesNotContain("Same_Order", Members(), StringComparer.Ordinal);
    }

    // The two the shipped corpus actually exercises must survive the rename.
    [Theory]
    [InlineData("Solo")]
    [InlineData("Standard")]
    public void TheMembersVanillaUses_AreStillThere(string member)
    {
        Assert.Contains(member, Members(), StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> Members()
    {
        return YamlSchemaParser.ParseEnumFile(File.ReadAllText(Find("FormationGrouping.yaml")))
            .Values.Select(v => v.Name).ToList();
    }

    private static string Find(string file)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaFormationGroupingTest).Assembly.Location)!);
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
