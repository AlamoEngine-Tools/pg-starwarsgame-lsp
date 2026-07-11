// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Story.Dialog.Handlers;

/// <summary>Value-type validation for numeric and boolean dialog command arguments.</summary>
public sealed class DialogArgValueHandler : IDialogDiagnosticsHandler
{
    public IEnumerable<DialogDiagnostic> Handle(DialogCommandFact fact, GameIndex index)
    {
        if (fact.Def?.Params is null) yield break;

        foreach (var param in fact.Def.Params)
        {
            if (param.Position >= fact.Command.Args.Count) continue;
            var arg = fact.Command.Args[param.Position];

            var message = param.ValueType switch
            {
                XmlValueType.UInt when !uint.TryParse(arg.Text, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                    => $"'{arg.Text}' is not a valid non-negative number for '{fact.Command.Name}' argument {param.Position + 1}.",
                XmlValueType.Int when !int.TryParse(arg.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    => $"'{arg.Text}' is not a valid number for '{fact.Command.Name}' argument {param.Position + 1}.",
                XmlValueType.Boolean when arg.Text is not ("1" or "0")
                    => $"'{arg.Text}' is not valid for '{fact.Command.Name}': use 1 (on) or 0 (off).",
                _ => null
            };

            if (message is not null)
                yield return new DialogDiagnostic(XmlDiagnosticSeverity.Error, message,
                    arg.Line, arg.Column, arg.Column + arg.Text.Length);
        }
    }
}
