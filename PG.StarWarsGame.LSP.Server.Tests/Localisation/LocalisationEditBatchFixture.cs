// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using PG.Commons.Hashing;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Localisation;
using PG.StarWarsGame.LSP.Localisation.Rows;
using PG.StarWarsGame.LSP.Server.Editing;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     The localisation editors' two composers over a file system of the test's own, for the tests
///     of what a batch composes. A composer writes nothing, so a test reads what a batch comes to
///     from the edit in the answer and holds the files to having stayed as they were.
/// </summary>
internal sealed class LocalisationEditBatchFixture
{
    private readonly FileHelper _helper;

    /// <param name="files">Each file's path and text.</param>
    /// <param name="layers">Which layer owns which text directory; the project owns everything when not given.</param>
    public LocalisationEditBatchFixture(
        IReadOnlyDictionary<string, string> files, Func<FileHelper, IProjectLayerMap>? layers = null)
    {
        Files = new MockFileSystem(files.ToDictionary(f => f.Key, f => new MockFileData(f.Value)));
        _helper = new FileHelper(Files);

        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(Files);
        services.SupportPetroglyphHashing();
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(_helper);
        services.TryAddSingleton<ILspConfigurationProvider>(new FakeLspConfigurationProvider());
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        services.AddSingleton<ILocalisationDocumentEditor, LocalisationDocumentEditor>();
        services.AddSingleton<IMessageCatalog>(ResxMessageCatalog.Server("en"));
        var provider = services.BuildServiceProvider();

        var documents = new LocalisationBatchDocuments(
            _helper,
            layers?.Invoke(_helper) ?? new ProjectLayerMap(_helper),
            new DocumentTextSource(Host, _helper, NullLogger<DocumentTextSource>.Instance),
            TestCatalogues.English);
        var editor = provider.GetRequiredService<ILocalisationDocumentEditor>();

        Translation = new TranslationEditBatchComposer(
            documents, editor,
            provider.GetRequiredService<ILocalisationRowReader>(),
            provider.GetRequiredService<ICrc32HashingService>(),
            NullLogger<TranslationEditBatchComposer>.Instance, TestCatalogues.English);
        Credits = new CreditsEditBatchComposer(
            documents, editor, NullLogger<CreditsEditBatchComposer>.Instance, TestCatalogues.English);
    }

    public MockFileSystem Files { get; }

    /// <summary>The documents the window has open; empty unless a test opens one.</summary>
    public FakeGameWorkspaceHost Host { get; } = new();

    public TranslationEditBatchComposer Translation { get; }

    public CreditsEditBatchComposer Credits { get; }

    /// <summary>The files as an editor names them: each with the hash of its text as it stands.</summary>
    public EditBatchSubject Subject(params string[] paths)
    {
        return new EditBatchSubject(Documents: paths
            .Select(p => new EditBatchDocument(p, LocalisationContentHash.Compute(Files.File.ReadAllText(p))))
            .ToList());
    }

    /// <summary>The URI the server knows a file by, to open it in <see cref="Host" />.</summary>
    public string UriOf(string path)
    {
        return _helper.NormalizeUri(path);
    }

    /// <summary>The commands as a batch carries them.</summary>
    public static JArray Staged<TCommand>(params TCommand[] commands)
    {
        return JArray.FromObject(commands);
    }

    /// <summary>
    ///     The text a batch composes for each file it changes, by file name. A file the batch leaves
    ///     as it is has no entry.
    /// </summary>
    public static Dictionary<string, string> Composed(ApplyEditBatchResult result)
    {
        return (result.Edit?.DocumentChanges ?? [])
            .Where(c => c.IsTextDocumentEdit)
            .ToDictionary(
                c => Path.GetFileName(c.TextDocumentEdit!.TextDocument.Uri.GetFileSystemPath()),
                c => c.TextDocumentEdit!.Edits.Single().NewText,
                StringComparer.OrdinalIgnoreCase);
    }
}