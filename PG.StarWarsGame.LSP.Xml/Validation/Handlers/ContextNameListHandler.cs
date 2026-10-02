// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Named handler (ID: <c>context-name-list</c>) for tags holding any number of tuple groups in
///     one value - <c>Land_Terrain_Model_Mapping</c>. Reached through <c>validationOverride</c>.
/// </summary>
/// <remarks>
///     It counts and nothing else; what each item is belongs to the tag's slots, checked per item by
///     the slot handlers. Those run on their own fact, which is why <c>mode: replace</c> here cannot
///     discard them the way it discards every default value handler.
/// </remarks>
public sealed class ContextNameListHandler : XmlDiagnosticsHandler<XmlTagValueFact>, IXmlNamedDiagnosticsHandler
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.ContextNameList;

    public string ValidationId => "context-name-list";

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        return TupleShape.WholeGroups(fact) is { } error ? [error] : [];
    }
}
