// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A variant tag on a type with no variant machinery is a warning: the engine parses it, does
///     not recognise it, and says so in the log.
/// </summary>
/// <remarks>
///     Warning rather than error because the object still loads and everything else about it works
///     - only the inheritance the author expected never happens. Kept apart from the unresolvable
///     base, which is an error: that one the engine reports nowhere at all, while this one at least
///     reaches a log file.
/// </remarks>
public sealed class VariantTagNotSupportedHandler : XmlDiagnosticsHandler<VariantTagNotSupportedFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.VariantTagNotSupported;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        VariantTagNotSupportedFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"<{fact.TagName}> does nothing on a {fact.TypeName} - only GameObjectType " +
                "inherits. The engine logs it as an unprocessed entry and loads the object " +
                "without it.")
        ];
    }
}
