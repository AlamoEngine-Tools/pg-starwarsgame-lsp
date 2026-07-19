// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>
///     Emits the story-mode symbols a Lua script contributes to the cross-language index:
///     <c>StoryModeEvents</c> table keys mirror XML event names (references of TypeName
///     <c>StoryEvent</c> — the PGStateMachine dispatches <c>Story_Event_Trigger(name)</c> into
///     this table), and <c>Story_Event("X")</c> string arguments define the
///     <c>StoryNotification</c> ids that XML <c>STORY_AI_NOTIFICATION</c> events listen for.
///     Flag reads (<c>Check_Story_Flag</c>) ride the ordinary <c>---@xmlref</c> reference path.
/// </summary>
internal static class LuaStorySymbolCollector
{
    public static void Collect(SyntaxNode root, string documentUri,
        List<GameSymbol> symbols, List<GameReference> references)
    {
        // The same notification id is commonly fired from several places in one script; the
        // first occurrence is the definition, the xmlref-driven references cover the rest.
        var seenNotifications = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in root.DescendantNodes())
            switch (node)
            {
                // Top-level form: StoryModeEvents = { ... }
                case AssignmentStatementSyntax assignment:
                {
                    for (var i = 0; i < assignment.Variables.Count && i < assignment.EqualsValues.Values.Count; i++)
                        if (assignment.Variables[i] is IdentifierNameSyntax { Name: "StoryModeEvents" } &&
                            assignment.EqualsValues.Values[i] is TableConstructorExpressionSyntax table)
                            EmitEventKeyReferences(table, documentUri, references);
                    break;
                }
                // Nested form (vanilla): Definitions = { StoryModeEvents = { ... } }
                case IdentifierKeyedTableFieldSyntax { Value: TableConstructorExpressionSyntax nested } field
                    when field.Identifier.Text == "StoryModeEvents":
                    EmitEventKeyReferences(nested, documentUri, references);
                    break;
                case FunctionCallExpressionSyntax call
                    when call.Expression is IdentifierNameSyntax { Name: "Story_Event" }:
                {
                    if (TryExtractFirstStringArgument(call) is not var (id, line, column)) break;
                    if (!seenNotifications.Add(id)) break;
                    symbols.Add(new GameSymbol(id, GameSymbolKind.XmlObject,
                        StoryReferenceTypes.NotificationSymbol,
                        new FileOrigin(documentUri, line, column), null));
                    break;
                }
            }
    }

    private static void EmitEventKeyReferences(TableConstructorExpressionSyntax table,
        string documentUri, List<GameReference> references)
    {
        foreach (var field in table.Fields.OfType<IdentifierKeyedTableFieldSyntax>())
        {
            var name = field.Identifier.Text;
            if (string.IsNullOrEmpty(name)) continue;
            var position = field.Identifier.GetLocation().GetLineSpan().StartLinePosition;
            references.Add(new GameReference(name, GameSymbolKind.XmlObject,
                StoryReferenceTypes.EventSymbol, documentUri,
                position.Line, position.Character, name.Length));
        }
    }

    private static (string Id, int Line, int Column)? TryExtractFirstStringArgument(
        FunctionCallExpressionSyntax call)
    {
        var literal = call.Argument switch
        {
            ExpressionListFunctionArgumentSyntax { Expressions: [LiteralExpressionSyntax lit, ..] } => lit,
            StringFunctionArgumentSyntax stringArg => stringArg.Expression,
            _ => null
        };

        if (literal is null || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            return null;

        var value = literal.Token.ValueText;
        if (string.IsNullOrEmpty(value)) return null;

        var span = literal.Token.GetLocation().GetLineSpan().StartLinePosition;
        // +1 skips the opening quote so the span covers exactly the id.
        return (value, span.Line, span.Character + 1);
    }
}