// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Server.Editing;

/// <summary>Reads a batch's commands as one editor's own.</summary>
public static class EditBatchCommands
{
    /// <summary>
    ///     The commands as <typeparamref name="TCommand" />s, in order.
    /// </summary>
    /// <returns>Null when they were read; otherwise the message saying they could not be.</returns>
    public static string? Read<TCommand>(
        JArray? commands, IMessageCatalog messages, out IReadOnlyList<TCommand> read)
    {
        read = [];
        if (commands is null) return null;

        try
        {
            read = commands.ToObject<List<TCommand>>() ?? [];
            return null;
        }
        catch (JsonException ex)
        {
            return messages.Format("EditBatch_CommandsUnreadable", ex.Message);
        }
    }
}