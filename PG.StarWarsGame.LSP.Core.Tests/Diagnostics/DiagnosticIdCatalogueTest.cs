// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reflection;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics;

/// <summary>
///     Guards the central id catalogue. Ids end up in suppression comments that users commit, so a
///     duplicate or a silently reassigned id would suppress the wrong diagnostic in their files -
///     these are the checks that make the catalogue safe to grow.
/// </summary>
public sealed class DiagnosticIdCatalogueTest
{
    private static IReadOnlyList<(string Name, DiagnosticId Id)> All()
    {
        return typeof(DiagnosticIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticId))
            .Select(f => (f.Name, (DiagnosticId)f.GetValue(null)!))
            .ToList();
    }

    [Fact]
    public void Catalogue_IsNotEmpty()
    {
        Assert.NotEmpty(All());
    }

    // The failure this prevents: two diagnostics sharing an id means suppressing one silences the
    // other, in a file the user already committed.
    [Fact]
    public void Ids_AreUnique()
    {
        var duplicates = All()
            .GroupBy(e => e.Id)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} <- {string.Join(", ", g.Select(e => e.Name))}")
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void EveryId_RoundTripsThroughItsWireFormat()
    {
        foreach (var (name, id) in All())
        {
            Assert.True(DiagnosticId.TryParse(id.ToString(), out var parsed), name);
            Assert.Equal(id, parsed);
        }
    }

    // An id whose group is not in the enum cannot be described to the user or offered as a
    // "suppress this whole group" action.
    [Fact]
    public void EveryGroup_IsADeclaredDiagnosticGroup()
    {
        var declared = Enum.GetValues<DiagnosticGroup>().Select(g => (int)g).ToHashSet();

        var orphans = All()
            .Where(e => !declared.Contains(e.Id.Group))
            .Select(e => $"{e.Name} ({e.Id})")
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void EveryGroup_HasAHumanReadableName()
    {
        foreach (var group in Enum.GetValues<DiagnosticGroup>())
            Assert.False(string.IsNullOrWhiteSpace(DiagnosticGroups.NameOf(group)));
    }
}
