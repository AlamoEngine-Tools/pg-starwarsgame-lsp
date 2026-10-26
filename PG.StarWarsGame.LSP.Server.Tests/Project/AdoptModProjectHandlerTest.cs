// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

/// <summary>
///     A project file for a mod that has none, written from what the folder holds (#295): only the
///     directories that exist, the text format found, the name from the mod's own modinfo.json.
///     Nothing is created and modinfo.json is never written.
/// </summary>
public sealed class AdoptModProjectHandlerTest
{
    private const string Root = "/mods/legacy";

    private static AdoptModProjectHandler Build(MockFileSystem fs)
    {
        return new AdoptModProjectHandler(new FileHelper(fs), ResxMessageCatalog.Server("en"));
    }

    private static Task<AdoptModProjectResult> Adopt(MockFileSystem fs)
    {
        return Build(fs).Handle(new AdoptModProjectParams(Root), TestContext.Current.CancellationToken);
    }

    private static MockFileSystem Mod(params string[] files)
    {
        var fs = new MockFileSystem();
        foreach (var file in files)
            fs.AddFile($"{Root}/{file}", new MockFileData(string.Empty));
        return fs;
    }

    private static JsonObject Written(MockFileSystem fs, AdoptModProjectResult result)
    {
        Assert.Null(result.Error);
        Assert.NotNull(result.ProjectFile);
        return JsonNode.Parse(fs.File.ReadAllText(result.ProjectFile!))!.AsObject();
    }

    [Fact]
    public async Task OnlyTheDirectoriesThatExist_AreListed_AndNoneIsCreated()
    {
        var fs = Mod("Data/XML/GameObjectFiles.xml", "Data/Scripts/Library/lib.lua", "Data/Art/Models/a.alo");

        var written = Written(fs, await Adopt(fs));

        var directories = written["directories"]!.AsObject();
        Assert.Equal("Data/XML", (string?)directories["xml"]![0]);
        Assert.Equal("Data/Scripts", (string?)directories["scripts"]![0]);
        Assert.Equal("Data/Art", (string?)directories["art"]![0]);
        Assert.False(directories.ContainsKey("audio"));
        Assert.False(fs.Directory.Exists($"{Root}/Data/Audio"));
        Assert.Null(written["localisation"]);
    }

    [Fact]
    public async Task TheDirectories_AreSpelledAsOnDisk()
    {
        var fs = Mod("data/xml/a.xml", "data/Scripts/a.lua");

        var directories = Written(fs, await Adopt(fs))["directories"]!.AsObject();

        Assert.Equal("data/xml", (string?)directories["xml"]![0]);
        Assert.Equal("data/Scripts", (string?)directories["scripts"]![0]);
    }

    [Theory]
    [InlineData("Data/Text/MasterTextFile.csv", "CSV")]
    [InlineData("Data/Text/mastertextfile_english.dat", "DAT")]
    public async Task TheTextFormat_IsTheOneFound(string textFile, string type)
    {
        var fs = Mod("Data/XML/a.xml", textFile);

        var localisation = Written(fs, await Adopt(fs))["localisation"]!.AsObject();

        Assert.Equal(type, (string?)localisation["type"]);
        Assert.Equal("Data/Text", (string?)localisation["directory"]);
    }

    /// <summary>The csv is what a mod compiles its dat from, so with both the csv is the source.</summary>
    [Fact]
    public async Task WithBothFormats_TheCsvIsTheSource()
    {
        var fs = Mod("Data/XML/a.xml", "Data/Text/MasterTextFile.csv", "Data/Text/mastertextfile_english.dat");

        Assert.Equal("CSV", (string?)Written(fs, await Adopt(fs))["localisation"]!["type"]);
    }

    [Fact]
    public async Task StoryDialog_IsListedWhereTheStoryScriptsHoldDialog()
    {
        var fs = Mod("Data/XML/a.xml", "Data/Scripts/Story/Dialog_Intro.txt");

        var directories = Written(fs, await Adopt(fs))["directories"]!.AsObject();

        Assert.Equal("Data/Scripts/Story", (string?)directories["storyDialog"]![0]);
    }

    [Fact]
    public async Task TheName_IsTheModsOwn_AndModinfoIsLeftAsItWas()
    {
        const string modinfo = """{ "name": "Thrawn's Revenge", "dependencies": [ "1125571106" ] }""";
        var fs = Mod("Data/XML/a.xml");
        fs.AddFile($"{Root}/modinfo.json", new MockFileData(modinfo));

        var result = await Adopt(fs);

        Assert.Equal("Thrawn's Revenge", (string?)Written(fs, result)["name"]);
        Assert.EndsWith("Thrawns_Revenge.pgproj", result.ProjectFile);
        Assert.Equal(modinfo, fs.File.ReadAllText($"{Root}/modinfo.json"));
    }

    [Fact]
    public async Task WithoutModinfo_TheNameIsTheFolders()
    {
        var fs = Mod("Data/XML/a.xml");

        var result = await Adopt(fs);

        Assert.Equal("legacy", (string?)Written(fs, result)["name"]);
        Assert.Equal($"{Root}/legacy.pgproj", result.ProjectFile!.Replace('\\', '/'));
    }

    [Fact]
    public async Task TheFile_IsStampedAndLoads()
    {
        var fs = Mod("Data/XML/a.xml", "Data/Text/MasterTextFile.csv");

        var result = await Adopt(fs);
        var written = Written(fs, result);

        Assert.Equal(PgprojFormat.TypeName, (string?)written["_type"]);
        Assert.Equal(PgprojFormat.Current.ToString(), (string?)written["_typeVersion"]);
        var model = new ModProjectLoader(new FileHelper(fs),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ModProjectLoader>.Instance).Load(result.ProjectFile!);
        Assert.Equal("legacy", model.Name);
        Assert.Equal(["Data/XML"], model.Directories.Xml);
        Assert.Empty(model.ProjectReferences);
    }

    [Fact]
    public async Task AFolderWithAProject_IsRefused_AndNothingWritten()
    {
        var fs = Mod("Data/XML/a.xml", "existing.pgproj");

        var result = await Adopt(fs);

        Assert.Null(result.ProjectFile);
        Assert.NotNull(result.Error);
        Assert.Single(fs.Directory.GetFiles(Root, "*.pgproj"));
    }

    [Fact]
    public async Task AFolderNotLaidOutLikeAMod_IsRefused()
    {
        var fs = Mod("notes/todo.xml");

        var result = await Adopt(fs);

        Assert.Null(result.ProjectFile);
        Assert.NotNull(result.Error);
        Assert.Empty(fs.Directory.GetFiles(Root, "*.pgproj"));
    }

    // The client offers a project for every folder laid out like a mod; one it offers and this
    // refuses was an offer that could only fail.
    [Theory]
    [InlineData("Data/Scripts/Library/lib.lua", "scripts")]
    [InlineData("Data/Art/Models/a.alo", "art")]
    [InlineData("Data/Audio/Speech/a.wav", "audio")]
    public async Task AModWithoutXml_IsAdopted_WithTheDirectoriesItHas(string file, string directory)
    {
        var fs = Mod(file);

        var directories = Written(fs, await Adopt(fs))["directories"]!.AsObject();

        Assert.True(directories.ContainsKey(directory));
        Assert.False(directories.ContainsKey("xml"));
    }

    [Fact]
    public async Task AModOfTextOrArchivesOnly_IsAdopted()
    {
        var text = Mod("Data/Text/MasterTextFile_English.dat");
        var packed = Mod("Data/Config.meg");

        Assert.Equal("DAT", (string?)Written(text, await Adopt(text))["localisation"]!["type"]);
        Assert.Empty(Written(packed, await Adopt(packed))["directories"]!.AsObject());
    }

    [Fact]
    public async Task AFolderWithOnlyModinfo_IsAdopted_UnderTheModsName()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{Root}/modinfo.json", new MockFileData("""{ "name": "Legacy Mod" }"""));

        Assert.Equal("Legacy Mod", (string?)Written(fs, await Adopt(fs))["name"]);
    }

    [Theory]
    [InlineData("sweaw.exe")]
    [InlineData("StarWarsG.exe")]
    public async Task TheGamesOwnFolder_IsRefused(string executable)
    {
        var fs = Mod("Data/XML/a.xml", executable);

        var result = await Adopt(fs);

        Assert.Null(result.ProjectFile);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task AFolderWithABareXmlFolder_IsRefused()
    {
        var fs = Mod("xml/a.xml");

        Assert.Null((await Adopt(fs)).ProjectFile);
    }
}