// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>How a state came to be registered.</summary>
public enum LuaStateSource
{
    /// <summary><c>Define_State("Name", handler)</c>.</summary>
    DEFINE_STATE,

    /// <summary>An entry of <c>StoryModeEvents</c>, registered by the story library under the entry's key.</summary>
    STORY_MODE_EVENTS
}

/// <summary>One state a script registers, and the value it registers it with.</summary>
/// <param name="Name">The state's name.</param>
/// <param name="NameLocation">The name itself: inside the quotes, or the table key.</param>
/// <param name="Value">The registered value, as written.</param>
/// <param name="Handler">The function the value is, when it is one in this file.</param>
public sealed record LuaStateRegistration(
    string Name,
    Location NameLocation,
    LuaStateSource Source,
    ExpressionSyntax Value,
    LuaStateHandler? Handler);

/// <summary>A state handler in the script: a function declared by name, or an inline function.</summary>
public sealed record LuaStateHandler(
    string? Name,
    ParameterListSyntax Parameters,
    StatementListSyntax Body,
    Location NameLocation);

/// <summary>A <c>Set_Next_State("Name")</c>.</summary>
public sealed record LuaStateTransition(string Name, Location NameLocation);

/// <summary>What of the state machine sits at a position.</summary>
public enum LuaStateHitKind
{
    /// <summary>A state's name: in Define_State, Set_Next_State or a StoryModeEvents key.</summary>
    STATE_NAME,

    /// <summary>A state handler's name: its declaration or the value registering it.</summary>
    HANDLER,

    /// <summary>A state handler's first parameter, the message, or a use of it.</summary>
    MESSAGE
}

/// <param name="State">The state's name.</param>
/// <param name="Location">The span under the position.</param>
/// <param name="Registration">The state's registration, when the state is registered in the file.</param>
public sealed record LuaStateHit(
    LuaStateHitKind Kind,
    string State,
    Location Location,
    LuaStateRegistration? Registration);

/// <summary>
///     A script's use of the game's state machine library, read from its syntax without running it
///     (#252): the states it registers - through <c>Define_State</c> and, under story mode, through
///     <c>StoryModeEvents</c> - and the transitions it names.
/// </summary>
[PublicAPI]
public sealed class LuaStateMachine
{
    /// <summary>The state story mode registers first, ahead of every StoryModeEvents entry.</summary>
    public const string StoryInitState = "PG_Story_State_Init";

    private LuaStateMachine(IReadOnlyList<LuaStateRegistration> registrations,
        IReadOnlyList<LuaStateTransition> transitions, bool computedRegistration,
        IReadOnlyDictionary<string, LuaStateHandler> functions)
    {
        Registrations = registrations;
        Transitions = transitions;
        HasComputedRegistration = computedRegistration;
        Functions = functions;
    }

    public IReadOnlyList<LuaStateRegistration> Registrations { get; }
    public IReadOnlyList<LuaStateTransition> Transitions { get; }

    /// <summary>A <c>Define_State</c> whose name is not a string literal: it could register any name.</summary>
    public bool HasComputedRegistration { get; }

    /// <summary>Every function the file declares by a plain name, global or local.</summary>
    public IReadOnlyDictionary<string, LuaStateHandler> Functions { get; }

    public bool IsEmpty => Registrations.Count == 0 && Transitions.Count == 0;

    /// <summary>The first registration of <paramref name="state" />, or null.</summary>
    public LuaStateRegistration? RegistrationOf(string state)
    {
        return Registrations.FirstOrDefault(r => r.Name == state);
    }

    /// <summary>What of the state machine sits at (line, character), 0-based; null for anything else.</summary>
    public LuaStateHit? At(int line, int character)
    {
        foreach (var registration in Registrations)
        {
            if (Contains(registration.NameLocation, line, character))
                return new LuaStateHit(LuaStateHitKind.STATE_NAME, registration.Name, registration.NameLocation,
                    registration);
            if (registration.Value is IdentifierNameSyntax &&
                Contains(registration.Value.GetLocation(), line, character))
                return new LuaStateHit(LuaStateHitKind.HANDLER, registration.Name, registration.Value.GetLocation(),
                    registration);
        }

        foreach (var transition in Transitions.Where(t => Contains(t.NameLocation, line, character)))
            return new LuaStateHit(LuaStateHitKind.STATE_NAME, transition.Name, transition.NameLocation,
                RegistrationOf(transition.Name));

        foreach (var registration in Registrations)
        {
            if (registration.Handler is not { } handler) continue;
            if (handler.Name is not null && Contains(handler.NameLocation, line, character))
                return new LuaStateHit(LuaStateHitKind.HANDLER, registration.Name, handler.NameLocation, registration);
            if (handler.Parameters.Parameters.FirstOrDefault() is not NamedParameterSyntax message) continue;
            var name = message.Identifier.Text;
            if (Contains(message.Identifier.GetLocation(), line, character))
                return new LuaStateHit(LuaStateHitKind.MESSAGE, registration.Name, message.Identifier.GetLocation(),
                    registration);
            foreach (var use in handler.Body.DescendantNodes().OfType<IdentifierNameSyntax>()
                         .Where(i => i.Name == name && Contains(i.GetLocation(), line, character)))
                return new LuaStateHit(LuaStateHitKind.MESSAGE, registration.Name, use.GetLocation(), registration);
        }

        return null;
    }

    private static bool Contains(Location location, int line, int character)
    {
        var span = location.GetLineSpan();
        var (start, end) = (span.StartLinePosition, span.EndLinePosition);
        if (line < start.Line || line > end.Line) return false;
        if (line == start.Line && character < start.Character) return false;
        return line != end.Line || character <= end.Character;
    }

    public static LuaStateMachine Read(SyntaxNode root)
    {
        var functions = new Dictionary<string, LuaStateHandler>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes())
            switch (node)
            {
                case FunctionDeclarationStatementSyntax { Name: SimpleFunctionNameSyntax simple } declared:
                    functions.TryAdd(simple.Name.Text,
                        new LuaStateHandler(simple.Name.Text, declared.Parameters, declared.Body,
                            simple.Name.GetLocation()));
                    break;
                case LocalFunctionDeclarationStatementSyntax local:
                    functions.TryAdd(local.Name.Name,
                        new LuaStateHandler(local.Name.Name, local.Parameters, local.Body, local.Name.GetLocation()));
                    break;
            }

        var registrations = new List<LuaStateRegistration>();
        var transitions = new List<LuaStateTransition>();
        var computed = false;
        foreach (var node in root.DescendantNodes())
            switch (node)
            {
                case FunctionCallExpressionSyntax { Expression: IdentifierNameSyntax callee } call:
                    var arguments = call.Argument is ExpressionListFunctionArgumentSyntax list
                        ? list.Expressions
                        : default;
                    if (callee.Name == "Define_State" && arguments.Count >= 2)
                    {
                        if (StringName(arguments[0]) is { } name)
                            registrations.Add(new LuaStateRegistration(name.Text, name.Location,
                                LuaStateSource.DEFINE_STATE, arguments[1], HandlerOf(arguments[1], functions)));
                        else
                            computed = true;
                    }
                    else if (callee.Name == "Set_Next_State" && arguments.Count >= 1 &&
                             StringName(arguments[0]) is { } target)
                    {
                        transitions.Add(new LuaStateTransition(target.Text, target.Location));
                    }

                    break;
                case AssignmentStatementSyntax assignment:
                    for (var i = 0; i < assignment.Variables.Count && i < assignment.EqualsValues.Values.Count; i++)
                        if (assignment.Variables[i] is IdentifierNameSyntax { Name: "StoryModeEvents" } &&
                            assignment.EqualsValues.Values[i] is TableConstructorExpressionSyntax table)
                            AddStoryEvents(table, functions, registrations);
                    break;
                case IdentifierKeyedTableFieldSyntax { Value: TableConstructorExpressionSyntax nested } field
                    when field.Identifier.Text == "StoryModeEvents":
                    AddStoryEvents(nested, functions, registrations);
                    break;
            }

        return new LuaStateMachine(registrations, transitions, computed, functions);
    }

    /// <summary>
    ///     Every <c>StoryModeEvents</c> entry: the event name and the function or inline function it
    ///     maps to. Shared with the story graph, which reads the same table.
    /// </summary>
    public static IEnumerable<(string State, string? FunctionName, AnonymousFunctionExpressionSyntax? Inline)>
        StoryModeEntries(SyntaxNode root)
    {
        return Read(root).Registrations
            .Where(r => r.Source == LuaStateSource.STORY_MODE_EVENTS)
            .Where(r => r.Value is IdentifierNameSyntax or AnonymousFunctionExpressionSyntax)
            .Select(r => (r.Name, (r.Value as IdentifierNameSyntax)?.Name,
                r.Value as AnonymousFunctionExpressionSyntax));
    }

    private static void AddStoryEvents(TableConstructorExpressionSyntax table,
        IReadOnlyDictionary<string, LuaStateHandler> functions, List<LuaStateRegistration> registrations)
    {
        foreach (var field in table.Fields.OfType<IdentifierKeyedTableFieldSyntax>())
            if (!string.IsNullOrEmpty(field.Identifier.Text))
                registrations.Add(new LuaStateRegistration(field.Identifier.Text, field.Identifier.GetLocation(),
                    LuaStateSource.STORY_MODE_EVENTS, field.Value, HandlerOf(field.Value, functions)));
    }

    private static LuaStateHandler? HandlerOf(ExpressionSyntax value,
        IReadOnlyDictionary<string, LuaStateHandler> functions)
    {
        return value switch
        {
            IdentifierNameSyntax reference => functions.GetValueOrDefault(reference.Name),
            AnonymousFunctionExpressionSyntax inline =>
                new LuaStateHandler(null, inline.Parameters, inline.Body, inline.GetLocation()),
            _ => null
        };
    }

    // A string literal's text and the location of the text inside its quotes.
    private static (string Text, Location Location)? StringName(ExpressionSyntax expression)
    {
        if (expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            return null;
        var text = literal.Token.ValueText;
        var span = literal.Token.Span;
        var inside = new Loretta.CodeAnalysis.Text.TextSpan(span.Start + 1, Math.Max(0, span.Length - 2));
        return (text, Location.Create(literal.SyntaxTree, inside));
    }
}