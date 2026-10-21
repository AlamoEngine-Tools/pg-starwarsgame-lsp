// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

/// <summary>
///     A story thread that comes in through a referenced project is read-only.
/// </summary>
/// <remarks>
///     A parent project is a library: inspect it, run it, do not edit it - the same contract Rider
///     and IntelliJ give a packaged source. Before this, edit mode opened on a dependency's graph,
///     the author did the work, and the SAVE failed because the change could not be staged against
///     a document the leaf does not own - the failure arriving after the effort, which is the worst
///     moment for it.
///     <para>
///         This is the backstop rather than the mechanism: the client hides the affordances, and
///         this refuses anything that reaches the command boundary by another route.
///     </para>
/// </remarks>
public sealed class DependencyOwnershipTest
{
    private static WorkspaceConfiguration Layered()
    {
        return ProjectSetup.ModOverVanilla.Configuration();
    }

    public static TheoryData<string, int> SetupsAndRanks()
    {
        var data = new TheoryData<string, int>();
        foreach (var (name, rank) in ProjectSetup.NamesAndRanks) data.Add(name, rank);
        return data;
    }

    /// <summary>
    ///     Every shape, a story file in every layer, as a path and as the URI the client sends (#280):
    ///     the root project's is editable, any lower layer's is refused naming its owner - which is
    ///     why the ordinary mod alone never notices the guard. The refusal names the owner so the
    ///     reason is actionable, and a disabled control can say why it is disabled.
    /// </summary>
    [Theory]
    [MemberData(nameof(SetupsAndRanks))]
    public void InEveryShape_OnlyTheRootProjectsDocumentIsEditable_AnyOtherNamesItsOwner(string name, int rank)
    {
        var setup = ProjectSetup.Named(name);
        var configuration = setup.Configuration();

        foreach (var document in new[] { setup.PathOf(rank, "Xml/Story.xml"), setup.Uri(rank, "Xml/Story.xml") })
        {
            var rejection = DependencyOwnership.Rejection(configuration, document);
            var owner = DependencyOwnership.ReadOnlyOwner(configuration, document);
            if (rank == setup.LeafRank)
            {
                Assert.Null(rejection);
                Assert.Null(owner);
                continue;
            }

            Assert.Contains(setup.Projects[rank], ResxMessageCatalog.Server("en").Format(rejection!));
            Assert.Equal(setup.Projects[rank], owner);
        }
    }

    /// <summary>
    ///     A command with no target document is not this guard's business - the ones that carry no
    ///     thread are refused, or allowed, by the checks that know what they do.
    /// </summary>
    [Fact]
    public void NoTargetIsNotRefusedHere()
    {
        Assert.Null(DependencyOwnership.Rejection(Layered(), null));
    }

    /// <summary>
    ///     A document under no project at all is left alone: it is not owned by a dependency, and
    ///     whatever else is wrong with it is not this guard's to report.
    /// </summary>
    [Fact]
    public void ADocumentOutsideEveryProjectIsNotRefusedHere()
    {
        Assert.Null(DependencyOwnership.Rejection(Layered(), "C:/elsewhere/story.xml"));
    }

    /// <summary>With no resolved workspace there is nothing to judge against.</summary>
    [Fact]
    public void NoWorkspaceIsNotRefused()
    {
        Assert.Null(DependencyOwnership.Rejection(null, ProjectSetup.ModOverVanilla.PathOf(1, "Xml/Story.xml")));
    }
}