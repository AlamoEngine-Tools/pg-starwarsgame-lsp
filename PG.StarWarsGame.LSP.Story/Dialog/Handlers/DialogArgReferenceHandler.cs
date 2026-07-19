// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Story.Dialog.Handlers;

/// <summary>
///     Outgoing-reference validation: localisation keys (membership-only, like the XML side) and
///     object references (speech events, movies, SFX events) against the symbol index.
/// </summary>
public sealed class DialogArgReferenceHandler : IDialogDiagnosticsHandler
{
    public IEnumerable<DialogDiagnostic> Handle(DialogCommandFact fact, GameIndex index)
    {
        if (fact.Def?.Params is null) yield break;

        foreach (var param in fact.Def.Params)
        {
            if (param.Position >= fact.Command.Args.Count) continue;
            var arg = fact.Command.Args[param.Position];

            string? message = null;
            if (param.ReferenceKind == ReferenceKind.LocalisationKey)
            {
                if (!index.Localisation.ContainsKey(arg.Text))
                    message = $"'{arg.Text}' is not a known localisation key.";
            }
            else if (param.ReferenceKind == ReferenceKind.XmlObject && param.ObjectType is not null)
            {
                if (index.Resolve(arg.Text) is null)
                    message = $"'{arg.Text}' is not a recognized {param.ObjectType.TypeName}.";
            }

            if (message is not null)
                yield return new DialogDiagnostic(XmlDiagnosticSeverity.Error, message,
                    arg.Line, arg.Column, arg.Column + arg.Text.Length);
        }
    }
}