// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

/// <summary>
///     The consented half of migrating a project file. The loader brings it forward in memory on
///     every read; this is the only thing that ever writes the result back, and only after asking.
/// </summary>
public sealed class PgprojMigrationOfferTest
{
    private const string Path = "/workspace/mymod.pgproj";
    private const string Original = """{ "_typeVersion": "aetswg-0.9.0", "renamedName": "Mod" }""";

    private static JsonObject Migrated()
    {
        return new JsonObject
        {
            ["_type"] = "aetswg.ModProject",
            ["_typeVersion"] = "aetswg-1.0.0",
            ["name"] = "Mod"
        };
    }

    private static (PgprojMigrationOffer Offer, MockFileSystem Fs, FakePrompt Prompt) Build(bool accept)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(Original) });
        var prompt = new FakePrompt { Accept = accept };
        return (new PgprojMigrationOffer(
            new FileHelper(fs), prompt, new RecordingUserNotifier(),
            NullLogger<PgprojMigrationOffer>.Instance), fs, prompt);
    }

    /// <summary>Answers the proposal, and keeps what the user would have been shown.</summary>
    private sealed class FakePrompt : IPgprojMigrationPrompt
    {
        public bool Accept { get; init; }
        public List<PgprojMigrationProposal> Proposals { get; } = [];

        public Task<bool> ProposeAsync(PgprojMigrationProposal proposal, CancellationToken ct)
        {
            Proposals.Add(proposal);
            return Task.FromResult(Accept);
        }
    }

    // ── declining ────────────────────────────────────────────────────────────

    // Nothing is asked until the workspace has settled, and nothing is written until the user says
    // so. A project file is theirs, in their repository, shared with people on other versions.
    [Fact]
    public void Migrated_AloneWritesNothing()
    {
        var (offer, fs, prompt) = Build(true);

        offer.Migrated(Path, Migrated(), []);

        Assert.Empty(prompt.Proposals);
        Assert.Equal(Original, fs.File.ReadAllText(Path));
    }

    [Fact]
    public async Task Declined_LeavesTheFileByteIdentical()
    {
        var (offer, fs, _) = Build(false);
        offer.Migrated(Path, Migrated(), []);

        await offer.OfferPendingAsync(CancellationToken.None);

        Assert.Equal(Original, fs.File.ReadAllText(Path));
        Assert.False(fs.File.Exists(Path + ".bak"));
    }

    // ── accepting ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Accepted_WritesTheMigratedProject()
    {
        var (offer, fs, _) = Build(true);
        offer.Migrated(Path, Migrated(), []);

        await offer.OfferPendingAsync(CancellationToken.None);

        var written = JsonNode.Parse(fs.File.ReadAllText(Path))!.AsObject();
        Assert.Equal("aetswg-1.0.0", (string?)written["_typeVersion"]);
        Assert.Equal("Mod", (string?)written["name"]);
    }

    // Writing costs the user their comments - System.Text.Json discards them - so the file as it
    // was has to survive somewhere they can get at it.
    [Fact]
    public async Task Accepted_BacksUpTheOriginalFirst()
    {
        var (offer, fs, _) = Build(true);
        offer.Migrated(Path, Migrated(), []);

        await offer.OfferPendingAsync(CancellationToken.None);

        Assert.Equal(Original, fs.File.ReadAllText(Path + ".bak"));
    }

    // ── what the proposal carries ────────────────────────────────────────────

    // The user is shown the file itself, not a description of it: the proposal carries the exact
    // text that would be written, which is what the client diffs against what is on disk.
    [Fact]
    public async Task TheProposal_CarriesTheFileAndTheTextThatWouldBeWritten()
    {
        var (offer, fs, prompt) = Build(false);
        offer.Migrated(Path, Migrated(), []);

        await offer.OfferPendingAsync(CancellationToken.None);

        var proposal = Assert.Single(prompt.Proposals);
        Assert.Equal(Path, proposal.Path);
        Assert.Equal("mymod.pgproj", proposal.FileName);
        Assert.Contains("aetswg-1.0.0", proposal.ProposedText, StringComparison.Ordinal);
        // ...and it is exactly what lands on disk when they accept.
        var (accepting, acceptingFs, _) = Build(true);
        accepting.Migrated(Path, Migrated(), []);
        await accepting.OfferPendingAsync(CancellationToken.None);
        Assert.Equal(proposal.ProposedText, acceptingFs.File.ReadAllText(Path));
    }

    // The proposal is the one place a migration can tell the user it needs something of them. A
    // step that requires a manual change and stays silent about it is worse than one that refuses.
    [Fact]
    public async Task TheProposal_CarriesEachMigrationsNotice()
    {
        var (offer, _, prompt) = Build(false);
        offer.Migrated(Path, Migrated(), ["Move your credits file under data/text."]);

        await offer.OfferPendingAsync(CancellationToken.None);

        Assert.Equal(
            "Move your credits file under data/text.", Assert.Single(Assert.Single(prompt.Proposals).Notices));
    }

    // ── asking once ──────────────────────────────────────────────────────────

    // Reload runs on every .pgproj change. Asking again each time is how a prompt becomes something
    // people dismiss without reading.
    [Fact]
    public async Task Declined_IsNotAskedAgainForTheSameProject()
    {
        var (offer, _, prompt) = Build(false);

        offer.Migrated(Path, Migrated(), []);
        await offer.OfferPendingAsync(CancellationToken.None);
        offer.Migrated(Path, Migrated(), []);
        await offer.OfferPendingAsync(CancellationToken.None);

        Assert.Single(prompt.Proposals);
    }
}
