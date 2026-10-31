// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Commands;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Commands;

public sealed class NewModProjectCommandHandlerTest
{
    [Fact]
    public async Task CreatesExpectedDirectoryTree()
    {
        var fs = new MockFileSystem();
        var handler = Build(fs);

        await handler.Handle(Params("My Mod", "/mods/mymod"), CancellationToken.None);

        Assert.True(fs.Directory.Exists("/mods/mymod"));
        Assert.True(fs.Directory.Exists("/mods/mymod/Data/XML"));
        Assert.True(fs.Directory.Exists("/mods/mymod/Data/Scripts"));
        Assert.True(fs.Directory.Exists("/mods/mymod/Data/Scripts/Story"));
        Assert.True(fs.Directory.Exists("/mods/mymod/Data/Scripts/Library"));
        Assert.True(fs.Directory.Exists("/mods/mymod/Data/Art"));
        Assert.True(fs.Directory.Exists("/mods/mymod/Data/Art/Models"));
        Assert.True(fs.Directory.Exists("/mods/mymod/Data/Art/Textures"));
        Assert.True(fs.Directory.Exists("/mods/mymod/Data/Audio"));
        Assert.True(fs.Directory.Exists("/mods/mymod/Data/Audio/Music"));
        Assert.True(fs.Directory.Exists("/mods/mymod/Data/Audio/SFX"));
        Assert.True(fs.Directory.Exists("/mods/mymod/Data/Text"));
    }

    [Fact]
    public async Task CreatesValidPgprojFile()
    {
        var fs = new MockFileSystem();
        var handler = Build(fs);

        await handler.Handle(Params("My Mod", "/mods/mymod"), CancellationToken.None);

        const string pgprojPath = "/mods/mymod/My_Mod.pgproj";
        Assert.True(fs.File.Exists(pgprojPath));

        var loader = new ModProjectLoader(new FileHelper(fs), new ListLogger<ModProjectLoader>(),
            new NullPgprojMigrationSink(), PgprojMigrations.All, ResxMessageCatalog.Server("en"));
        var model = loader.Load(pgprojPath);
        Assert.Equal("My Mod", model.Modinfo!.Name);
        Assert.Equal(new[] { "data/xml" }, model.Directories.Xml);
        Assert.Equal("CSV", model.Localisation!.Type);
        Assert.Equal("data/text", model.Localisation.Directory);
    }

    // A project we create says what it is and which format it is in, like every other document the
    // extension writes. Absent still means version one, so this is not required for the file to
    // load - it is what makes the NEXT format change able to tell the two apart.
    [Fact]
    public async Task NewProject_IsStampedWithItsTypeAndVersion()
    {
        var fs = new MockFileSystem();
        var handler = Build(fs);

        await handler.Handle(Params("My Mod", "/mods/mymod"), CancellationToken.None);

        var written = JsonNode.Parse(fs.File.ReadAllText("/mods/mymod/My_Mod.pgproj"))!.AsObject();
        Assert.Equal(PgprojFormat.TypeName, (string?)written["_type"]);
        Assert.Equal(PgprojFormat.Current.ToString(), (string?)written["_typeVersion"]);
    }

    [Fact]
    public async Task CreatesModinfoJson()
    {
        var fs = new MockFileSystem();
        var handler = Build(fs);

        await handler.Handle(Params("My Mod", "/mods/mymod"), CancellationToken.None);

        const string modinfoPath = "/mods/mymod/modinfo.json";
        Assert.True(fs.File.Exists(modinfoPath));

        using var doc = JsonDocument.Parse(fs.File.ReadAllText(modinfoPath));
        Assert.Equal("My Mod", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task MissingName_NoFilesCreated()
    {
        var fs = new MockFileSystem();
        var handler = Build(fs);

        await handler.Handle(Params("", "/mods/mymod"), CancellationToken.None);

        Assert.False(fs.File.Exists("/mods/mymod/.pgproj"));
        Assert.Empty(fs.AllFiles);
    }

    /// <summary>
    ///     Run inside an existing mod, the command adds a project file and leaves the mod's own
    ///     modinfo.json - its dependencies included - as it was.
    /// </summary>
    [Fact]
    public async Task ExistingModinfo_IsLeftAsItWas()
    {
        const string modinfo = """{ "name": "Old Name", "version": "2.3", "dependencies": [ "123456" ] }""";
        var fs = new MockFileSystem();
        fs.AddFile("/mods/mymod/modinfo.json", new MockFileData(modinfo));
        var handler = Build(fs);

        await handler.Handle(Params("My Mod", "/mods/mymod"), CancellationToken.None);

        Assert.True(fs.File.Exists("/mods/mymod/My_Mod.pgproj"));
        Assert.Equal(modinfo, fs.File.ReadAllText("/mods/mymod/modinfo.json"));
    }

    [Fact]
    public async Task MissingPath_NoFilesCreated()
    {
        var fs = new MockFileSystem();
        var handler = Build(fs);

        await handler.Handle(Params("My Mod", ""), CancellationToken.None);

        Assert.Empty(fs.AllFiles);
    }

    [Fact]
    public async Task ExistingPgproj_NoOverwrite()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/mods/mymod/My_Mod.pgproj", new MockFileData("existing"));
        var handler = Build(fs);

        await handler.Handle(Params("My Mod", "/mods/mymod"), CancellationToken.None);

        Assert.Equal("existing", fs.File.ReadAllText("/mods/mymod/My_Mod.pgproj"));
    }

    [Fact]
    public async Task NameSanitization()
    {
        var fs = new MockFileSystem();
        var handler = Build(fs);

        await handler.Handle(Params("My Awesome Mod!", "/mods/mymod"), CancellationToken.None);

        const string pgprojPath = "/mods/mymod/My_Awesome_Mod.pgproj";
        Assert.True(fs.File.Exists(pgprojPath));

        var loader = new ModProjectLoader(new FileHelper(fs), new ListLogger<ModProjectLoader>(),
            new NullPgprojMigrationSink(), PgprojMigrations.All, ResxMessageCatalog.Server("en"));
        var model = loader.Load(pgprojPath);
        Assert.Equal("My Awesome Mod!", model.Modinfo!.Name);
    }

    private static NewModProjectCommandHandler Build(MockFileSystem fs)
    {
        return Build(fs, new RecordingUserNotifier());
    }

    private static NewModProjectCommandHandler Build(MockFileSystem fs, RecordingUserNotifier notifier)
    {
        var fileHelper = new FileHelper(fs);
        return new NewModProjectCommandHandler(fileHelper, new ModLayout(fileHelper), notifier,
            ResxMessageCatalog.Server("en"), new ListLogger<NewModProjectCommandHandler>());
    }

    // The client announced "created" whenever the command returned, and a refusal returned like a
    // success: a project file that already existed was reported as a new project.
    [Fact]
    public async Task AnExistingProjectFile_IsReportedAsTheRefusal_NotAsCreated()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/mods/mymod/My_Mod.pgproj", new MockFileData("existing"));
        var notifier = new RecordingUserNotifier();

        await Build(fs, notifier).Handle(Params("My Mod", "/mods/mymod"), CancellationToken.None);

        var error = Assert.Single(notifier.Errors);
        Assert.Contains("My_Mod.pgproj", error);
        Assert.Empty(notifier.Infos);
    }

    [Fact]
    public async Task ACreatedProject_IsAnnounced_WithWhereItIs()
    {
        var fs = new MockFileSystem();
        var notifier = new RecordingUserNotifier();

        await Build(fs, notifier).Handle(Params("My Mod", "/mods/mymod"), CancellationToken.None);

        var info = Assert.Single(notifier.Infos);
        Assert.Contains("My Mod", info);
        Assert.Empty(notifier.Errors);
    }

    // The adopt handler refuses any project file in the folder; this one refused only a project file
    // of the same name, and wrote a second one beside a differently named one - then announced it.
    [Fact]
    public async Task ADifferentlyNamedProjectFile_IsRefused_AndNothingWritten()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/mods/mymod/Other.pgproj", new MockFileData("existing"));
        var notifier = new RecordingUserNotifier();

        await Build(fs, notifier).Handle(Params("My Mod", "/mods/mymod"), CancellationToken.None);

        Assert.False(fs.File.Exists("/mods/mymod/My_Mod.pgproj"));
        Assert.False(fs.File.Exists("/mods/mymod/modinfo.json"));
        var error = Assert.Single(notifier.Errors);
        Assert.Contains("Other.pgproj", error);
        Assert.Empty(notifier.Infos);
    }

    // The game's own install folder is not a mod; the adopt handler knew, this one did not.
    [Theory]
    [InlineData("swfoc.exe")]
    [InlineData("SWEAW.EXE")]
    public async Task TheGamesOwnFolder_IsRefused_AndNothingWritten(string executable)
    {
        var fs = new MockFileSystem();
        fs.AddFile($"/game/{executable}", new MockFileData(string.Empty));
        var notifier = new RecordingUserNotifier();

        await Build(fs, notifier).Handle(Params("My Mod", "/game"), CancellationToken.None);

        Assert.Empty(fs.Directory.GetFiles("/game", "*.pgproj"));
        Assert.False(fs.File.Exists("/game/modinfo.json"));
        Assert.False(fs.Directory.Exists("/game/Data"));
        var error = Assert.Single(notifier.Errors);
        Assert.Contains("/game", error.Replace('\\', '/'));
        Assert.Empty(notifier.Infos);
    }

    // Three refusals logged a warning and returned like a success; the client, which no longer
    // announces anything itself, showed nothing.
    [Fact]
    public async Task AMissingName_IsAnnounced()
    {
        var notifier = new RecordingUserNotifier();

        await Build(new MockFileSystem(), notifier).Handle(Params("  ", "/mods/mymod"), CancellationToken.None);

        Assert.Single(notifier.Errors);
        Assert.Empty(notifier.Infos);
    }

    [Fact]
    public async Task AMissingPath_IsAnnounced()
    {
        var notifier = new RecordingUserNotifier();

        await Build(new MockFileSystem(), notifier).Handle(Params("My Mod", ""), CancellationToken.None);

        Assert.Single(notifier.Errors);
        Assert.Empty(notifier.Infos);
    }

    [Fact]
    public async Task MissingArguments_AreAnnounced()
    {
        var notifier = new RecordingUserNotifier();

        await Build(new MockFileSystem(), notifier)
            .Handle(new ExecuteCommandParams { Arguments = new JArray() }, CancellationToken.None);

        Assert.Single(notifier.Errors);
        Assert.Empty(notifier.Infos);
    }

    // An existing mod keeps its own modinfo.json and gets a project file; a plain folder gets both.
    // The refusals above must not take these two paths with them.
    [Fact]
    public async Task AFolderLaidOutLikeAMod_WithoutAProjectFile_GetsOne()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/mods/mymod/Data/XML/a.xml", new MockFileData(string.Empty));
        var notifier = new RecordingUserNotifier();

        await Build(fs, notifier).Handle(Params("My Mod", "/mods/mymod"), CancellationToken.None);

        Assert.True(fs.File.Exists("/mods/mymod/My_Mod.pgproj"));
        Assert.Single(notifier.Infos);
        Assert.Empty(notifier.Errors);
    }

    private static ExecuteCommandParams Params(string name, string path)
    {
        return new ExecuteCommandParams
        {
            Arguments = new JArray(JObject.FromObject(new { name, path }))
        };
    }

    // ReSharper disable NotAccessedPositionalProperty.Local - captured for the debugger, counted by the tests
    private sealed record LogEntry(LogLevel Level, string Message);
    // ReSharper restore NotAccessedPositionalProperty.Local

    private sealed class ListLogger<T> : ILogger<T>
    {
        // ReSharper disable once CollectionNeverQueried.Local - captured for the debugger
        public ConcurrentBag<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}