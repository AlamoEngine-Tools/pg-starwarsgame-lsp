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

/// <summary>What a script's state is for its reuse between instances (#250).</summary>
/// <param name="Pooled">The state is reused: ScriptPoolCount is not 0 in the script or its libraries.</param>
/// <param name="HasFlushG">A file of the state defines Flush_G, which the game calls on release.</param>
/// <param name="ResetOnRelease">Globals set at definition time, which runs again on release.</param>
/// <param name="RequiresResolved">Every require of the script resolved: what the state lacks is known.</param>
public sealed record LuaPooledState(
    bool Pooled,
    bool HasFlushG,
    IReadOnlySet<string> ResetOnRelease,
    bool RequiresResolved = true)
{
    /// <summary>
    ///     Functions that run at definition time: Definitions, Base_Definitions and what they call,
    ///     across the files of the state - a library's Definitions calling the script's own function
    ///     among them.
    /// </summary>
    public IReadOnlySet<string> DefinitionFunctions { get; init; } = new HashSet<string>();

    /// <summary>Another script requires this file: a library, wherever it lives, loaded into theirs.</summary>
    public bool RequiredByOthers { get; init; }

    /// <summary>The state's state machine library keeps the game's contract: a handler's order is known.</summary>
    public bool StateMachine { get; init; }

    /// <summary>What runs on release calls <c>GetEvent.Reset()</c>, emptying the event queue the game keeps.</summary>
    public bool ResetsEvents { get; init; } = true;

    /// <summary>The state of <paramref name="scriptUri" /> and the libraries it requires.</summary>
    public static LuaPooledState For(string scriptUri, SyntaxNode script,
        IReadOnlyDictionary<string, DocumentIndex> documents, ILuaParseCache parseCache, IFileHelper fileHelper)
    {
        var libraries = LuaScriptClosure.RequiredBy(scriptUri, documents, fileHelper)
            .Select(uri => parseCache.GetOrParse(uri)?.Tree.GetRoot())
            .OfType<SyntaxNode>()
            .ToList();
        var roots = libraries.Prepend(script).ToList();

        // The script's own top-level value wins: its chunk runs after the libraries it requires.
        var poolCount = PoolCount(script) ?? libraries.Select(PoolCount).LastOrDefault(c => c is not null);
        var reached = LuaGlobalWrites.DefinitionsReach(roots);
        var reset = reached.SelectMany(f => LuaGlobalWrites.Writes(f.Body, LuaGlobalWrites.Locals(f)))
            .Select(w => w.Name.Name)
            .ToHashSet(StringComparer.Ordinal);
        var hasFlushG = roots.Any(root => root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>()
            .Any(f => f.Name is SimpleFunctionNameSyntax { Name.Text: "Flush_G" }));
        var resolved = documents.TryGetValue(scriptUri, out var document) &&
                       (document.RequireArgs.IsDefaultOrEmpty || document.RequireArgs.All(arg =>
                           LuaRequireResolver.Resolve(arg, documents, fileHelper, scriptUri) is not null));
        // By module name, as require looks it up: one flat namespace, the file name without .lua.
        var module = Path.GetFileNameWithoutExtension(fileHelper.FileUriToPath(scriptUri) ?? scriptUri);
        var requiredByOthers = documents.Any(d =>
            !string.Equals(d.Key, scriptUri, StringComparison.OrdinalIgnoreCase) &&
            !d.Value.RequireArgs.IsDefaultOrEmpty &&
            d.Value.RequireArgs.Any(arg => string.Equals(
                Path.GetFileNameWithoutExtension(arg), module,
                StringComparison.OrdinalIgnoreCase)));
        var resetsEvents = reached.Any(f => f.Body.DescendantNodes().OfType<FunctionCallExpressionSyntax>().Any(c =>
            c.Expression is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Name: "GetEvent" }, MemberName.Text: "Reset"
            }));
        return new LuaPooledState(poolCount is not 0, hasFlushG, reset, resolved)
        {
            DefinitionFunctions = reached.Select(LuaGlobalWrites.NameOf).ToHashSet(StringComparer.Ordinal),
            RequiredByOthers = requiredByOthers,
            StateMachine = LuaStateMachineContract.For(scriptUri, documents, parseCache, fileHelper).StateMachine,
            ResetsEvents = resetsEvents
        };
    }

    private static int? PoolCount(SyntaxNode root)
    {
        if (root is not CompilationUnitSyntax unit) return null;
        int? count = null;
        foreach (var assignment in unit.Statements.Statements.OfType<AssignmentStatementSyntax>())
            for (var i = 0; i < assignment.Variables.Count && i < assignment.EqualsValues.Values.Count; i++)
                if (assignment.Variables[i] is IdentifierNameSyntax { Name: "ScriptPoolCount" } &&
                    assignment.EqualsValues.Values[i] is LiteralExpressionSyntax { Token.Value: double value })
                    count = (int)value;
        return count;
    }
}

/// <summary>Global assignments, told apart from locals and parameters, and what runs at definition time.</summary>
internal static class LuaGlobalWrites
{
    private static readonly string[] s_definitions = ["Definitions", "Base_Definitions"];

    public static string NameOf(FunctionDeclarationStatementSyntax function)
    {
        return ((SimpleFunctionNameSyntax)function.Name).Name.Text;
    }

    /// <summary>
    ///     Definitions, Base_Definitions and every function they call, by name, across
    ///     <paramref name="roots" />: the files of one Lua state share one namespace.
    /// </summary>
    public static IReadOnlyList<FunctionDeclarationStatementSyntax> DefinitionsReach(IEnumerable<SyntaxNode> roots)
    {
        var functions = new Dictionary<string, List<FunctionDeclarationStatementSyntax>>(StringComparer.Ordinal);
        foreach (var function in roots.SelectMany(r => r.DescendantNodes())
                     .OfType<FunctionDeclarationStatementSyntax>().Where(f => f.Name is SimpleFunctionNameSyntax))
        {
            if (!functions.TryGetValue(NameOf(function), out var all)) functions[NameOf(function)] = all = [];
            all.Add(function);
        }

        var reached = new List<FunctionDeclarationStatementSyntax>();
        var pending = new Queue<string>(s_definitions.Where(functions.ContainsKey));
        var seen = new HashSet<string>(pending, StringComparer.Ordinal);
        while (pending.Count > 0)
            foreach (var function in functions[pending.Dequeue()])
            {
                reached.Add(function);
                foreach (var call in function.Body.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
                    if (call.Expression is IdentifierNameSyntax { Name: var callee } &&
                        functions.ContainsKey(callee) && seen.Add(callee))
                        pending.Enqueue(callee);
            }

        return reached;
    }

    /// <summary>Every assignment to a name that is not a local or parameter of its function.</summary>
    public static IEnumerable<(IdentifierNameSyntax Name, ExpressionSyntax? Value)> Writes(SyntaxNode body,
        IReadOnlySet<string> locals)
    {
        foreach (var assignment in body.DescendantNodes().OfType<AssignmentStatementSyntax>())
            for (var i = 0; i < assignment.Variables.Count; i++)
                if (assignment.Variables[i] is IdentifierNameSyntax name && !locals.Contains(name.Name))
                    yield return (name,
                        i < assignment.EqualsValues.Values.Count ? assignment.EqualsValues.Values[i] : null);
    }

    /// <summary>The parameters and locals declared anywhere in a function, nested functions included.</summary>
    public static IReadOnlySet<string> Locals(SyntaxNode function)
    {
        var locals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in function.DescendantNodesAndSelf())
            switch (node)
            {
                case NamedParameterSyntax parameter:
                    locals.Add(parameter.Identifier.Text);
                    break;
                case LocalVariableDeclarationStatementSyntax local:
                    foreach (var name in local.Names) locals.Add(name.IdentifierName.Name);
                    break;
                case LocalFunctionDeclarationStatementSyntax localFunction:
                    locals.Add(localFunction.Name.Name);
                    break;
                case NumericForStatementSyntax numeric:
                    locals.Add(numeric.Identifier.Name);
                    break;
                case GenericForStatementSyntax generic:
                    foreach (var name in generic.Identifiers) locals.Add(name.Name);
                    break;
            }

        return locals;
    }

    /// <summary>
    ///     Whether <paramref name="name" />'s first access in <paramref name="body" />, in evaluation
    ///     order, is a read: an assignment's values are read before its targets are written, so
    ///     <c>x = x + 1</c> reads first.
    /// </summary>
    public static bool ReadsFirst(SyntaxNode body, string name)
    {
        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>().Where(i => i.Name == name))
        {
            if (identifier.Parent is not AssignmentStatementSyntax assignment ||
                !assignment.Variables.Contains(identifier))
                return true;
            return assignment.EqualsValues.Values.Any(v =>
                v.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(i => i.Name == name));
        }

        return false;
    }
}

/// <summary>
///     What the game's reuse of a script's state does to it (#250). Counted over the vanilla EaW
///     scripts: four plans read PlayerObject in Definitions (guarded, so they always take the Easy
///     branch); no pooled script lacks Flush_G outside the libraries.
/// </summary>
internal static class LuaPooledStateAnalyzer
{
    private static readonly HashSet<string> s_hostGlobals = new(StringComparer.Ordinal)
        { "Object", "PlayerObject", "Target", "AITarget", "Budget", "FreeStore" };

    // Calls that pump events: a callback can run inside them.
    private static readonly HashSet<string> s_pumps = new(StringComparer.Ordinal)
        { "Sleep", "PumpEvents", "BlockOnCommand" };

    public static IReadOnlyList<LspDiagnostic> Analyze(string documentUri, SyntaxTree tree, LuaPooledState state,
        IMessageCatalog messages)
    {
        var root = tree.GetRoot();
        var definitionTime = DefinitionTime(root, state);
        var diagnostics = new List<LspDiagnostic>();
        HostGlobalsInDefinitions(definitionTime, diagnostics, messages);

        // Only a script a host attaches per instance is pooled and reused; a library is the
        // requiring script's, an evaluator is loaded once.
        if (!state.Pooled || state.RequiredByOthers || !LuaFileClassifier.IsInstanceScriptUri(documentUri))
            return diagnostics;

        if (!state.HasFlushG && state.RequiresResolved)
            diagnostics.Add(Diagnostic(DiagnosticIds.LuaPooledScriptWithoutFlushG, LspDiagnosticSeverity.Warning,
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 0)),
                messages.Format("LuaPooledState_NoFlushG")));

        if (!state.ResetsEvents && state.RequiresResolved)
            diagnostics.Add(Diagnostic(DiagnosticIds.LuaPooledScriptEventsNotReset, LspDiagnosticSeverity.Warning,
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 0)),
                messages.Format("LuaPooledState_EventsNotReset")));

        Bleeding(root, state, definitionTime, diagnostics, messages);
        return diagnostics;
    }

    // Globals a state handler resets when the state machine calls it with OnUpdate. The library calls
    // a handler with OnEnter, then OnUpdate, and pumps events only after that - so the reset precedes
    // every event callback, unless OnEnter itself pumps (a Sleep in it, say).
    private static IEnumerable<string> ResetInOnUpdate(SyntaxNode root)
    {
        foreach (var registration in LuaStateMachine.Read(root).Registrations)
        {
            if (registration.Handler?.Parameters.Parameters.FirstOrDefault() is not NamedParameterSyntax message)
                continue;
            var branches = Branches(registration.Handler.Body, message.Identifier.Text);
            if (branches.TryGetValue("OnEnter", out var enter) && enter.DescendantNodes()
                    .OfType<FunctionCallExpressionSyntax>()
                    .Any(c => c.Expression is IdentifierNameSyntax { Name: var callee } && s_pumps.Contains(callee)))
                continue;
            if (!branches.TryGetValue("OnUpdate", out var update)) continue;
            foreach (var (name, _) in LuaGlobalWrites.Writes(update, LuaGlobalWrites.Locals(registration.Handler.Body)))
                yield return name.Name;
        }
    }

    // The branches of `if message == OnEnter ... elseif message == OnUpdate ...`, by message.
    private static Dictionary<string, StatementListSyntax> Branches(StatementListSyntax body, string message)
    {
        var branches = new Dictionary<string, StatementListSyntax>(StringComparer.Ordinal);
        foreach (var statement in body.Statements.OfType<IfStatementSyntax>())
        {
            if (MessageOf(statement.Condition, message) is { } first) branches.TryAdd(first, statement.Body);
            foreach (var clause in statement.ElseIfClauses)
                if (MessageOf(clause.Condition, message) is { } other)
                    branches.TryAdd(other, clause.Body);
        }

        return branches;
    }

    private static string? MessageOf(ExpressionSyntax condition, string message)
    {
        if (condition is not BinaryExpressionSyntax comparison || !comparison.IsKind(SyntaxKind.EqualsExpression))
            return null;
        return (comparison.Left, comparison.Right) switch
        {
            (IdentifierNameSyntax l, IdentifierNameSyntax r) when l.Name == message => r.Name,
            (IdentifierNameSyntax l, IdentifierNameSyntax r) when r.Name == message => l.Name,
            _ => null
        };
    }

    // The file's functions that run at definition time: its own Definitions reach, and any the
    // state's other files reach by name.
    private static HashSet<FunctionDeclarationStatementSyntax> DefinitionTime(SyntaxNode root, LuaPooledState state)
    {
        var own = LuaGlobalWrites.DefinitionsReach([root]).ToHashSet();
        own.UnionWith(root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>()
            .Where(f => f.Name is SimpleFunctionNameSyntax &&
                        state.DefinitionFunctions.Contains(LuaGlobalWrites.NameOf(f))));
        return own;
    }

    private static void HostGlobalsInDefinitions(IEnumerable<FunctionDeclarationStatementSyntax> definitionTime,
        List<LspDiagnostic> diagnostics, IMessageCatalog messages)
    {
        foreach (var function in definitionTime)
        {
            var name = LuaGlobalWrites.NameOf(function);
            var locals = LuaGlobalWrites.Locals(function);
            var written = LuaGlobalWrites.Writes(function.Body, locals).Select(w => w.Name).ToHashSet();
            foreach (var read in function.Body.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (!s_hostGlobals.Contains(read.Name) || locals.Contains(read.Name) || written.Contains(read))
                    continue;
                var where = name is "Definitions" or "Base_Definitions"
                    ? name
                    : messages.Format("LuaPooledState_RunsAtDefinitionTime", name);
                diagnostics.Add(Diagnostic(DiagnosticIds.LuaHostGlobalInDefinitions, LspDiagnosticSeverity.Error,
                    RangeOf(read.GetLocation()),
                    messages.Format("LuaPooledState_HostGlobalTooEarly", read.Name, where)));
            }
        }
    }

    private static void Bleeding(SyntaxNode root, LuaPooledState state,
        HashSet<FunctionDeclarationStatementSyntax> definitionTime, List<LspDiagnostic> diagnostics,
        IMessageCatalog messages)
    {
        var reset = new HashSet<string>(state.ResetOnRelease, StringComparer.Ordinal);
        foreach (var function in definitionTime)
            reset.UnionWith(LuaGlobalWrites.Writes(function.Body, LuaGlobalWrites.Locals(function))
                .Select(w => w.Name.Name));
        if (state.StateMachine) reset.UnionWith(ResetInOnUpdate(root));

        // The run-time functions: everything but definition time and what it encloses.
        var runTime = root.DescendantNodes()
            .Where(n => n is FunctionDeclarationStatementSyntax or LocalFunctionDeclarationStatementSyntax
                or AnonymousFunctionExpressionSyntax)
            .Where(n => !n.AncestorsAndSelf().OfType<FunctionDeclarationStatementSyntax>().Any(definitionTime.Contains))
            .ToList();
        var bodies = runTime.Select(BodyOf).OfType<StatementListSyntax>().ToList();

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in runTime)
        {
            if (BodyOf(function) is not { } body) continue;
            var outermost = function.AncestorsAndSelf().Last(a => a is FunctionDeclarationStatementSyntax
                or LocalFunctionDeclarationStatementSyntax or AnonymousFunctionExpressionSyntax);
            foreach (var (name, value) in LuaGlobalWrites.Writes(body, LuaGlobalWrites.Locals(outermost)))
            {
                if (reset.Contains(name.Name) || !Scalar(value) || reported.Contains(name.Name)) continue;
                // A value always overwritten before it is read carries nothing over.
                if (!bodies.Any(b => LuaGlobalWrites.ReadsFirst(b, name.Name))) continue;
                reported.Add(name.Name);
                diagnostics.Add(Diagnostic(DiagnosticIds.LuaGlobalBleedsBetweenInstances, LspDiagnosticSeverity.Warning,
                    RangeOf(name.GetLocation()),
                    messages.Format("LuaPooledState_BleedsBetweenInstances", name.Name)));
            }
        }
    }

    private static StatementListSyntax? BodyOf(SyntaxNode function)
    {
        return function switch
        {
            FunctionDeclarationStatementSyntax f => f.Body,
            LocalFunctionDeclarationStatementSyntax l => l.Body,
            AnonymousFunctionExpressionSyntax a => a.Body,
            _ => null
        };
    }

    // A value Flush_G keeps: a number, string, boolean or function. A table is deleted, nil changes
    // nothing, and a call's result is not known here.
    private static bool Scalar(ExpressionSyntax? value)
    {
        return value switch
        {
            LiteralExpressionSyntax literal => !literal.IsKind(SyntaxKind.NilLiteralExpression),
            ParenthesizedExpressionSyntax parenthesized => Scalar(parenthesized.Expression),
            BinaryExpressionSyntax or UnaryExpressionSyntax or AnonymousFunctionExpressionSyntax => true,
            _ => false
        };
    }

    private static LspRange RangeOf(Location location)
    {
        var span = location.GetLineSpan();
        return new LspRange(new LspPosition(span.StartLinePosition.Line, span.StartLinePosition.Character),
            new LspPosition(span.EndLinePosition.Line, span.EndLinePosition.Character));
    }

    private static LspDiagnostic Diagnostic(DiagnosticId id, LspDiagnosticSeverity severity, LspRange range,
        string message)
    {
        return new LspDiagnostic
        {
            Code = new LspDiagnosticCode(id.ToString()),
            Severity = severity,
            Message = message,
            Range = range,
            Source = AppProperties.LspServerId
        };
    }
}