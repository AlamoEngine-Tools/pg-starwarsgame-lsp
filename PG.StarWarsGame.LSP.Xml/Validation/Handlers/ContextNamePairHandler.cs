// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Named handler (ID: <c>context-name-pair</c>) for tags holding exactly one tuple group -
///     <c>Music_Event_List_Ambient</c> and <c>Music_Event_List_Battle</c>, which repeat the TAG
///     rather than the value. Reached through <c>validationOverride</c>.
/// </summary>
/// <remarks>
///     <para>
///         It counts and nothing else. The music event used to be looked up here, by name and
///         against nothing in particular; it is the tag's second slot now, typed as a MusicEvent
///         reference, so the parser records it and the reference pipeline resolves it - with
///         hover, go-to and rename besides. <see cref="DiagnosticIds.ContextNamePairUnresolvedMusicEvent" />
///         is no longer raised; ids are append-only, so it stays registered.
///     </para>
///     <para>
///         <c>mode: replace</c> discards every default value handler for these tags. Nothing is
///         lost by it: the reference and slot checks run on facts of their own.
///     </para>
/// </remarks>
public sealed class ContextNamePairHandler : XmlDiagnosticsHandler<XmlTagValueFact>, IXmlNamedDiagnosticsHandler
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.ContextNamePair;

    public string ValidationId => "context-name-pair";

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        return TupleShape.ExactlyOneGroup(fact) is { } error ? [error] : [];
    }
}