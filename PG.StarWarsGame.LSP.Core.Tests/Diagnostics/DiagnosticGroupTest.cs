// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics;

/// <summary>
///     The group numbers are a suppression contract: an author's <c>aetswg-disable</c> comment names
///     an id, and the id carries its group. Groups are append-only and never renumbered. The two
///     groups added for 0.5.0 are assigned here, together, before either producer exists, so the
///     strictness pass and the Lua analyzer fold cannot collide or drift apart.
/// </summary>
public sealed class DiagnosticGroupTest
{
    [Theory]
    [InlineData(DiagnosticGroup.XmlStrictness, 15, "XML strictness")]
    [InlineData(DiagnosticGroup.LuaAnalyzer, 16, "Lua analyzer")]
    public void TheGroupsAddedFor050_KeepTheirNumbersAndNames(DiagnosticGroup group, int number, string name)
    {
        Assert.Equal(number, (int)group);
        Assert.Equal(name, DiagnosticGroups.NameOf(group));
    }

    [Fact]
    public void GroupNumbers_AreUniqueAndNeverReused()
    {
        var numbers = Enum.GetValues<DiagnosticGroup>().Select(g => (int)g).ToArray();

        Assert.Equal(numbers.Length, numbers.Distinct().Count());
        // Appended, not inserted: the newest group has the highest number.
        Assert.Equal(16, numbers.Max());
    }
}