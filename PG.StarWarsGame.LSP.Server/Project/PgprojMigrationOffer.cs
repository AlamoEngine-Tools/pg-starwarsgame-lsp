// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     Offers to write a migrated project file back, and does it only if the user agrees.
///     <para>
///         The annoying option, chosen on purpose. A quiet command leaves people unaware and
///         un-upgraded, and the prompt is the one place we can say what changed and name anything
///         they have to do themselves - which is what each migration's notice is for.
///     </para>
///     <para>
///         Asked once per project per session: reload runs on every <c>.pgproj</c> change, and a
///         question repeated on every keystroke is one people dismiss without reading.
///     </para>
/// </summary>
public sealed class PgprojMigrationOffer(
    IFileHelper fileHelper, IPgprojMigrationPrompt prompt, IUserNotifier notifier,
    ILogger<PgprojMigrationOffer> logger)
    : IPgprojMigrationSink
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Projects already asked about, so a reload does not ask again.</summary>
    private readonly ConcurrentDictionary<string, byte> _asked = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentQueue<Pending> _pending = new();

    public void Migrated(string pgprojPath, JsonNode migrated, IReadOnlyList<string> notices)
    {
        // Queued rather than asked here: this runs inside a load, during startup or a reload, where
        // a question is either ignored or asked several times over.
        _pending.Enqueue(new Pending(pgprojPath, migrated, notices));
    }

    /// <summary>Asks about everything migrated since the last call. Never throws.</summary>
    public async Task OfferPendingAsync(CancellationToken ct)
    {
        while (_pending.TryDequeue(out var pending))
        {
            if (!_asked.TryAdd(pending.Path, 0)) continue;

            try
            {
                await OfferAsync(pending, ct);
            }
            catch (Exception ex)
            {
                // The project is already migrated in memory and everything works; failing to offer
                // to persist it must never take a workspace down.
                logger.LogWarning(ex, "Could not offer to update '{Path}'", pending.Path);
            }
        }
    }

    private async Task OfferAsync(Pending pending, CancellationToken ct)
    {
        var fs = fileHelper.FileSystem;
        var fileName = fs.Path.GetFileName(pending.Path);
        var proposed = pending.Migrated.ToJsonString(WriteOptions);

        // The user sees the two files side by side rather than a sentence about them: this rewrites
        // something they own and keep in version control, so what exactly changes is the only
        // useful thing to show.
        var accepted = await prompt.ProposeAsync(
            new PgprojMigrationProposal(pending.Path, fileName, proposed, pending.Notices), ct);

        if (!accepted) return;

        // The backup goes first, and the write only happens once it is there: the comments about to
        // be lost are the reason there is a backup at all.
        var backup = pending.Path + ".bak";
        fs.File.Copy(pending.Path, backup, true);
        await fs.File.WriteAllTextAsync(pending.Path, proposed, ct);

        notifier.ShowInfo($"'{fileName}' updated. The previous file is at '{fs.Path.GetFileName(backup)}'.");
    }

    private sealed record Pending(string Path, JsonNode Migrated, IReadOnlyList<string> Notices);
}
