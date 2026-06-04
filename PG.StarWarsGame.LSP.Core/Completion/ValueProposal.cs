// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Completion;

public record ValueProposal
{
    public required string Label { get; init; }
    public string? Detail { get; init; }

    /// <summary>Text inserted on accept; falls back to <see cref="Label" /> when null.</summary>
    public string? InsertText { get; init; }

    /// <summary>
    ///     Right-aligned secondary label shown in the completion popup (maps to
    ///     <c>CompletionItem.labelDetails.description</c> in LSP 3.17). Clients that do not support
    ///     <c>labelDetails</c> ignore it; the <see cref="Detail" /> field remains the universal fallback.
    /// </summary>
    public string? Description { get; init; }
}