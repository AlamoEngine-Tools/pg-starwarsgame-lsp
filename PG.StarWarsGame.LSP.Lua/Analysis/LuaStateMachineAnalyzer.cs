// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Parsing;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticCode = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticCode;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>
///     What a script's state loads of the game's state machine library, and what else it defines.
/// </summary>
/// <param name="StateMachine">The state's PGStateMachine has the library's contract.</param>
/// <param name="StoryMode">The state's PGStoryMode registers StoryModeEvents as the game's does.</param>
/// <param name="FunctionsElsewhere">Globals defined in the other files of the state.</param>
public sealed record LuaStateMachineContract(
    bool StateMachine,
    bool StoryMode,
    IReadOnlySet<string> FunctionsElsewhere)
{
    public static readonly LuaStateMachineContract None = new(false, false, new HashSet<string>());

    /// <summary>
    ///     The contract of <paramref name="scriptUri" />'s state: the script plus its require closure.
    ///     A library counts when its own text has the game's contract - a mod's replacement that
    ///     changes it holds the script to nothing.
    /// </summary>
    public static LuaStateMachineContract For(string scriptUri, IReadOnlyDictionary<string, DocumentIndex> documents,
        ILuaParseCache parseCache, IFileHelper fileHelper)
    {
        var closure = LuaScriptClosure.RequiredBy(scriptUri, documents, fileHelper);
        if (closure.Count == 0) return None;

        bool Holds(string fileName, Func<SyntaxNode, bool> contract)
        {
            return LuaScriptClosure.Named(closure, fileName, fileHelper)
                .Any(uri => parseCache.GetOrParse(uri) is { } parsed && contract(parsed.Tree.GetRoot()));
        }

        var elsewhere = closure
            .Select(uri => documents.GetValueOrDefault(uri))
            .OfType<DocumentIndex>()
            .SelectMany(d => d.Symbols)
            .Where(s => s.Kind == GameSymbolKind.LuaGlobal)
            .Select(s => s.Id)
            .ToHashSet(StringComparer.Ordinal);
        return new LuaStateMachineContract(Holds("pgstatemachine.lua", HasMachineContract),
            Holds("pgstorymode.lua", HasStoryContract), elsewhere);
    }

    // Define_State registers, Set_Next_State refuses an unregistered name, Process_States drives a
    // handler with OnEnter, OnUpdate and OnExit.
    private static bool HasMachineContract(SyntaxNode root)
    {
        var functions = Functions(root);
        return functions.ContainsKey("Define_State") &&
               functions.TryGetValue("Set_Next_State", out var next) && Mentions(next, "StateMachine") &&
               functions.TryGetValue("Process_States", out var process) &&
               Mentions(process, "OnEnter") && Mentions(process, "OnUpdate") && Mentions(process, "OnExit");
    }

    // PG_Story_Mode_Init registers the entries of StoryModeEvents with Define_State.
    private static bool HasStoryContract(SyntaxNode root)
    {
        return Functions(root).TryGetValue("PG_Story_Mode_Init", out var init) &&
               Mentions(init, "StoryModeEvents") && Mentions(init, "Define_State");
    }

    private static Dictionary<string, FunctionDeclarationStatementSyntax> Functions(SyntaxNode root)
    {
        return root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>()
            .Where(f => f.Name is SimpleFunctionNameSyntax)
            .GroupBy(f => ((SimpleFunctionNameSyntax)f.Name).Name.Text, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    private static bool Mentions(SyntaxNode node, string name)
    {
        return node.DescendantNodes().OfType<IdentifierNameSyntax>().Any(i => i.Name == name);
    }
}

/// <summary>
///     Holds a script to the state machine library's contract (#252). Counted over the vanilla
///     scripts (EaW 62, FoC 151 using the machine): every handler but one per game takes exactly one
///     parameter, every comparison of the message is with OnEnter, OnUpdate or OnExit, no transition
///     names an unregistered state, and 8 EaW and 202 FoC StoryModeEvents entries name a function
///     defined nowhere - events that do nothing.
/// </summary>
internal static class LuaStateMachineAnalyzer
{
    private static readonly HashSet<string> s_messages = new(StringComparer.Ordinal)
        { "OnEnter", "OnUpdate", "OnExit" };

    public static IReadOnlyList<LspDiagnostic> Analyze(string documentUri, SyntaxTree tree,
        LuaStateMachineContract contract, IMessageCatalog messages)
    {
        // A library's machine runs in the scripts that require it; the file alone cannot be judged.
        if (!contract.StateMachine || LuaFileClassifier.IsLibraryUri(documentUri)) return [];
        var machine = LuaStateMachine.Read(tree.GetRoot());
        if (machine.IsEmpty) return [];

        var diagnostics = new List<LspDiagnostic>();
        var registered = new HashSet<string>(StringComparer.Ordinal);
        if (contract.StoryMode) registered.Add(LuaStateMachine.StoryInitState);

        foreach (var registration in machine.Registrations)
        {
            if (registration.Source == LuaStateSource.STORY_MODE_EVENTS && !contract.StoryMode) continue;
            if (!registered.Add(registration.Name))
                diagnostics.Add(Diagnostic(DiagnosticIds.LuaStateRegisteredTwice, LspDiagnosticSeverity.Warning,
                    registration.NameLocation,
                    messages.Format("LuaStateMachine_RegisteredAgain", registration.Name)));

            if (registration.Handler is { } handler)
            {
                CheckHandler(handler, diagnostics, messages);
                continue;
            }

            if (registration.Value is IdentifierNameSyntax reference &&
                contract.FunctionsElsewhere.Contains(reference.Name))
                continue;

            var written = registration.Value.ToString().Trim();
            diagnostics.Add(registration.Source == LuaStateSource.DEFINE_STATE
                ? Diagnostic(DiagnosticIds.LuaStateHandlerNotFunction, LspDiagnosticSeverity.Error,
                    registration.Value.GetLocation(),
                    messages.Format("LuaStateMachine_HandlerNotFunction", registration.Name, written))
                : Diagnostic(DiagnosticIds.LuaStoryEventHandlerMissing, LspDiagnosticSeverity.Warning,
                    registration.Value.GetLocation(),
                    registration.Value is IdentifierNameSyntax
                        ? messages.Format("LuaStateMachine_StoryEventUndefined", registration.Name, written)
                        : messages.Format("LuaStateMachine_StoryEventNotFunction", registration.Name, written)));
        }

        if (!machine.HasComputedRegistration)
            foreach (var transition in machine.Transitions.Where(t => !registered.Contains(t.Name)))
                diagnostics.Add(Diagnostic(DiagnosticIds.LuaStateTransitionUnregistered, LspDiagnosticSeverity.Error,
                    transition.NameLocation,
                    messages.Format("LuaStateMachine_NotRegistered", transition.Name)));

        return diagnostics;
    }

    private static void CheckHandler(LuaStateHandler handler, List<LspDiagnostic> diagnostics,
        IMessageCatalog messages)
    {
        var parameters = handler.Parameters.Parameters;
        if (parameters.Count != 1)
        {
            diagnostics.Add(Diagnostic(DiagnosticIds.LuaStateHandlerParameters, LspDiagnosticSeverity.Warning,
                handler.Parameters.GetLocation(),
                messages.Format("LuaStateMachine_HandlerParameters",
                    handler.Name ?? messages.Format("LuaStateMachine_AnonymousHandler"), parameters.Count)));
            if (parameters.Count == 0) return;
        }

        if (parameters[0] is not NamedParameterSyntax message) return;
        foreach (var comparison in handler.Body.DescendantNodes().OfType<BinaryExpressionSyntax>())
        {
            if (!comparison.IsKind(SyntaxKind.EqualsExpression) && !comparison.IsKind(SyntaxKind.NotEqualsExpression))
                continue;
            var other = IsName(comparison.Left, message.Identifier.Text) ? comparison.Right
                : IsName(comparison.Right, message.Identifier.Text) ? comparison.Left
                : null;
            if (other is null || (other is IdentifierNameSyntax { Name: var name } && s_messages.Contains(name)))
                continue;
            diagnostics.Add(Diagnostic(DiagnosticIds.LuaStateMessageComparison, LspDiagnosticSeverity.Warning,
                comparison.GetLocation(),
                messages.Format("LuaStateMachine_MessageComparison", message.Identifier.Text,
                    other.ToString().Trim())));
        }
    }

    private static bool IsName(ExpressionSyntax expression, string name)
    {
        return expression is IdentifierNameSyntax identifier && identifier.Name == name;
    }

    private static LspDiagnostic Diagnostic(DiagnosticId id, LspDiagnosticSeverity severity, Location location,
        string message)
    {
        var span = location.GetLineSpan();
        return new LspDiagnostic
        {
            Code = new LspDiagnosticCode(id.ToString()),
            Severity = severity,
            Message = message,
            Range = new LspRange(
                new LspPosition(span.StartLinePosition.Line, span.StartLinePosition.Character),
                new LspPosition(span.EndLinePosition.Line, span.EndLinePosition.Character)),
            Source = AppProperties.LspServerId
        };
    }
}