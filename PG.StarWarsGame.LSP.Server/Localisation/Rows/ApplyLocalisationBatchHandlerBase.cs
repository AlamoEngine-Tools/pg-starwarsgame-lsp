// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Committing a staged batch of localisation edits, in the way that is the same for both kinds
///     of file.
///     <para>
///         The credits and translation handlers are separate endpoints on purpose - the two are
///         addressed differently, and one endpoint serving both is what made a single grid branch
///         on the file kind in dozens of places. What is <em>not</em> different is everything around
///         the edit: the feature gate, the path checks, the concurrency guard, the empty-batch
///         shortcut, reading the written text back for its hash, recording the write and reloading.
///         Both had spelled all of that out, and had already drifted - the comment explaining why
///         the hash comes from a read-back rather than from composed text survived on one copy only.
///     </para>
///     <para>
///         Writes to disk rather than sending <c>workspace/applyEdit</c>: the whole localisation
///         read path is disk-based, so an applyEdit would leave the file unsaved and make every
///         later read stale.
///     </para>
/// </summary>
/// <typeparam name="TCommand">How this endpoint addresses an edit - by row, or by key.</typeparam>
/// <typeparam name="TResult">The endpoint's own result record.</typeparam>
public abstract class ApplyLocalisationBatchHandlerBase<TCommand, TResult>
{
    private readonly ILspConfigurationProvider _config;
    private readonly IFileHelper _fileHelper;
    private readonly ILogger _logger;
    private readonly IModProjectReloadService _reloadService;
    private readonly ILocalisationWriteLedger _writeLedger;

    protected ApplyLocalisationBatchHandlerBase(
        IModProjectReloadService reloadService,
        IFileHelper fileHelper,
        ILogger logger,
        ILspConfigurationProvider config,
        ILocalisationWriteLedger writeLedger)
    {
        _reloadService = reloadService;
        _fileHelper = fileHelper;
        _logger = logger;
        _config = config;
        _writeLedger = writeLedger;
    }

    /// <summary>What to call this kind of file when a write fails, for the log line.</summary>
    protected abstract string FileKind { get; }

    /// <summary>
    ///     Applies the batch and writes it.
    ///     <para>
    ///         The editor owns the write because <c>.dat</c> is binary: there is no text for a
    ///         handler to save, and the DAT services write through a real file stream.
    ///     </para>
    /// </summary>
    protected abstract Task<LocalisationEditResult> ApplyAsync(
        string filePath, IReadOnlyList<TCommand> commands, CancellationToken ct);

    protected abstract TResult Succeeded(string newContentHash);

    protected abstract TResult Failed(int? failedIndex, string error);

    protected async Task<TResult> ApplyBatchAsync(
        string projectFilePath,
        string? expectedContentHash,
        IReadOnlyList<TCommand> commands,
        CancellationToken ct)
    {
        if (LocalisationFeatureDisabled.Rejection(_config) is { } rejection)
            return Failed(null, rejection);

        if (string.IsNullOrWhiteSpace(projectFilePath))
            return Failed(null, "No project file path provided.");

        var fs = _fileHelper.FileSystem;
        if (!fs.File.Exists(projectFilePath))
            return Failed(null, $"File not found: {projectFilePath}");

        // Checked once for the whole batch, before anything is composed: the batch is atomic, so
        // there is no point discovering a stale file halfway through.
        if (await LocalisationConcurrencyGuard.CheckAsync(
                fs, projectFilePath, expectedContentHash, ct) is { } stale)
            return Failed(null, stale.Error!);

        if (commands.Count == 0)
        {
            var unchanged = await fs.File.ReadAllTextAsync(projectFilePath, ct);
            return Succeeded(LocalisationContentHash.Compute(unchanged));
        }

        LocalisationEditResult result;
        try
        {
            result = await ApplyAsync(projectFilePath, commands, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write {FileKind} '{Path}'.", FileKind, projectFilePath);
            return Failed(null, $"Could not write the file: {ex.Message}");
        }

        if (!result.Success)
            return Failed(result.FailedIndex, result.Error!);

        // Read back rather than hashing composed text, because a binary format has no composed text
        // to hash. The concurrency guard reads the file the same way, so the two always agree.
        var written = await fs.File.ReadAllTextAsync(projectFilePath, ct);
        var writtenHash = LocalisationContentHash.Compute(written);

        // Recorded before the reload, so the watcher event for this write - which can arrive at any
        // point after it - is recognised as our own and does not reload everything a second time.
        _writeLedger.Record(projectFilePath, writtenHash);

        await _reloadService.ReloadLocalisationAsync(ct);

        return Succeeded(writtenHash);
    }
}
