// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A game object's <c>Lua_Script</c> is loaded the way the game loads it: the name goes into the
///     Lua search path verbatim, every script directory in order, <c>.lua</c> then <c>.lc</c>, and
///     the first file that opens wins.
/// </summary>
public sealed class LuaScriptReferenceHandlerTest
{
    private static readonly LuaScriptReferenceHandler Sut = new();

    private static readonly XmlTagDefinition LuaScriptTag =
        XmlHandlerTestFixtures.MakeTag("Lua_Script", XmlValueType.NameReference,
            referenceKind: ReferenceKind.WorkspaceFile) with
        {
            ReferenceTypeName = WorkspaceFileKey.LuaScriptType
        };

    private static XmlTagValueFact Fact(string value, string? owner = "SpaceUnit")
    {
        return new XmlTagValueFact("file:///test.xml", 3, 14, value.Length, LuaScriptTag, value, owner);
    }

    private static DiagnosticsContext CtxWith(params string[] paths)
    {
        var index = GameIndex.Empty with { AssetFiles = new MergedAssetFileIndex(paths) };
        return new DiagnosticsContext(new EmptySchemaProvider(), index, "file:///test.xml", "en");
    }

    // ── resolves ─────────────────────────────────────────────────────────────

    [Fact]
    public void Script_in_GameObject_is_silent()
    {
        Assert.Empty(Sut.Handle(Fact("BobaFettPlan"), CtxWith("data/scripts/gameobject/bobafettplan.lua")));
    }

    [Fact]
    public void A_compiled_script_counts()
    {
        Assert.Empty(Sut.Handle(Fact("BobaFettPlan"), CtxWith("data/scripts/gameobject/bobafettplan.lc")));
    }

    /// <summary>The name is pasted into the path as written, so a forward-slash sub-path resolves.</summary>
    [Fact]
    public void A_sub_path_below_GameObject_is_silent()
    {
        Assert.Empty(Sut.Handle(Fact("Heroes/BobaFettPlan"),
            CtxWith("data/scripts/gameobject/heroes/bobafettplan.lua")));
    }

    [Fact]
    public void A_bare_name_does_not_match_a_script_in_a_sub_directory()
    {
        var d = Assert.Single(Sut.Handle(Fact("BobaFettPlan"),
            CtxWith("data/scripts/gameobject/heroes/bobafettplan.lua")));

        Assert.Equal(DiagnosticIds.LuaScriptExistence, d.Id);
    }

    // ── missing ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_missing_script_is_an_error()
    {
        var d = Assert.Single(Sut.Handle(Fact("NoSuchPlan"), CtxWith("data/scripts/gameobject/bobafettplan.lua")));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Equal(DiagnosticIds.LuaScriptExistence, d.Id);
        Assert.Contains("'NoSuchPlan'", d.Message);
    }

    /// <summary>
    ///     A backslash sits inside a Lua string literal when the game loads the script, so it is read
    ///     as an escape and the path never reaches the file system as written.
    /// </summary>
    [Fact]
    public void A_backslash_is_an_error_even_when_the_slash_spelling_exists()
    {
        var d = Assert.Single(Sut.Handle(Fact(@"Heroes\BobaFettPlan"),
            CtxWith("data/scripts/gameobject/heroes/bobafettplan.lua")));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Equal(DiagnosticIds.LuaScriptBackslash, d.Id);
    }

    /// <summary>
    ///     A <c>..</c> resolves for a loose file on Windows only: an archive lookup keeps the segment
    ///     as written and never finds the file.
    /// </summary>
    [Fact]
    public void A_parent_directory_segment_is_an_error_even_when_the_target_exists()
    {
        var d = Assert.Single(Sut.Handle(Fact("../Story/BobaFettPlan"),
            CtxWith("data/scripts/story/bobafettplan.lua")));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Equal(DiagnosticIds.LuaScriptParentPath, d.Id);
    }

    /// <summary>The game appends the extension itself, so <c>X.lua</c> looks for <c>X.lua.lua</c>.</summary>
    [Theory]
    [InlineData("BobaFettPlan.lua")]
    [InlineData("BobaFettPlan.LC")]
    public void An_extension_is_an_error(string value)
    {
        var d = Assert.Single(Sut.Handle(Fact(value),
            CtxWith("data/scripts/gameobject/bobafettplan.lua", "data/scripts/gameobject/bobafettplan.lc")));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Equal(DiagnosticIds.LuaScriptExtension, d.Id);
    }

    // ── resolves outside GameObject ──────────────────────────────────────────

    [Theory]
    [InlineData("data/scripts/library/bobafettplan.lua")]
    [InlineData("data/scripts/story/bobafettplan.lua")]
    [InlineData("data/scripts/ai/landmode/bobafettplan.lua")]
    [InlineData("data/scripts/evaluators/bobafettplan.lc")]
    public void A_script_outside_GameObject_is_a_warning(string path)
    {
        var d = Assert.Single(Sut.Handle(Fact("BobaFettPlan"), CtxWith(path)));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(DiagnosticIds.LuaScriptOutsideGameObject, d.Id);
    }

    /// <summary>
    ///     Library is searched before GameObject, so a Library script of the same name is the one
    ///     the game loads.
    /// </summary>
    [Fact]
    public void A_Library_script_shadows_the_GameObject_one()
    {
        var d = Assert.Single(Sut.Handle(Fact("BobaFettPlan"),
            CtxWith("data/scripts/library/bobafettplan.lua", "data/scripts/gameobject/bobafettplan.lua")));

        Assert.Equal(DiagnosticIds.LuaScriptOutsideGameObject, d.Id);
        Assert.Contains("Data/Scripts/Library/BobaFettPlan.lua", d.Message);
        Assert.Contains("Data/Scripts/GameObject/BobaFettPlan.lua", d.Message);
    }

    /// <summary>GameObject comes before FreeStore, so the GameObject script wins and nothing is said.</summary>
    [Fact]
    public void A_GameObject_script_wins_over_a_later_directory()
    {
        Assert.Empty(Sut.Handle(Fact("BobaFettPlan"),
            CtxWith("data/scripts/freestore/bobafettplan.lua", "data/scripts/gameobject/bobafettplan.lua")));
    }

    // ── scope ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_story_manifest_script_is_not_checked_here()
    {
        Assert.Empty(Sut.Handle(Fact("NoSuchPlot", StoryReferenceTypes.PlotManifestFileTypeName), CtxWith()));
    }

    [Fact]
    public void An_unknown_owner_is_not_checked()
    {
        Assert.Empty(Sut.Handle(Fact("NoSuchPlan", null), CtxWith()));
    }

    [Fact]
    public void Another_workspace_file_tag_is_not_checked()
    {
        var other = XmlHandlerTestFixtures.MakeTag("Active_Plot", XmlValueType.NameReference,
            referenceKind: ReferenceKind.WorkspaceFile) with
        {
            ReferenceTypeName = StoryReferenceTypes.ThreadFileTypeName
        };
        var fact = new XmlTagValueFact("file:///test.xml", 3, 14, 6, other, "Nope", "SpaceUnit");

        Assert.Empty(Sut.Handle(fact, CtxWith()));
    }
}
