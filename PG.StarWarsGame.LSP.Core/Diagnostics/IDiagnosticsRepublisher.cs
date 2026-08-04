// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     A diagnostics publisher that can be asked to publish everything again, for changes it has no
///     other way of hearing about.
///     <para>
///         Diagnostics normally refresh when the index or a document changes. A project-wide
///         suppression changes neither: it edits <c>.aetswg/suppressions.json</c>, and every
///         diagnostic already on screen was computed before it existed. Without an explicit
///         republish the suppression appears not to work until each file is next touched.
///     </para>
///     <para>
///         Implemented by <see cref="DiagnosticsPublisherBase" />, so every language gets it - the
///         store is shared, so refreshing only the language the quick fix was invoked from would
///         leave the other two stale.
///     </para>
/// </summary>
public interface IDiagnosticsRepublisher
{
    Task RepublishAllAsync(CancellationToken ct);
}

/// <summary>
///     A diagnostics publisher that can retract everything it published for one document.
///     <para>
///         Diagnostics are replaced per URI, so they normally go stale only until the next publish
///         for that file. Removing a workspace folder has no next publish: the files stop being
///         indexed, so whatever was last shown for them would stay on screen forever unless it is
///         explicitly retracted.
///     </para>
///     <para>
///         Implemented by <see cref="DiagnosticsPublisherBase" />, so every language gets it - a
///         removed folder's files must lose their XML, Lua and dialog diagnostics alike.
///     </para>
/// </summary>
public interface IDocumentDiagnosticsClearer
{
    void ClearDocument(string uri);
}
