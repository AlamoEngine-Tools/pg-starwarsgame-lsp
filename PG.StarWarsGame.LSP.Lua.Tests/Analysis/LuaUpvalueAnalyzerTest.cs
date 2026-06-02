// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaUpvalueAnalyzerTest
{
    private static IReadOnlyList<Diagnostic> Analyze(string text, string? uri = null) =>
        LuaUpvalueAnalyzer.Analyze(text, uri);

    // ── no-warning baseline ───────────────────────────────────────────────────

    [Fact]
    public void EmptyFile_NoDiagnostics()
    {
        var result = Analyze("");
        Assert.Empty(result);
    }

    [Fact]
    public void OnlyGlobals_NoDiagnostics()
    {
        const string text = """
                            x = 1
                            function Foo()
                                x = x + 1
                            end
                            """;
        var result = Analyze(text);
        Assert.Empty(result);
    }

    [Fact]
    public void FileLevelLocalNotUsedInAnyFunction_NoDiagnostic()
    {
        const string text = """
                            local counter = 0
                            counter = 1
                            """;
        var result = Analyze(text);
        Assert.Empty(result);
    }

    [Fact]
    public void FunctionOwnLocal_NoDiagnostic()
    {
        const string text = """
                            function Foo()
                                local x = 1
                                x = x + 1
                            end
                            """;
        var result = Analyze(text);
        Assert.Empty(result);
    }

    [Fact]
    public void FunctionParameter_NoDiagnostic()
    {
        const string text = """
                            function Foo(counter)
                                return counter + 1
                            end
                            """;
        var result = Analyze(text);
        Assert.Empty(result);
    }

    [Fact]
    public void LocalFunctionDeclaration_NoDiagnosticForName()
    {
        // A local function is itself locally bound — not a file-level local
        const string text = """
                            local function Helper() end
                            function Foo()
                                Helper()
                            end
                            """;
        var result = Analyze(text);
        Assert.Empty(result);
    }

    // ── upvalue detection ─────────────────────────────────────────────────────

    [Fact]
    public void FileLevelLocal_CapturedByTopLevelFunction_EmitsWarning()
    {
        const string text = """
                            local counter = 0
                            function Update_Thread()
                                counter = counter + 1
                            end
                            """;
        var result = Analyze(text);
        var diag = Assert.Single(result);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("counter", diag.Message);
        Assert.Contains("Update_Thread", diag.Message);
    }

    [Fact]
    public void FileLevelLocal_CapturedByTwoFunctions_TwoWarnings()
    {
        const string text = """
                            local state = 0
                            function Init_Thread()
                                state = 1
                            end
                            function Update_Thread()
                                state = state + 1
                            end
                            """;
        var result = Analyze(text);
        Assert.Equal(2, result.Count);
        Assert.All(result, d => Assert.Contains("state", d.Message));
    }

    [Fact]
    public void FileLevelLocal_UsedMultipleTimesInSameFunction_OneWarningPerLocal()
    {
        const string text = """
                            local counter = 0
                            function Update_Thread()
                                counter = counter + 1
                                counter = counter + 1
                            end
                            """;
        var result = Analyze(text);
        var diag = Assert.Single(result);
        Assert.Contains("counter", diag.Message);
    }

    [Fact]
    public void TwoFileLevelLocals_BothCaptured_TwoWarnings()
    {
        const string text = """
                            local a = 0
                            local b = 0
                            function Foo()
                                a = a + b
                            end
                            """;
        var result = Analyze(text);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.Message.Contains("'a'"));
        Assert.Contains(result, d => d.Message.Contains("'b'"));
    }

    [Fact]
    public void FileLevelLocal_CapturedByNestedFunction_EmitsWarning()
    {
        // Nested closures inside a top-level function are equally dangerous
        const string text = """
                            local state = 0
                            function Outer()
                                local function Inner()
                                    state = state + 1
                                end
                                Inner()
                            end
                            """;
        var result = Analyze(text);
        var diag = Assert.Single(result);
        Assert.Contains("state", diag.Message);
    }

    [Fact]
    public void FileLevelLocal_DefinedAfterFunction_StillWarns()
    {
        // Lua captures by reference, order in file doesn't matter
        const string text = """
                            function Foo()
                                x = count + 1
                            end
                            local count = 0
                            """;
        // Note: 'x' is a global (no local), 'count' IS a file-level local → warning for count
        var result = Analyze(text);
        var diag = Assert.Single(result);
        Assert.Contains("count", diag.Message);
    }

    [Fact]
    public void DiagnosticCode_IsEngineUpvalue()
    {
        const string text = """
                            local x = 1
                            function Foo()
                                return x
                            end
                            """;
        var result = Analyze(text);
        var diag = Assert.Single(result);
        Assert.True(diag.Code?.IsString == true);
        Assert.Equal(LuaDiagnosticCodes.EngineUpvalue, diag.Code?.String);
    }

    [Fact]
    public void DiagnosticRange_CoversUseSite()
    {
        // "local x = 1" on line 0
        // "function Foo()" on line 1
        // "    return x" on line 2 — 'x' starts at column 11 (after 4-space indent + "return ")
        const string text = "local x = 1\nfunction Foo()\n    return x\nend";
        var result = Analyze(text);
        var diag = Assert.Single(result);
        Assert.Equal(2, diag.Range.Start.Line);
        Assert.Equal("    return ".Length, diag.Range.Start.Character);
    }

    // ── suppression ───────────────────────────────────────────────────────────

    [Fact]
    public void UpvalueOkAnnotation_AboveLocalDeclaration_SuppressesWarning()
    {
        const string text = """
                            ---@upvalue-ok
                            local counter = 0
                            function Update_Thread()
                                counter = counter + 1
                            end
                            """;
        var result = Analyze(text);
        Assert.Empty(result);
    }

    [Fact]
    public void UpvalueOkAnnotation_PartialSuppression_OnlyAnnotatedLocalIsSuppressed()
    {
        const string text = """
                            ---@upvalue-ok
                            local a = 0
                            local b = 0
                            function Foo()
                                a = b + 1
                            end
                            """;
        var result = Analyze(text);
        var diag = Assert.Single(result);
        Assert.Contains("'b'", diag.Message);
    }

    // ── message quality ───────────────────────────────────────────────────────

    [Fact]
    public void DiagnosticMessage_MentionsFunctionName()
    {
        const string text = """
                            local x = 1
                            function Definitions()
                                return x
                            end
                            """;
        var result = Analyze(text);
        var diag = Assert.Single(result);
        Assert.Contains("Definitions", diag.Message);
    }

    [Fact]
    public void DiagnosticMessage_MentionsSaveGameImpact()
    {
        const string text = """
                            local x = 1
                            function Foo()
                                return x
                            end
                            """;
        var result = Analyze(text);
        var diag = Assert.Single(result);
        Assert.Contains("save", diag.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── related information ───────────────────────────────────────────────────

    [Fact]
    public void FileLevelLocal_CapturedByFunction_RelatedInfoHasLocalDeclarationEntry()
    {
        // "local x = 1" is on line 0; RelatedInformation[0] must point there
        const string text = "local x = 1\nfunction Foo()\n    return x\nend";
        var result = Analyze(text, "file:///s.lua");
        var diag = Assert.Single(result);
        Assert.NotNull(diag.RelatedInformation);
        var localEntry = diag.RelatedInformation!.FirstOrDefault(r =>
            r.Message.Contains("local", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(localEntry);
        Assert.Equal(0, localEntry!.Location.Range.Start.Line);
        Assert.Equal("file:///s.lua", localEntry.Location.Uri.ToString());
    }

    [Fact]
    public void FileLevelLocal_CapturedByFunction_RelatedInfoHasFunctionDeclarationEntry()
    {
        // "function Foo()" is on line 1; RelatedInformation must also contain that location
        const string text = "local x = 1\nfunction Foo()\n    return x\nend";
        var result = Analyze(text, "file:///s.lua");
        var diag = Assert.Single(result);
        Assert.NotNull(diag.RelatedInformation);
        var funcEntry = diag.RelatedInformation!.FirstOrDefault(r =>
            r.Message.Contains("function", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(funcEntry);
        Assert.Equal(1, funcEntry!.Location.Range.Start.Line);
    }

    [Fact]
    public void Analyze_WithoutUri_NoRelatedInformation()
    {
        // When no URI is provided the analyzer cannot produce location-based related info
        const string text = "local x = 1\nfunction Foo()\n    return x\nend";
        var result = Analyze(text);
        var diag = Assert.Single(result);
        Assert.True(diag.RelatedInformation is null || !diag.RelatedInformation.Any());
    }
}
