// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Story;

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
    private const string LeafDir = "C:/mods/rev";
    private const string CoreDir = "C:/mods/core";

    private static WorkspaceConfiguration Layered()
    {
        ProjectLayer[] layers =
        [
            new(1, "Revan's Revenge", [LeafDir + "/data/xml"], [], [], [], null,
                LeafDir + "/rev.pgproj"),
            new(0, "EaWX Core", [CoreDir + "/data/xml"], [], [], [], null, CoreDir + "/core.pgproj")
        ];
        return WorkspaceConfiguration.Empty with { Layers = layers };
    }

    [Fact]
    public void ADocumentInTheRootProjectIsEditable()
    {
        Assert.Null(DependencyOwnership.Rejection(Layered(), LeafDir + "/data/xml/story.xml"));
    }

    /// <summary>The refusal names the project that owns it, so the reason is actionable.</summary>
    [Fact]
    public void ADocumentInAReferencedProjectIsRefusedByName()
    {
        var rejection = DependencyOwnership.Rejection(Layered(), CoreDir + "/data/xml/core_story.xml");

        Assert.NotNull(rejection);
        Assert.Contains("EaWX Core", rejection);
    }

    /// <summary>A file URI is the shape the client actually sends.</summary>
    [Fact]
    public void ItUnderstandsAFileUri()
    {
        Assert.NotNull(DependencyOwnership.Rejection(
            Layered(), "file:///c:/mods/core/data/xml/core_story.xml"));
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
    ///     A single-project workspace has no dependency, so nothing is ever refused - the ordinary
    ///     mod must not notice this exists.
    /// </summary>
    [Fact]
    public void ASingleProjectWorkspaceIsNeverRefused()
    {
        var single = WorkspaceConfiguration.Empty with
        {
            Layers =
            [
                new ProjectLayer(0, "Mod", [LeafDir + "/data/xml"], [], [], [], null,
                    LeafDir + "/rev.pgproj")
            ]
        };

        Assert.Null(DependencyOwnership.Rejection(single, LeafDir + "/data/xml/story.xml"));
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
        Assert.Null(DependencyOwnership.Rejection(null, LeafDir + "/data/xml/story.xml"));
    }

    /// <summary>
    ///     The graph carries the owner's NAME rather than a bare flag: a disabled control has to say
    ///     why it is disabled, and in a layered setup "which project owns this thread" is the
    ///     author's actual question.
    /// </summary>
    [Fact]
    public void TheOwnerOfAReferencedDocumentIsNamed()
    {
        Assert.Equal("EaWX Core",
            DependencyOwnership.ReadOnlyOwner(Layered(), CoreDir + "/data/xml/core_story.xml"));
    }

    /// <summary>No owner means editable - the same answer the command boundary gives.</summary>
    [Fact]
    public void TheOwnerOfAnEditableDocumentIsNull()
    {
        Assert.Null(DependencyOwnership.ReadOnlyOwner(Layered(), LeafDir + "/data/xml/story.xml"));
    }
}