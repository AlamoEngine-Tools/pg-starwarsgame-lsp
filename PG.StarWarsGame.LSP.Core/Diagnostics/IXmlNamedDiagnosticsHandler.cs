// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Extends <see cref="IXmlDiagnosticsHandler" /> with a stable string identifier so the handler
///     can be addressed from YAML schema via <c>validationOverride.validationId</c>.
///     Register implementations via DI as <see cref="IXmlDiagnosticsHandler" />; the registry
///     detects this interface automatically.
/// </summary>
public interface IXmlNamedDiagnosticsHandler : IXmlDiagnosticsHandler
{
    string ValidationId { get; }
}