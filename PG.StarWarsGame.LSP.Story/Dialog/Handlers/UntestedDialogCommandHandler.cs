// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Story.Dialog.Handlers;

public sealed class UntestedDialogCommandHandler : IDialogDiagnosticsHandler
{
    public IEnumerable<DialogDiagnostic> Handle(DialogCommandFact fact, GameIndex index)
    {
        if (fact.Def is not { Untested: true }) yield break;

        var command = fact.Command;
        yield return new DialogDiagnostic(XmlDiagnosticSeverity.Warning,
            $"'{command.Name}' is documented but untested — it may not work in the engine.",
            command.Line, command.Column, command.Column + command.RawName.Length);
    }
}
