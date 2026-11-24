// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Editing;

namespace PG.StarWarsGame.LSP.Server.Tests.Editing;

/// <summary>
///     A batch does not write below the project (#339 UC-2): whichever composer made the edit, an
///     edit that changes or creates a file of a dependency or of the game is refused with a message
///     that names the file and the layer it belongs to, and nothing is handed to the editor.
/// </summary>
public sealed class EditBatchLayerGuardTest
{
    private static readonly string Root = Path.GetPathRoot(Path.GetFullPath("."))!;
    private static readonly string CoreXml = Path.Combine(Root, "core", "data", "xml");
    private static readonly string ModXml = Path.Combine(Root, "mod", "data", "xml");
    private static readonly string ModText = Path.Combine(Root, "mod", "data", "text");

    private static readonly FileHelper Helper = new(new MockFileSystem());

    private static EditBatchLayerGuard Guard(bool layered = true)
    {
        var map = new ProjectLayerMap(Helper);
        if (layered)
            map.SetLayers([
                new ProjectLayer(0, "Core", [CoreXml], [], [], [], null),
                new ProjectLayer(1, "Mod", [ModXml], [], [ModText], [], null)
            ]);
        return new EditBatchLayerGuard(map, Helper, TestCatalogues.English);
    }

    private static WorkspaceEdit Changing(params string[] paths)
    {
        return new WorkspaceEdit
        {
            DocumentChanges = new Container<WorkspaceEditDocumentChange>(paths.Select(p =>
                new WorkspaceEditDocumentChange(new TextDocumentEdit
                {
                    TextDocument = new OptionalVersionedTextDocumentIdentifier
                    {
                        Uri = DocumentUri.From(Helper.NormalizeUri(p))
                    },
                    Edits = new TextEditContainer(new TextEdit { NewText = "x" })
                })))
        };
    }

    private static WorkspaceEdit Creating(string path)
    {
        return new WorkspaceEdit
        {
            DocumentChanges = new Container<WorkspaceEditDocumentChange>(
                new WorkspaceEditDocumentChange(new CreateFile { Uri = DocumentUri.From(Helper.NormalizeUri(path)) }))
        };
    }

    [Fact]
    public void AnEditOfTheProjectsOwnFiles_Passes()
    {
        Assert.Null(Guard().Objection(Changing(
            Path.Combine(ModXml, "story.xml"), Path.Combine(ModText, "texts.csv"))));
    }

    [Fact]
    public void AnEditOfADependencysFile_IsRefused_NamingTheFileAndItsLayer()
    {
        var file = Path.Combine(CoreXml, "story.xml");

        var objection = Guard().Objection(Changing(Path.Combine(ModXml, "story.xml"), file));

        Assert.NotNull(objection);
        Assert.Contains("story.xml", objection);
        Assert.Contains("belongs to Core, which is read-only", objection);
    }

    [Fact]
    public void CreatingAFileInADependency_IsRefused()
    {
        var objection = Guard().Objection(Creating(Path.Combine(CoreXml, "new_thread.xml")));

        Assert.Contains("belongs to Core, which is read-only", objection);
    }

    // A file under no layer is one somebody opened by hand: it is not a dependency's, so it is not
    // this rule's to refuse.
    [Fact]
    public void AnEditOfAFileUnderNoLayer_Passes()
    {
        Assert.Null(Guard().Objection(Changing(Path.Combine(Root, "elsewhere", "notes.xml"))));
    }

    [Fact]
    public void BeforeAnyProjectIsLoaded_EveryEditPasses()
    {
        Assert.Null(Guard(false).Objection(Changing(Path.Combine(CoreXml, "story.xml"))));
    }

    [Fact]
    public void NoEdit_Passes()
    {
        Assert.Null(Guard().Objection(null));
        Assert.Null(Guard().Objection(new WorkspaceEdit()));
    }

    /// <summary>The pair holds every composer's edit to the rule: the answer is the refusal, with no edit in it.</summary>
    [Fact]
    public async Task ApplyEditBatch_WithAComposerWhoseEditReachesBelowTheProject_IsRefusedWithNoEdit()
    {
        var composer = new FixedComposer(Changing(Path.Combine(CoreXml, "story.xml")));

        var result = await new ApplyEditBatchHandler(
                new EditBatchComposers([composer]), Guard(), TestCatalogues.English)
            .Handle(new ApplyEditBatchParams("fixed", new EditBatchSubject(), []), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Edit);
        Assert.Contains("belongs to Core, which is read-only", result.Error);
    }

    [Fact]
    public async Task ApplyEditBatch_WithAnEditOfTheProjectsOwnFile_HandsTheEditOn()
    {
        var edit = Changing(Path.Combine(ModXml, "story.xml"));

        var result = await new ApplyEditBatchHandler(
                new EditBatchComposers([new FixedComposer(edit)]), Guard(), TestCatalogues.English)
            .Handle(new ApplyEditBatchParams("fixed", new EditBatchSubject(), []), CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Same(edit, result.Edit);
    }

    private sealed class FixedComposer(WorkspaceEdit edit) : IEditBatchComposer
    {
        public string Editor => "fixed";

        public Task<ValidateEditBatchResult> ValidateAsync(
            EditBatchSubject subject, JArray commands, CancellationToken ct)
        {
            return Task.FromResult(new ValidateEditBatchResult([]));
        }

        public Task<ApplyEditBatchResult> ComposeAsync(
            EditBatchSubject subject, JArray commands, CancellationToken ct)
        {
            return Task.FromResult(new ApplyEditBatchResult(true, Edit: edit));
        }
    }
}