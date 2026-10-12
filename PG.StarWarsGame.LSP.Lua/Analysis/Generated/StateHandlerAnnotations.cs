// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Parsing;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Generated;

/// <summary>
///     A state handler's message is one of three values: the state machine library calls it with
///     OnEnter (1), OnUpdate (2) and OnExit (3) and nothing else (#252). Typed for the analyzer on
///     every handler the script registers by name, where its library keeps the game's contract.
/// </summary>
public sealed class StateHandlerAnnotations(
    IGameIndexService index,
    ILuaParseCache parseCache,
    IFileHelper fileHelper) : ILuaAnnotationSource
{
    public IEnumerable<LuaAnnotationInsertion> For(SyntaxTree tree, string uri)
    {
        var machine = LuaStateMachine.Read(tree.GetRoot());
        if (machine.Registrations.Count == 0) yield break;
        var contract = LuaStateMachineContract.For(uri, index.Current.Documents, parseCache, fileHelper);
        if (!contract.StateMachine) yield break;

        var text = tree.GetText();
        var annotated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var registration in machine.Registrations)
        {
            if (registration.Source == LuaStateSource.STORY_MODE_EVENTS && !contract.StoryMode) continue;
            if (registration.Handler is not { Name: { } name } handler || !annotated.Add(name)) continue;
            if (handler.Parameters.Parameters.FirstOrDefault() is not NamedParameterSyntax message) continue;

            var line = handler.Parameters.Parent!.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (LuaAnnotations.AuthorAnnotated(text, line)) continue;
            yield return new LuaAnnotationInsertion(line,
                [$"---@param {message.Identifier.Text} 1|2|3 OnEnter (1), OnUpdate (2) or OnExit (3)"]);
        }
    }
}