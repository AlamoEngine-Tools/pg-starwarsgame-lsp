// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reflection;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Tests.Schema;

/// <summary>
///     Seven <see cref="XmlValueType" /> members carried
///     <c>[Obsolete("No usages found in EaW XML data files")]</c> while the shipped corpus uses
///     them.
/// </summary>
/// <remarks>
///     <para>
///         Measured against <c>eaw/Data</c> and <c>foc/Data</c>, counting only real game data - the
///         schema manifest <c>DatabaseMapExport.xml</c> is a listing of what exists, not a use of
///         it, so it does not count:
///     </para>
///     <para>
///         <c>Attack_Priorities</c> in 6 files (three targeting-priority files per game),
///         <c>Clash_Actions</c> in both <c>Heroclash.xml</c>, <c>Projectile_Types_Targeted</c> in
///         the Juggernaut and Crusader Gunship, <c>Projectile_Combat_Mod</c> in one,
///         <c>ShipNameTextFiles</c> in two, the four <c>Music_Event_*_Vs_Faction</c> tags in eight,
///         and <c>Default_Bounty_By_Category_SP</c>/<c>_MP</c> in one each.
///     </para>
///     <para>
///         A wrong Obsolete marker is worse than none: it reads as permission to delete a type the
///         shipped data depends on, and it suppresses the reader's instinct to look closer.
///     </para>
/// </remarks>
public sealed class XmlValueTypeObsoleteMarkerTest
{
    // Every one of these was measured in the shipped corpus.
    [Theory]
    [InlineData(XmlValueType.MusicEventPerFactionMap)]
    [InlineData(XmlValueType.ShipNameTextFileList)]
    [InlineData(XmlValueType.CategoryToIntegerMap)]
    [InlineData((XmlValueType)58)]
    [InlineData((XmlValueType)68)]
    [InlineData((XmlValueType)81)]
    [InlineData((XmlValueType)82)]
    public void TypesTheShippedDataUses_AreNotMarkedObsolete(XmlValueType value)
    {
        Assert.Null(ObsoleteOn(value));
    }

    private static ObsoleteAttribute? ObsoleteOn(XmlValueType value)
    {
        var member = typeof(XmlValueType)
            .GetMember(value.ToString(), BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault();
        Assert.NotNull(member);
        return member!.GetCustomAttribute<ObsoleteAttribute>();
    }
}
