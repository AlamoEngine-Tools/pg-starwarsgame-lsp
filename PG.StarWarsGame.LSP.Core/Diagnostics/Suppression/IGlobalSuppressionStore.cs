// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

/// <summary>
///     Names of the LSP commands backing suppression.
///     <para>
///         Here rather than on the handler because the two ends live in assemblies that cannot see
///         each other: the quick fix is built in the XML layer, the handler that executes it in the
///         server. A copy on each side would be free to drift, leaving a code action that silently
///         does nothing when invoked.
///     </para>
/// </summary>
public static class SuppressionCommands
{
    /// <summary>Records a matcher in <c>.aetswg/suppressions.json</c>. Takes one id or group.</summary>
    public const string SuppressGlobally = "aet-eaw-edit.lsp.suppressDiagnosticGlobally";
}

/// <summary>
///     The report actions beside the suppressions. Both are CLIENT commands: the client holds the
///     extension version, the clipboard and the browser, so the server never registers them - it
///     only names them in the code action, with the diagnostic as the one argument.
/// </summary>
public static class DiagnosticReportCommands
{
    /// <summary>Opens a prefilled GitHub issue. The author submits it; nothing is posted.</summary>
    public const string ReportOnGitHub = "aet-eaw-edit.reportDiagnosticOnGitHub";

    /// <summary>Opens the same report, untrimmed, in an untitled editor tab.</summary>
    public const string OpenInEditor = "aet-eaw-edit.openDiagnosticReport";
}

/// <summary>
///     Project-wide suppressions, persisted in <c>.aetswg/suppressions.json</c>.
///     <para>
///         The interface lives here so the diagnostics pipeline can consult it without depending on
///         the server's project layer - the same split as <c>IStoryChainProblemStore</c>. Unlike the
///         index caches beside it, this file is meant to be committed: <c>.aetswg/.gitignore</c>
///         excludes only <c>indices/</c>, so a team shares its decisions about what not to report.
///     </para>
/// </summary>
public interface IGlobalSuppressionStore
{
    /// <summary>Matchers in force for the whole project. Empty when there is no project file.</summary>
    IReadOnlyList<SuppressionMatcher> GetAll();

    /// <summary>
    ///     Adds a matcher and persists it. Adding one that is already covered is a no-op, so the
    ///     "suppress everywhere" quick fix stays idempotent.
    /// </summary>
    void Add(SuppressionMatcher matcher, string? reason = null);

    /// <summary>Removes a matcher and persists. Silent when it was not present.</summary>
    void Remove(SuppressionMatcher matcher);
}