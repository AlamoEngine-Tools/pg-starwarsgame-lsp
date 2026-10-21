// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis.Instances;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis.Instances;

/// <summary>
///     The analyzer reads one instance's files under a folder of the instance's own, where nothing
///     else is: every real file has a place there, and every place leads back to its file.
/// </summary>
public sealed class LuaInstanceMirrorTest
{
    private const string Root = "C:/cache/instance-1/root";
    private const string Script = "file:///D:/Mods/MyMod/Data/Scripts/GameObject/Interdictor.lua";
    private const string Base = "file:///C:/Games/FoC/Data/Scripts/Library/PGBase.lua";
    private const string Stub = "file:///C:/schema/lua/api.d.lua";

    private static LuaInstanceMirror Mirror()
    {
        return new LuaInstanceMirror(Root, ["d:/mods/mymod/data/scripts", "c:/games/foc/data/scripts"],
            new FileHelper(new MockFileSystem()));
    }

    [Fact]
    public void AFile_KeepsItsPathBelowItsLayersScriptRoot()
    {
        var mirror = Mirror();

        Assert.Equal("file:///C:/cache/instance-1/root/GameObject/Interdictor.lua", mirror.Add(Script));
        Assert.Equal("file:///C:/cache/instance-1/root/Library/PGBase.lua", mirror.Add(Base));
    }

    [Fact]
    public void TheMirroredPlace_LeadsBack_InAnySpellingOfTheUri()
    {
        var mirror = Mirror();
        mirror.Add(Base);

        Assert.Equal(Base, mirror.RealOf("file:///c%3A/cache/instance-1/root/library/pgbase.lua"));
        Assert.Null(mirror.RealOf(Stub));
    }

    [Fact]
    public void TwoFilesOfOnePlace_AreKeptApart()
    {
        var mirror = new LuaInstanceMirror(Root, ["d:/mods/mymod/data/scripts", "c:/games/foc/data/scripts"],
            new FileHelper(new MockFileSystem()));

        var leaf = mirror.Add("file:///D:/Mods/MyMod/Data/Scripts/Library/PGBase.lua");
        var vanilla = mirror.Add(Base);

        Assert.NotEqual(leaf, vanilla);
        Assert.Equal(Base, mirror.RealOf(vanilla));
    }

    [Fact]
    public void AFileOutsideEveryScriptRoot_StillHasAPlace()
    {
        var mirror = Mirror();

        var place = mirror.Add("file:///E:/elsewhere/Loose.lua");

        Assert.StartsWith("file:///C:/cache/instance-1/root/", place);
        Assert.Equal("file:///E:/elsewhere/Loose.lua", mirror.RealOf(place));
    }

    [Fact]
    public void ARequest_IsMirrored_AndItsAnswer_LeadsBack_UrisAsValuesAndAsEditKeys()
    {
        var mirror = Mirror();
        var placeOfScript = mirror.Add(Script);
        var placeOfBase = mirror.Add(Base);

        var request = JObject.FromObject(new { textDocument = new { uri = Script }, other = Stub });
        mirror.ToMirror(request);
        Assert.Equal(placeOfScript, request["textDocument"]!["uri"]!.Value<string>());
        Assert.Equal(Stub, request["other"]!.Value<string>());

        var answer = new JObject
        {
            ["location"] = new JObject { ["uri"] = placeOfBase.ToLowerInvariant() },
            ["changes"] = new JObject { [placeOfScript] = new JArray() }
        };
        mirror.ToReal(answer);
        Assert.Equal(Base, answer["location"]!["uri"]!.Value<string>());
        Assert.Equal(Script, ((JObject)answer["changes"]!).Properties().Single().Name);
    }

    [Fact]
    public void AFileRemoved_HasNoPlaceAnyMore()
    {
        var mirror = Mirror();
        var place = mirror.Add(Base);

        mirror.Remove(Base);

        Assert.Null(mirror.RealOf(place));
        Assert.Null(mirror.PlaceOf(Base));
    }
}
