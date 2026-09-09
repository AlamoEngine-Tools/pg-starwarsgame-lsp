// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Persistence;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Suppression;

/// <summary>One suppression as it is written down: the id, and why.</summary>
public sealed class SuppressionEntry
{
    /// <summary>Wire form of a <see cref="SuppressionMatcher" />: an id or <c>aetswg-000-*</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Free text explaining the decision. Never read back - it is there for the next reader.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

/// <summary>
///     JSON sidecar at <c>.aetswg/suppressions.json</c>, held by a
///     <see cref="SidecarStore{T}" />: cached in memory, written through on every change, degrading
///     to in-memory when there is no <c>.pgproj</c>, and versioned by
///     <see cref="SuppressionsDocument" />.
///     <para>
///         Unlike the layout sidecar this one is intended for version control - suppressing a
///         diagnostic is a decision about the mod, not editor state - which is why
///         <c>.aetswg/.gitignore</c> excludes only <c>indices/</c>.
///     </para>
/// </summary>
public sealed class GlobalSuppressionStore : IGlobalSuppressionStore
{
    private readonly object _gate = new();
    private readonly AetswgSidecarLocator _locator;
    private readonly ILogger<GlobalSuppressionStore> _logger;
    private readonly IUserNotifier? _notifier;
    private readonly SidecarStore<SuppressionsDocument.Payload> _store;

    /// <summary>
    ///     Whether the file's own problems have been reported. GetAll runs on every publish, and
    ///     ballooning the user once per keystroke is worse than the typo it is reporting.
    /// </summary>
    private bool _reported;

    public GlobalSuppressionStore(
        IModProjectReloadService reloadService,
        IFileHelper fileHelper,
        ILogger<GlobalSuppressionStore> logger,
        IUserNotifier? notifier = null)
    {
        _notifier = notifier;
        _logger = logger;
        _locator = new AetswgSidecarLocator(reloadService);
        _store = new SidecarStore<SuppressionsDocument.Payload>(
            "suppressions.json", SuppressionsDocument.TypeName, SuppressionsDocument.Version,
            SuppressionsDocument.Migrations, _locator, fileHelper, logger,
            () => new SuppressionsDocument.Payload());
    }

    public IReadOnlyList<SuppressionMatcher> GetAll()
    {
        lock (_gate)
        {
            var result = new List<SuppressionMatcher>();
            foreach (var entry in LoadLocked().Entries)
                // A malformed entry is skipped, not fatal: it must leave the diagnostic visible
                // rather than take the whole suppression file down with it.
                if (SuppressionMatcher.TryParse(entry.Id, out var matcher))
                    result.Add(matcher);

            return result;
        }
    }

    public void Add(SuppressionMatcher matcher, string? reason = null)
    {
        lock (_gate)
        {
            var document = LoadLocked();
            var wire = matcher.ToString();
            if (document.Entries.Any(e => string.Equals(e.Id, wire, StringComparison.OrdinalIgnoreCase))) return;

            document.Entries.Add(new SuppressionEntry { Id = wire, Reason = reason });
            SaveLocked(document);
        }
    }

    public void Remove(SuppressionMatcher matcher)
    {
        lock (_gate)
        {
            var document = LoadLocked();
            var wire = matcher.ToString();
            if (document.Entries.RemoveAll(e => string.Equals(e.Id, wire, StringComparison.OrdinalIgnoreCase)) == 0)
                return;

            SaveLocked(document);
        }
    }

    private SuppressionsDocument.Payload LoadLocked()
    {
        var load = _store.Load();
        if (_reported) return load.Value;

        _reported = true;
        // Failing open - reporting everything - is the safe direction: a file we cannot read must
        // not silence diagnostics wholesale, which looks exactly like "the mod is clean".
        if (load.Message is { } message) Report(message + WhereToLook());
        ReportUnreadableEntries(load.Value);

        return load.Value;
    }

    private void SaveLocked(SuppressionsDocument.Payload document)
    {
        // A refused save is the newer-file case: the store says so, and the change stays in memory
        // rather than overwriting a document this build cannot read.
        if (!_store.TrySave(document, out var error) && error is not null) Report(error);
    }

    /// <summary>
    ///     Tells the user about entries that will silence nothing. Left to the log alone, a typo
    ///     here is invisible: the diagnostic simply stays on screen with no explanation.
    /// </summary>
    private void ReportUnreadableEntries(SuppressionsDocument.Payload document)
    {
        var bad = document.Entries
            .Where(e => !SuppressionMatcher.TryParse(e.Id, out _))
            .Select(e => $"'{e.Id}'")
            .ToList();

        if (bad.Count == 0) return;

        foreach (var id in bad) _logger.LogWarning("Ignoring unreadable suppression id {Id}", id);

        Report($"suppressions.json has {(bad.Count == 1 ? "an entry that is not" : "entries that are not")} "
               + $"a diagnostic id: {string.Join(", ", bad)}. "
               + $"{(bad.Count == 1 ? "It suppresses" : "They suppress")} nothing. "
               + $"Expected aetswg-<group>-<number> or aetswg-<group>-*{WhereToLook()}");
    }

    private string WhereToLook()
    {
        var path = _locator.TryLocate("suppressions.json");
        return path is null ? "." : $" See '{path}'.";
    }

    private void Report(string message)
    {
        _notifier?.ShowError(message);
    }
}
