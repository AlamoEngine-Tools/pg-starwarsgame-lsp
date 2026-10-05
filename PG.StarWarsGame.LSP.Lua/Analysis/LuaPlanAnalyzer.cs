// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticCode = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticCode;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>
///     What the engine demands of a plan script beyond parsing: one thread function per task
///     force. When a plan loads, the engine maps each entry of the <c>TaskForce</c> table into
///     the script as a global of type TaskForce and starts a coroutine on the global function
///     named <c>&lt;Force&gt;_Thread</c>; the suffix is the engine's own, appended when it builds
///     the function name (measured on the game's binary, 2026-10-05). A plan without that
///     function is logged as unable to find the thread function and never runs the force. The
///     vanilla plans agree: 248 of 250 forces, the two exceptions being the intervention library,
///     whose table is consumed by the plans that require it and define the function themselves.
/// </summary>
internal static class LuaPlanAnalyzer
{
    private const string ThreadSuffix = "_Thread";

    public static IReadOnlyList<LspDiagnostic> Analyze(string documentUri, SyntaxTree tree)
    {
        // A library's Definitions runs inside the plan that requires it, where the thread
        // function lives; the library file alone cannot be judged.
        if (LuaFileClassifier.IsLibraryUri(documentUri)) return [];

        var root = tree.GetRoot();
        var forces = LuaTaskForceTable.Forces(root);
        if (forces.Count == 0) return [];

        var globalFunctions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
            if (declaration.Name is SimpleFunctionNameSyntax simple)
                globalFunctions.Add(simple.Name.Text);

        var diagnostics = new List<LspDiagnostic>();
        foreach (var (name, token) in forces)
        {
            var expected = name + ThreadSuffix;
            if (globalFunctions.Contains(expected)) continue;

            // The range covers the name inside the quotes.
            var span = token.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;
            diagnostics.Add(new LspDiagnostic
            {
                Code = new LspDiagnosticCode(DiagnosticIds.LuaPlanThreadFunctionMissing.ToString()),
                Severity = LspDiagnosticSeverity.Error,
                Message =
                    $"Task force '{name}' has no thread function: The engine starts '{expected}' when the plan loads and cannot run the force without it.",
                Range = new LspRange(
                    new LspPosition(start.Line, start.Character + 1),
                    new LspPosition(start.Line, start.Character + 1 + name.Length)),
                Source = AppProperties.LspServerId
            });
        }

        return diagnostics;
    }
}