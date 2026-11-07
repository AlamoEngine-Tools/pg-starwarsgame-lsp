// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Nodes;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Project;
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

    /// <summary>
    ///     A hand-written <c>credits</c> sub-node survives a repoint.
    ///     <para>
    ///         Nothing in the server ever writes that node - it exists so a mod whose credits files
    ///         deviate from the naming convention can say so - and this writer replaced the whole
    ///         localisation node, so an import or a format conversion silently undid the only way to
    ///         configure it.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task SetLocalisationAsync_ExistingCreditsSettings_AreKept()
    {
        const string json = """
                            {
                              "name": "My Mod",
                              "localisation": {
                                "type": "CSV",
                                "directory": "old/dir",
                                "credits": { "detection": "explicit", "files": ["crawl.csv"] }
                              }
                            }
                            """;
        var (writer, fs) = Build(json);

        await writer.SetLocalisationAsync(Path, "NLS", "data/text", CancellationToken.None);

        var localisation = JsonDocument.Parse(fs.File.ReadAllText(Path))
            .RootElement.GetProperty("localisation");

        Assert.Equal("NLS", localisation.GetProperty("type").GetString());
        Assert.Equal("data/text", localisation.GetProperty("directory").GetString());

        var credits = localisation.GetProperty("credits");
        Assert.Equal("explicit", credits.GetProperty("detection").GetString());
        Assert.Equal("crawl.csv", credits.GetProperty("files")[0].GetString());
    }

    [Fact]
    public async Task SetLocalisationAsync_ExistingNode_TypeAndDirectoryAreOverwritten()
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

    // A patch changes the node it was asked to change and claims no format the file does not
    // follow: bringing a file forward is the migration offer's, which says what it changes - an EaW
    // mod stamped as current would have been given foc without being told.
    [Theory]
    [InlineData("""{ "name": "Mod" }""", null)]
    [InlineData("""{ "_type": "aetswg.ModProject", "_typeVersion": "aetswg-1.0.0", "name": "Mod" }""", "aetswg-1.0.0")]
    public async Task SetLocalisation_KeepsTheFormatTheFileDeclares(string project, string? version)
    {
        var fs = new MockFileSystem();
        fs.AddFile(Path, new MockFileData(project));

        await new ModProjectFileWriter(new FileHelper(fs))
            .SetLocalisationAsync(Path, "CSV", "data/text", CancellationToken.None);

        var written = JsonNode.Parse(fs.File.ReadAllText(Path))!.AsObject();
        Assert.Equal(version, (string?)written["_typeVersion"]);
        Assert.Null(written["game"]);
        var model = new ModProjectLoader(new FileHelper(fs),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ModProjectLoader>.Instance,
                new NullPgprojMigrationSink(), PgprojMigrations.All, ResxMessageCatalog.Server("en"))
            .Load(Path);
        Assert.Equal(TargetGame.FOC, model.Game);
        Assert.Equal("data/text", model.Localisation!.Directory);
    }
}
