// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Story.Dialog;

/// <summary>
///     One parsed dialog command paired with its <c>StoryDialogCommand</c> schema definition
///     (<c>null</c> when the command is unknown to the schema).
/// </summary>
public sealed record DialogCommandFact(
    string DocumentUri,
    StoryDialogCommand Command,
    EnumValueDefinition? Def);

/// <summary>A dialog diagnostic with its 0-based single-line range.</summary>
/// <param name="Id">
///     Overrides the reporting handler's <see cref="IDialogDiagnosticsHandler.DefaultId" />, for a
///     handler that reports more than one kind of problem. Null means "use the default", which is
///     the common case.
/// </param>
public sealed record DialogDiagnostic(
    XmlDiagnosticSeverity Severity,
    string Message,
    int Line,
    int Column,
    int EndColumn,
    DiagnosticId? Id = null);

/// <summary>One validation concern over dialog command facts (dispatcher pattern, like the XML handlers).</summary>
public interface IDialogDiagnosticsHandler
{
    /// <summary>
    ///     Id carried by this handler's diagnostics unless one overrides it. Required, so that no
    ///     dialog diagnostic can reach the client unsuppressable - the same rule the XML handlers
    ///     follow.
    /// </summary>
    DiagnosticId DefaultId { get; }

    IEnumerable<DialogDiagnostic> Handle(DialogCommandFact fact, GameIndex index);
}

public sealed class DialogDiagnosticsHandlerRegistry(IEnumerable<IDialogDiagnosticsHandler> handlers)
{
    private readonly IReadOnlyList<IDialogDiagnosticsHandler> _handlers = handlers.ToList();

    public IEnumerable<DialogDiagnostic> Dispatch(DialogCommandFact fact, GameIndex index)
    {
        foreach (var handler in _handlers)
        foreach (var diagnostic in handler.Handle(fact, index))
            yield return diagnostic.Id is null ? diagnostic with { Id = handler.DefaultId } : diagnostic;
    }
}