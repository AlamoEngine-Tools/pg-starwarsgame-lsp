// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>Keeps every publish a diagnostics publisher sends, in order, where the editor would receive it.</summary>
public sealed class RecordingDiagnosticsSink : IDiagnosticsSink
{
    public RecordingDiagnosticsSink() : this([])
    {
    }

    /// <summary>Records into a list the test already holds.</summary>
    public RecordingDiagnosticsSink(List<PublishDiagnosticsParams> into)
    {
        Published = into;
    }

    public List<PublishDiagnosticsParams> Published { get; }

    public void Publish(PublishDiagnosticsParams diagnostics)
    {
        Published.Add(diagnostics);
    }
}