// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Story.Dialog;

/// <summary>
///     Pairs every parsed dialog command with its <c>StoryDialogCommand</c> schema definition so
///     the handlers stay schema-driven - no hardcoded command table in C#.
/// </summary>
public sealed class DialogFactProducer(ISchemaProvider schema)
{
    public IReadOnlyList<DialogCommandFact> Produce(StoryDialogDocument document, string documentUri)
    {
        var commandEnum = schema.GetEnum("StoryDialogCommand");
        var facts = new List<DialogCommandFact>();
        foreach (var chapter in document.Chapters)
        foreach (var command in chapter.Commands)
        {
            var def = commandEnum?.Values.FirstOrDefault(v =>
                string.Equals(v.Name, command.Name, StringComparison.OrdinalIgnoreCase));
            facts.Add(new DialogCommandFact(documentUri, command, def));
        }

        return facts;
    }
}