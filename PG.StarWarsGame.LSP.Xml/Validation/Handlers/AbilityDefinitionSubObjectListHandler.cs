// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Structural container handler for <c>Abilities</c> (AbilityDefinitionSubObjectList).
///     The tag holds heterogeneous named child elements — there is no scalar value to validate.
/// </summary>
public sealed class AbilityDefinitionSubObjectListHandler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.AbilityDefinitionSubObjectList;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
        => [];
}
