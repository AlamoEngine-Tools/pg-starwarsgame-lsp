// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

public sealed class ModProjectFileWriterTest
{
    private const string Path = "/workspace/mymod.pgproj";

    [Fact]
    public async Task SetLocalisationAsync_NoExistingNode_AddsLocalisationNode()
    {
        const string json = """
                            {
                              "name": "My Mod",
                              "directories": { "xml": ["data/xml"] }
                            }
                            """;
        var (writer, fs) = Build(json);

        await writer.SetLocalisationAsync(Path, "CSV", "data/text", CancellationToken.None);

        var root = JsonDocument.Parse(fs.File.ReadAllText(Path)).RootElement;
        var loc = root.GetProperty("localisation");
        Assert.Equal("CSV", loc.GetProperty("type").GetString());
        Assert.Equal("data/text", loc.GetProperty("directory").GetString());
    }

    [Fact]
    public async Task SetLocalisationAsync_PreservesOtherTopLevelProperties()
    {
        const string json = """
                            {
                              "name": "My Mod",
                              "modinfo": { "name": "Modinfo Name", "version": "1.0.0" },
                              "directories": { "xml": ["data/xml"] },
                              "projectReferences": [ { "path": "../base/base.pgproj" } ]
                            }
                            """;
        var (writer, fs) = Build(json);

        await writer.SetLocalisationAsync(Path, "CSV", "data/text", CancellationToken.None);

        var root = JsonDocument.Parse(fs.File.ReadAllText(Path)).RootElement;
        Assert.Equal("My Mod", root.GetProperty("name").GetString());
        Assert.Equal("Modinfo Name", root.GetProperty("modinfo").GetProperty("name").GetString());
        Assert.Equal(["data/xml"], root.GetProperty("directories").GetProperty("xml")
            .EnumerateArray().Select(e => e.GetString()));
        Assert.Single(root.GetProperty("projectReferences").EnumerateArray());
    }

    [Fact]
    public async Task SetLocalisationAsync_ExistingNode_IsReplaced()
    {
        const string json = """
                            {
                              "name": "My Mod",
                              "localisation": { "type": "XML", "directory": "old/dir" }
                            }
                            """;
        var (writer, fs) = Build(json);

        await writer.SetLocalisationAsync(Path, "NLS", "data/text", CancellationToken.None);

        var root = JsonDocument.Parse(fs.File.ReadAllText(Path)).RootElement;
        var loc = root.GetProperty("localisation");
        Assert.Equal("NLS", loc.GetProperty("type").GetString());
        Assert.Equal("data/text", loc.GetProperty("directory").GetString());
    }

    [Fact]
    public async Task SetLocalisationAsync_CommentsAndTrailingCommas_ParsedButNotPreserved()
    {
        // Documented limitation: JsonNode has no concept of comments, so a round trip through the
        // writer silently drops them. The write must still succeed rather than throw.
        const string json = """
                            {
                              // a comment
                              "name": "My Mod",
                              "directories": { "xml": ["data/xml"], },
                            }
                            """;
        var (writer, fs) = Build(json);

        await writer.SetLocalisationAsync(Path, "CSV", "data/text", CancellationToken.None);

        var written = fs.File.ReadAllText(Path);
        Assert.DoesNotContain("// a comment", written);
        var root = JsonDocument.Parse(written).RootElement;
        Assert.Equal("My Mod", root.GetProperty("name").GetString());
    }

    private static (ModProjectFileWriter Writer, MockFileSystem Fs) Build(string json)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(json) });
        var writer = new ModProjectFileWriter(new FileHelper(fs));
        return (writer, fs);
    }
}
