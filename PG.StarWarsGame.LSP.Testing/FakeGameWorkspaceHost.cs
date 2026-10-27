// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>
///     The open-document store a test hands its handlers (#304): a dictionary keyed on the
///     normalised URI, so the spelling a handler asks with need not be the spelling the document
///     was opened with. Each document keeps the URI it was opened with.
/// </summary>
public sealed class FakeGameWorkspaceHost : IGameWorkspaceHost
{
    private static readonly IFileHelper Files = new FileHelper(new MockFileSystem());

    private readonly Dictionary<string, TrackedDocument> _docs = new(DocumentUris.Comparer);

    public FakeGameWorkspaceHost()
    {
    }

    /// <summary>A host holding one document, which is what most handler tests need.</summary>
    public FakeGameWorkspaceHost(string uri, string text, int version = 1)
    {
        With(uri, text, version);
    }

    public IEnumerable<TrackedDocument> All => _docs.Values;

    /// <summary>Every <see cref="AddOrUpdate" />, in order, as the caller wrote it.</summary>
    public List<TrackedDocument> Writes { get; } = [];

    /// <summary>Every <see cref="Remove" />, in order.</summary>
    public List<string> Removals { get; } = [];

    public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
    {
        var doc = new TrackedDocument(uri, text, version, publishDiagnostics);
        Writes.Add(doc);
        _docs[Files.NormalizeUri(uri)] = doc;
    }

    public void Remove(string uri)
    {
        Removals.Add(uri);
        _docs.Remove(Files.NormalizeUri(uri));
    }

    public bool TryGet(string uri, out TrackedDocument doc)
    {
        return _docs.TryGetValue(Files.NormalizeUri(uri), out doc!);
    }

    /// <summary>Seeds a document, for a fluent setup.</summary>
    public FakeGameWorkspaceHost With(string uri, string text, int version = 1)
    {
        AddOrUpdate(uri, text, version);
        return this;
    }
}