// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Story.Dialog.Handlers;

/// <summary>
///     Argument-count validation. For dialog commands a missing <c>Params</c> list on the schema
///     value means "takes no arguments" (every command's slots are fully declared in
///     StoryDialogCommand.yaml), unlike story event params where null means unconstrained.
/// </summary>
public sealed class DialogCommandArityHandler : IDialogDiagnosticsHandler
{
    public IEnumerable<DialogDiagnostic> Handle(DialogCommandFact fact, GameIndex index)
    {
        if (fact.Def is null) yield break;

        var command = fact.Command;
        var expected = fact.Def.Params?.Count ?? 0;
        var required = fact.Def.Params?.Count(p => !p.Optional) ?? 0;
        var got = command.Args.Count;

        if (got < required)
        {
            yield return new DialogDiagnostic(XmlDiagnosticSeverity.Error,
                $"'{command.Name}' expects {expected} argument(s), got {got}.",
                command.Line, command.Column,
                got > 0 ? EndOf(command.Args[^1]) : command.Column + command.RawName.Length);
        }
        else if (got > expected)
        {
            var firstExtra = command.Args[expected];
            var message = expected == 0
                ? $"'{command.Name}' takes no arguments, got {got}."
                : $"'{command.Name}' expects {expected} argument(s), got {got}.";
            yield return new DialogDiagnostic(XmlDiagnosticSeverity.Error, message,
                firstExtra.Line, firstExtra.Column, EndOf(command.Args[^1]));
        }
    }

    private static int EndOf(StoryDialogToken token)
    {
        return token.Column + token.Text.Length;
    }
}