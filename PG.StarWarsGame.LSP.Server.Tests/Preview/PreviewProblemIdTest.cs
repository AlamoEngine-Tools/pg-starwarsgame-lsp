// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reflection;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     Every preview finding names a diagnostic id, like every other finding this server reports.
/// </summary>
/// <remarks>
///     <para>
///         Raised by the user: a preview warning "had no diagnostic id and did not appear in the
///         Problems view the same as any other problems table we have". The id is the half that
///         makes a finding referable at all - it is what a suppression comment names, and what a
///         reader searches for when they want to know what a message means.
///     </para>
///     <para>
///         Mostly enforced by the compiler rather than by these tests: the id is a required part of
///         <see cref="PreviewProblem" />, so a new finding cannot be added without one. What is
///         checked here is the part the compiler cannot see - that the ids come from the catalogue
///         and belong to the preview's own group.
///     </para>
/// </remarks>
public sealed class PreviewProblemIdTest
{
    [Fact]
    public void SubjectThatDoesNotResolve_NamesItsId()
    {
        var scene = PreviewScene.NotFound("Nowhere_Unit", "no such object", new GameAssetTiers(0, false, false, 0));

        Assert.Equal(DiagnosticIds.PreviewSubjectNotFound,
            Assert.Single(scene.Problems).DiagnosticId);
    }

    /// <summary>
    ///     A preview finding must never borrow another group's number. Ids are a published contract
    ///     and the group is half of one, so a preview problem carrying, say, a References number
    ///     would tell a reader to suppress something else entirely.
    /// </summary>
    [Fact]
    public void EveryPreviewId_IsInThePreviewGroup()
    {
        var strays = PreviewIds()
            .Where(entry => entry.Value.Group != (int)DiagnosticGroup.Preview)
            .Select(entry => entry.Key)
            .ToList();

        Assert.Empty(strays);
    }

    [Fact]
    public void ThePreviewGroup_IsNamedForAReader()
    {
        Assert.False(string.IsNullOrWhiteSpace(
            DiagnosticGroups.NameOf(DiagnosticGroup.Preview)));
    }

    /// <summary>
    ///     Guards the catalogue against a silent emptying - the two tests above both pass happily
    ///     over no ids at all.
    /// </summary>
    [Fact]
    public void ThePreviewGroup_HasIds()
    {
        Assert.NotEmpty(PreviewIds());
    }

    private static Dictionary<string, DiagnosticId> PreviewIds()
    {
        return typeof(DiagnosticIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticId))
            .Where(field => field.Name.StartsWith("Preview", StringComparison.Ordinal))
            .ToDictionary(field => field.Name, field => (DiagnosticId)field.GetValue(null)!);
    }
}
