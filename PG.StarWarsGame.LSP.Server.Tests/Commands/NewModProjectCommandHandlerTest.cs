// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
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

        var loader = new ModProjectLoader(new FileHelper(fs), new ListLogger<ModProjectLoader>());
        var model = loader.Load(pgprojPath);
        Assert.Equal("My Mod", model.Modinfo.Name);
        Assert.Equal(new[] { "data/xml" }, model.Directories.Xml);
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

        var loader = new ModProjectLoader(new FileHelper(fs), new ListLogger<ModProjectLoader>());
        var model = loader.Load(pgprojPath);
        Assert.Equal("My Awesome Mod!", model.Modinfo.Name);
    }

    private static NewModProjectCommandHandler Build(MockFileSystem fs)
    {
        return new NewModProjectCommandHandler(
            new FileHelper(fs), new ListLogger<NewModProjectCommandHandler>());
    }

    private static ExecuteCommandParams Params(string name, string path)
    {
        return new ExecuteCommandParams
        {
            Arguments = new JArray(JObject.FromObject(new { name, path }))
        };
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class ListLogger<T> : ILogger<T>
    {
        public ConcurrentBag<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

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
