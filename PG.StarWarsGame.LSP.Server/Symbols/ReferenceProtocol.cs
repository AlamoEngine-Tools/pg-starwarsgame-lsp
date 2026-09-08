// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Symbols;

/// <summary>
///     Go-to for a reference value that is not sitting in a document the client has open.
/// </summary>
/// <remarks>
///     <para>
///         The ordinary way to jump to a definition is <c>textDocument/definition</c> over a
///         position, and where a position exists that is still the right request. This one exists
///         for the panels: a webview showing an ability row holds a NAME and nothing else - no
///         document, no offset - because the row was assembled by the server from several files. The
///         name plus the type it is expected to be is all the reader has, and it is enough.
///     </para>
///     <para>
///         Deliberately ungated. <c>aet/resolveStoryReference</c> answers the same question but only
///         while the story editor is switched on, which is no basis for the model preview to be able
///         to open a <c>SpecialAbility</c>.
///     </para>
/// </remarks>
/// <param name="Value">The value as the XML writes it.</param>
/// <param name="ReferenceType">
///     The type the reference is expected to be - the schema's <c>referenceType</c>, e.g.
///     <c>SpecialAbility</c>. A hint: unknown and umbrella names simply resolve untyped.
/// </param>
[Method("aet/resolveReference", Direction.ClientToServer)]
public sealed record ResolveReferenceParams(string Value, string? ReferenceType = null)
    : IRequest<ResolveReferenceResult>;

/// <param name="Uri">The defining file, or null when <paramref name="Error" /> says why not.</param>
/// <param name="Error">
///     Why there is nothing to open. Worth showing the reader verbatim: "defined in the base game"
///     and "does not resolve" mean very different things to someone editing a mod.
/// </param>
public sealed record ResolveReferenceResult(
    string? Uri = null,
    int Line = 0,
    int Column = 0,
    string? Error = null);

/// <summary>Handles <c>aet/resolveReference</c>.</summary>
public sealed class ResolveReferenceHandler(IDefinitionLocator locator)
    : IJsonRpcRequestHandler<ResolveReferenceParams, ResolveReferenceResult>
{
    public Task<ResolveReferenceResult> Handle(ResolveReferenceParams request, CancellationToken ct)
    {
        var located = locator.Locate(request.Value, request.ReferenceType);
        return Task.FromResult(new ResolveReferenceResult(
            located.Uri, located.Line, located.Column, located.Error));
    }
}
