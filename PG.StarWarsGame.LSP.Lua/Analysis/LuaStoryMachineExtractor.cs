// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Lua.Parsing;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>
///     Reads a story script into a <see cref="LuaStoryMachine" /> without running it. Vanilla
///     story scripts (measured 2026-09-19 over 138 of them) keep one shape: a <c>StoryModeEvents</c>
///     table maps XML event names to state functions; a state function branches on
///     <c>message == OnEnter / OnUpdate / OnExit</c>; the enter branch starts threads by name with
///     <c>Create_Thread("F")</c>, and a thread sleeps and calls <c>Story_Event("Id")</c>. No vanilla
///     script calls <c>Set_Next_State</c> itself. This walker follows that shape: it steps through a
///     phase's statements in order, sums the numeric <c>Sleep</c> calls on the way, follows thread
///     starts and same-file function calls one level, and walks loop and branch bodies once.
/// </summary>
public static class LuaStoryMachineExtractor
{
    // A phase follows a thread or helper, and that one its own callee; nothing deeper.
    private const int MaxFollowDepth = 2;

    public static LuaStoryMachine? Extract(string text, string documentUri)
    {
        var root = ParsedLuaDocument.Parse(text, documentUri).Tree.GetRoot();
        var entries = StoryModeEntries(root);
        if (entries.Count == 0) return null;

        var functions = root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>()
            .Where(f => f.Name is SimpleFunctionNameSyntax)
            .GroupBy(f => ((SimpleFunctionNameSyntax)f.Name).Name.Text, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var scope = new FileScope(root, functions);

        var states = new List<LuaStoryState>();
        foreach (var (stateName, functionName, inline) in entries)
        {
            StatementListSyntax? body = inline?.Body;
            if (body is null && functionName is not null && functions.TryGetValue(functionName, out var declared))
                body = declared.Body;
            var phases = body is null
                ? (LuaStoryPhase.Empty, LuaStoryPhase.Empty, LuaStoryPhase.Empty)
                : Phases(body, scope);
            states.Add(
                new LuaStoryState(stateName, functionName ?? stateName, phases.Item1, phases.Item2, phases.Item3));
        }

        var scriptName = Path.GetFileNameWithoutExtension(documentUri);
        return new LuaStoryMachine(documentUri, scriptName, states);
    }

    // ── StoryModeEvents ──────────────────────────────────────────────────────

    private static List<(string State, string? FunctionName, AnonymousFunctionExpressionSyntax? Inline)>
        StoryModeEntries(SyntaxNode root)
    {
        var entries = new List<(string, string?, AnonymousFunctionExpressionSyntax?)>();
        foreach (var node in root.DescendantNodes())
            switch (node)
            {
                case AssignmentStatementSyntax assignment:
                    for (var i = 0; i < assignment.Variables.Count && i < assignment.EqualsValues.Values.Count; i++)
                        if (assignment.Variables[i] is IdentifierNameSyntax { Name: "StoryModeEvents" } &&
                            assignment.EqualsValues.Values[i] is TableConstructorExpressionSyntax table)
                            AddEntries(table, entries);
                    break;
                case IdentifierKeyedTableFieldSyntax { Value: TableConstructorExpressionSyntax nested } field
                    when field.Identifier.Text == "StoryModeEvents":
                    AddEntries(nested, entries);
                    break;
            }

        return entries;
    }

    private static void AddEntries(TableConstructorExpressionSyntax table,
        List<(string, string?, AnonymousFunctionExpressionSyntax?)> entries)
    {
        foreach (var field in table.Fields.OfType<IdentifierKeyedTableFieldSyntax>())
        {
            var name = field.Identifier.Text;
            if (string.IsNullOrEmpty(name)) continue;
            switch (field.Value)
            {
                case IdentifierNameSyntax reference:
                    entries.Add((name, reference.Name, null));
                    break;
                case AnonymousFunctionExpressionSyntax inline:
                    entries.Add((name, null, inline));
                    break;
            }
        }
    }

    // ── Phases ───────────────────────────────────────────────────────────────

    private static (LuaStoryPhase, LuaStoryPhase, LuaStoryPhase) Phases(StatementListSyntax body, FileScope scope)
    {
        var enter = new PhaseBuilder();
        var update = new PhaseBuilder();
        var exit = new PhaseBuilder();
        foreach (var statement in body.Statements)
        {
            if (statement is not IfStatementSyntax ifStatement)
                continue;
            if (PhaseOf(ifStatement.Condition) is { } first)
                Walk(ifStatement.Body, ForPhase(first), scope, new WalkState());
            foreach (var clause in ifStatement.ElseIfClauses)
                if (PhaseOf(clause.Condition) is { } phase)
                    Walk(clause.Body, ForPhase(phase), scope, new WalkState());
        }

        return (enter.Build(), update.Build(), exit.Build());

        PhaseBuilder ForPhase(string phase)
        {
            return phase switch { "OnEnter" => enter, "OnUpdate" => update, _ => exit };
        }
    }

    /// <summary><c>message == OnEnter</c> in either operand order; null for any other condition.</summary>
    private static string? PhaseOf(ExpressionSyntax condition)
    {
        if (condition is not BinaryExpressionSyntax binary) return null;
        foreach (var side in new[] { binary.Left, binary.Right })
            if (side is IdentifierNameSyntax { Name: "OnEnter" or "OnUpdate" or "OnExit" } id)
                return id.Name;
        return null;
    }

    // ── Statement walk ───────────────────────────────────────────────────────

    private sealed class WalkState
    {
        public double Delay;
        public int Depth;
        public HashSet<string> Visited { get; } = new(StringComparer.Ordinal);
    }

    private static void Walk(StatementListSyntax block, PhaseBuilder phase, FileScope scope, WalkState state)
    {
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case ExpressionStatementSyntax { Expression: FunctionCallExpressionSyntax call }:
                    Call(call, phase, scope, state);
                    break;
                case LocalVariableDeclarationStatementSyntax local when local.EqualsValues is not null:
                    foreach (var value in local.EqualsValues.Values)
                        if (value is FunctionCallExpressionSyntax call)
                            Call(call, phase, scope, state);
                    break;
                case AssignmentStatementSyntax assignment:
                    foreach (var value in assignment.EqualsValues.Values)
                        if (value is FunctionCallExpressionSyntax call)
                            Call(call, phase, scope, state);
                    break;
                case IfStatementSyntax nested:
                    Walk(nested.Body, phase, scope, state);
                    foreach (var clause in nested.ElseIfClauses) Walk(clause.Body, phase, scope, state);
                    if (nested.ElseClause is { } elseClause) Walk(elseClause.ElseBody, phase, scope, state);
                    break;
                case WhileStatementSyntax loop:
                    Walk(loop.Body, phase, scope, state);
                    break;
                case RepeatUntilStatementSyntax loop:
                    Walk(loop.Body, phase, scope, state);
                    break;
                case NumericForStatementSyntax loop:
                    Walk(loop.Body, phase, scope, state);
                    break;
                case GenericForStatementSyntax loop:
                    Walk(loop.Body, phase, scope, state);
                    break;
                case DoStatementSyntax block2:
                    Walk(block2.Body, phase, scope, state);
                    break;
                case ReturnStatementSyntax:
                    return;
            }
        }
    }

    private static void Call(FunctionCallExpressionSyntax call, PhaseBuilder phase, FileScope scope, WalkState state)
    {
        if (call.Expression is not IdentifierNameSyntax { Name: var name }) return;
        var args = Arguments(call);
        switch (name)
        {
            case "Sleep":
                state.Delay += args.Count > 0 ? scope.Number(args[0]) : 0;
                break;
            case "Story_Event":
                if (args.Count > 0 && scope.String(args[0]) is { } id)
                    phase.Emissions.Add(new LuaStoryEmission(id, state.Delay));
                break;
            case "Set_Next_State":
                if (args.Count > 0 && scope.String(args[0]) is { } target) phase.Transitions.Add(target);
                break;
            case "Create_Thread":
            {
                if (args.Count == 0 || scope.String(args[0]) is not { } threadName) break;
                phase.Threads.Add(threadName);
                // A thread starts now and sleeps on its own clock; its emissions land at the
                // current delay plus its own.
                if (state.Depth < MaxFollowDepth && state.Visited.Add(threadName) &&
                    scope.Function(threadName) is { } thread)
                    Walk(thread.Body, phase, scope, new WalkState { Delay = state.Delay, Depth = state.Depth + 1 });
                break;
            }
            case "Spawn_Unit":
            case "SpawnList":
            case "Spawn_Unit_Type":
            {
                if (args.Count == 0) break;
                var planet = args.Count > 1 ? scope.Planet(args[1]) : null;
                foreach (var unitType in scope.Strings(args[0]))
                    phase.Spawns.Add(new LuaStorySpawn(unitType, planet));
                break;
            }
            default:
                // A same-file helper called directly runs inline: its sleeps and emissions are
                // this phase's, one level down.
                if (state.Depth < MaxFollowDepth && state.Visited.Add(name) && scope.Function(name) is { } helper)
                {
                    var inner = new WalkState { Delay = state.Delay, Depth = state.Depth + 1 };
                    foreach (var visited in state.Visited) inner.Visited.Add(visited);
                    Walk(helper.Body, phase, scope, inner);
                    state.Delay = inner.Delay;
                }

                break;
        }
    }

    private static IReadOnlyList<ExpressionSyntax> Arguments(FunctionCallExpressionSyntax call)
    {
        return call.Argument switch
        {
            ExpressionListFunctionArgumentSyntax list => list.Expressions.ToList(),
            StringFunctionArgumentSyntax single => [single.Expression],
            _ => []
        };
    }

    private sealed class PhaseBuilder
    {
        public List<LuaStoryEmission> Emissions { get; } = [];
        public List<string> Transitions { get; } = [];
        public List<LuaStorySpawn> Spawns { get; } = [];
        public List<string> Threads { get; } = [];

        public LuaStoryPhase Build()
        {
            return new LuaStoryPhase(Emissions, Transitions, Spawns, Threads);
        }
    }

    /// <summary>Same-file lookups: functions by name, and the string, number and table values globals are given once.</summary>
    private sealed class FileScope
    {
        private readonly Dictionary<string, FunctionDeclarationStatementSyntax> _functions;
        private readonly Dictionary<string, ExpressionSyntax> _globals = new(StringComparer.Ordinal);

        public FileScope(SyntaxNode root, Dictionary<string, FunctionDeclarationStatementSyntax> functions)
        {
            _functions = functions;
            foreach (var assignment in root.DescendantNodes().OfType<AssignmentStatementSyntax>())
                for (var i = 0; i < assignment.Variables.Count && i < assignment.EqualsValues.Values.Count; i++)
                    if (assignment.Variables[i] is IdentifierNameSyntax { Name: var target })
                        _globals.TryAdd(target, assignment.EqualsValues.Values[i]);
            foreach (var local in root.DescendantNodes().OfType<LocalVariableDeclarationStatementSyntax>())
            {
                if (local.EqualsValues is null) continue;
                for (var i = 0; i < local.Names.Count && i < local.EqualsValues.Values.Count; i++)
                    _globals.TryAdd(local.Names[i].Name, local.EqualsValues.Values[i]);
            }
        }

        public FunctionDeclarationStatementSyntax? Function(string name)
        {
            return _functions.GetValueOrDefault(name);
        }

        public string? String(ExpressionSyntax expression)
        {
            return expression switch
            {
                LiteralExpressionSyntax { Token.Value: string s } => s,
                IdentifierNameSyntax { Name: var name } when _globals.GetValueOrDefault(name) is LiteralExpressionSyntax
                {
                    Token.Value: string s
                } => s,
                _ => null
            };
        }

        public double Number(ExpressionSyntax expression)
        {
            if (expression is LiteralExpressionSyntax literal &&
                double.TryParse(literal.Token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
            return 0;
        }

        /// <summary>A string literal, or a global table of string literals, as the unit types it names.</summary>
        public IEnumerable<string> Strings(ExpressionSyntax expression)
        {
            if (String(expression) is { } single) return [single];
            var table = expression as TableConstructorExpressionSyntax
                        ?? (expression is IdentifierNameSyntax { Name: var name }
                            ? _globals.GetValueOrDefault(name) as TableConstructorExpressionSyntax
                            : null);
            if (table is null) return [];
            return table.Fields.OfType<UnkeyedTableFieldSyntax>()
                .Select(f => f.Value is LiteralExpressionSyntax { Token.Value: string s } ? s : null)
                .Where(s => s is not null)!;
        }

        /// <summary>The planet a value names: a literal, or a variable assigned <c>FindPlanet("Name")</c>.</summary>
        public string? Planet(ExpressionSyntax expression)
        {
            if (String(expression) is { } literal) return literal;
            if (expression is not IdentifierNameSyntax { Name: var name }) return null;
            if (_globals.GetValueOrDefault(name) is FunctionCallExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Name: "FindPlanet" }
                } find)
            {
                var args = Arguments(find);
                return args.Count > 0 ? String(args[0]) : null;
            }

            return null;
        }
    }
}