// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Providers;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     The shipped schema's repeat rule against the engine's field codes, measured on both 2024 x64
///     builds: a list code adds the entries of a repeat outside the variant overlay, a single value
///     is replaced by it.
/// </summary>
public sealed class EawSchemaRepeatModeTest
{
    private static readonly Lazy<LocalFileSchemaProvider> Shipped = new(() =>
        new LocalFileSchemaProvider(EawSchemaRepo.Root, new FileSystem(),
            NullLogger<LocalFileSchemaProvider>.Instance));

    // The targeting set's six fields, all six read on both builds.
    [Theory]
    [InlineData("Attack_Priorities", RepeatMode.ADD)]
    [InlineData("Unit_Exclusions", RepeatMode.ADD)]
    [InlineData("Hard_Point_Priorities", RepeatMode.ADD)]
    [InlineData("Hard_Point_Exclusions", RepeatMode.ADD)]
    [InlineData("Category_Exclusions", RepeatMode.REPLACE)]
    [InlineData("Property_Exclusions", RepeatMode.REPLACE)]
    public void TheTargetingSetFields_CarryTheMeasuredRepeatRule(string tag, RepeatMode expected)
    {
        var definition = Shipped.Value.GetTagsForType("TargetingPrioritySet")
            .Single(t => t.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expected, definition.RepeatMode);
    }

    // One of each kind on game objects: an accumulating list, a list the overlay clears, a scalar.
    [Theory]
    [InlineData("Company_Units", RepeatMode.ADD)]
    [InlineData("Projectile_Types", RepeatMode.ADD)]
    [InlineData("Max_Speed", RepeatMode.REPLACE)]
    public void GameObjectFields_CarryTheMeasuredRepeatRule(string tag, RepeatMode expected)
    {
        var definition = Shipped.Value.GetTagsForType("GameObjectType")
            .Single(t => t.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expected, definition.RepeatMode);
    }
}