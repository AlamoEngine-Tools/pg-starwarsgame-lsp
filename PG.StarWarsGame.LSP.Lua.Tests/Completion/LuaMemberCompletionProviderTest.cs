// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Completion;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Completion;

public sealed class LuaMemberCompletionProviderTest
{
    // Schema with:
    //   Find_Player(idx) -> PlayerObject
    //   PlayerObject.Faction (field)
    //   PlayerObject:GetName() (method) -> string
    private static readonly ILuaApiSchemaProvider Schema = new LuaApiSchemaProvider([
        """
        ---@return PlayerObject
        function Find_Player(playerIndex) end

        ---@return string
        function PlayerObject:GetName() end

        function PlayerObject.Faction() end
        """
    ]);

    private static IEnumerable<CompletionItem> Provide(
        string? receiverName,
        bool isMethod,
        IReadOnlyList<ScopeEntry>? scope = null,
        ILuaTypeIndex? typeIndex = null)
    {
        var ctx = new MemberAccessContext(receiverName, isMethod);
        return LuaMemberCompletionProvider.Provide(ctx, scope ?? [], Schema, typeIndex ?? LuaTypeIndex.Empty);
    }

    // ── return type resolution ────────────────────────────────────────────────

    [Fact]
    public void Provide_ReceiverIsEngineFunction_UsesReturnTypeName()
    {
        // Find_Player returns PlayerObject; GetName and Faction are members
        var scope = new List<ScopeEntry>
        {
            new("Find_Player", ScopeEntryKind.EngineApi, null)
        };
        var items = Provide("Find_Player", false, scope).ToList();
        // Should include members of PlayerObject
        Assert.NotEmpty(items);
    }

    [Fact]
    public void Provide_UnknownReceiver_ReturnsEmpty()
    {
        var items = Provide("UnknownVar", false).ToList();
        Assert.Empty(items);
    }

    // ── IsMethodCall filtering ────────────────────────────────────────────────

    [Fact]
    public void Provide_ColonAccess_OnlyMethodMembers()
    {
        var scope = new List<ScopeEntry> { new("Find_Player", ScopeEntryKind.EngineApi, null) };
        var items = Provide("Find_Player", true, scope).ToList();
        // GetName is a method (:), Faction is a field (.) → only GetName
        Assert.Contains(items, i => i.Label == "GetName");
        Assert.DoesNotContain(items, i => i.Label == "Faction");
    }

    [Fact]
    public void Provide_DotAccess_OnlyFieldMembers()
    {
        var scope = new List<ScopeEntry> { new("Find_Player", ScopeEntryKind.EngineApi, null) };
        var items = Provide("Find_Player", false, scope).ToList();
        // Faction is a field (.), GetName is a method (:) → only Faction
        Assert.Contains(items, i => i.Label == "Faction");
        Assert.DoesNotContain(items, i => i.Label == "GetName");
    }

    // ── completion item properties ────────────────────────────────────────────

    [Fact]
    public void Provide_MethodCompletion_KindIsMethod()
    {
        var scope = new List<ScopeEntry> { new("Find_Player", ScopeEntryKind.EngineApi, null) };
        var items = Provide("Find_Player", true, scope).ToList();
        var getNameItem = items.SingleOrDefault(i => i.Label == "GetName");
        Assert.NotNull(getNameItem);
        Assert.Equal(CompletionItemKind.Method, getNameItem!.Kind);
    }

    [Fact]
    public void Provide_FieldCompletion_KindIsField()
    {
        var scope = new List<ScopeEntry> { new("Find_Player", ScopeEntryKind.EngineApi, null) };
        var items = Provide("Find_Player", false, scope).ToList();
        var factionItem = items.SingleOrDefault(i => i.Label == "Faction");
        Assert.NotNull(factionItem);
        Assert.Equal(CompletionItemKind.Field, factionItem!.Kind);
    }

    // ── schema parsing ────────────────────────────────────────────────────────

    [Fact]
    public void GetReturnTypeName_ParsedFromReturnAnnotation()
    {
        Assert.Equal("PlayerObject", Schema.GetReturnTypeName("Find_Player"));
    }

    [Fact]
    public void GetMembersOf_ParsesMethodAndField()
    {
        var members = Schema.GetMembersOf("PlayerObject");
        Assert.Contains(members, m => m.Name == "GetName" && m.IsMethod);
        Assert.Contains(members, m => m.Name == "Faction" && !m.IsMethod);
    }

    [Fact]
    public void GetMembersOf_UnknownType_ReturnsEmpty()
    {
        Assert.Empty(Schema.GetMembersOf("UnknownType"));
    }

    // ── workspace type index members ─────────────────────────────────────────

    [Fact]
    public void Provide_TypeAnnotatedLocal_WorkspaceClassFields_AreOffered()
    {
        // Build a workspace type index with PGUnit having "health" and "name" fields.
        var repo = new LuaAnnotationRepository();
        var cls = new LuaClassDefinition("PGUnit", false,
            ImmutableArray<string>.Empty,
            [
                new LuaFieldDefinition("health", false, new LuaTypeRef("integer"), null),
                new LuaFieldDefinition("name", false, new LuaTypeRef("string"), null)
            ], null);
        repo.Update("file:///types.lua", [EmmyLuaAnnotations.Empty with { ClassDef = cls }]);
        repo.RebuildIndex();

        // Local variable "myUnit" annotated with @type PGUnit
        var scope = new List<ScopeEntry>
        {
            new("myUnit", ScopeEntryKind.LocalVariable, null, "PGUnit")
        };

        var items = Provide("myUnit", false, scope, repo.Current).ToList();

        Assert.Contains(items, i => i.Label == "health");
        Assert.Contains(items, i => i.Label == "name");
        Assert.All(items, i => Assert.Equal(CompletionItemKind.Field, i.Kind));
    }

    [Fact]
    public void Provide_TypeAnnotatedLocal_MethodCall_NoFieldsOffered()
    {
        var repo = new LuaAnnotationRepository();
        var cls = new LuaClassDefinition("PGUnit", false,
            ImmutableArray<string>.Empty,
            [new LuaFieldDefinition("health", false, new LuaTypeRef("integer"), null)],
            null);
        repo.Update("file:///types.lua", [EmmyLuaAnnotations.Empty with { ClassDef = cls }]);
        repo.RebuildIndex();

        var scope = new List<ScopeEntry>
        {
            new("myUnit", ScopeEntryKind.LocalVariable, null, "PGUnit")
        };

        // IsMethodCall=true → workspace class fields should not appear (they are dot-access only)
        var items = Provide("myUnit", true, scope, repo.Current).ToList();
        Assert.DoesNotContain(items, i => i.Label == "health");
    }

    [Fact]
    public void Provide_EngineApiReturnType_IsWorkspaceClass_FieldsOffered()
    {
        // Engine function returns a workspace-defined type — cross-lookup scenario.
        var repo = new LuaAnnotationRepository();
        var cls = new LuaClassDefinition("PlayerObject", false,
            ImmutableArray<string>.Empty,
            [new LuaFieldDefinition("credits", false, new LuaTypeRef("integer"), null)],
            null);
        repo.Update("file:///types.lua", [EmmyLuaAnnotations.Empty with { ClassDef = cls }]);
        repo.RebuildIndex();

        // Schema already knows Find_Player returns PlayerObject (via @return).
        var scope = new List<ScopeEntry>
        {
            new("Find_Player", ScopeEntryKind.EngineApi, null)
        };

        var items = Provide("Find_Player", false, scope, repo.Current).ToList();

        // Existing engine member "Faction" still present; new workspace field "credits" also present.
        Assert.Contains(items, i => i.Label == "Faction");
        Assert.Contains(items, i => i.Label == "credits");
    }
}