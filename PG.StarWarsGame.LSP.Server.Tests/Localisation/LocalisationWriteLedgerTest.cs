// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class LocalisationWriteLedgerTest
{
    private const string Path = "/mod/data/text/MasterTextFile.csv";

    private static LocalisationWriteLedger Build()
    {
        return new LocalisationWriteLedger(new FileHelper(new MockFileSystem()));
    }

    [Fact]
    public void UnrecordedFile_IsNotAnEcho()
    {
        Assert.False(Build().ConsumeEcho(Path, "hash-1"));
    }

    [Fact]
    public void FileHoldingWhatTheServerWrote_IsAnEcho()
    {
        var ledger = Build();
        ledger.Record(Path, "hash-1");

        Assert.True(ledger.ConsumeEcho(Path, "hash-1"));
    }

    // The whole point of matching on content: an edit that landed between the server's write and
    // the watcher event must still be reloaded.
    [Fact]
    public void FileChangedSinceTheServerWroteIt_IsNotAnEcho()
    {
        var ledger = Build();
        ledger.Record(Path, "hash-1");

        Assert.False(ledger.ConsumeEcho(Path, "hash-2"));
    }

    // One write produces one watcher event. A second event for the same file is a real change, and
    // treating it as the same echo would swallow it.
    [Fact]
    public void EchoIsConsumed_SoTheNextChangeIsNotSwallowed()
    {
        var ledger = Build();
        ledger.Record(Path, "hash-1");

        Assert.True(ledger.ConsumeEcho(Path, "hash-1"));
        Assert.False(ledger.ConsumeEcho(Path, "hash-1"));
    }

    [Fact]
    public void RecordsArePerFile()
    {
        var ledger = Build();
        ledger.Record(Path, "hash-1");

        Assert.False(ledger.ConsumeEcho("/mod/data/text/creditstext.csv", "hash-1"));
    }

    // Watcher events and request parameters describe the same file with different separators and
    // casing; a raw-string key would miss the match and let the redundant reload through.
    [Fact]
    public void PathShapeDoesNotMatter()
    {
        var ledger = Build();
        ledger.Record(@"C:\mod\data\text\MasterTextFile.csv", "hash-1");

        Assert.True(ledger.ConsumeEcho(@"c:\mod\data\text\mastertextfile.csv", "hash-1"));
    }
}
