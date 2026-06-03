// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Completion;

internal static class LuaCompletionContextClassifier
{
    public static LuaCompletionContext? Classify(string text, int line, int character)
    {
        var textLines = text.Split('\n');
        if (line >= textLines.Length)
            return new IdentifierContext(true);

        var lineText = textLines[line].TrimEnd('\r');
        var col = Math.Min(character, lineText.Length);

        // 1. String arg context takes highest priority
        var stringCtx = TryStringArgContext(lineText, col);
        if (stringCtx is not null)
            return stringCtx;

        // 2. Inside a string but not a function arg → no completions
        if (IsInsideStringLiteral(lineText, col))
            return null;

        // 3. Cursor immediately after . or :
        if (col > 0)
        {
            var prevChar = lineText[col - 1];
            if (prevChar == '.')
                return new MemberAccessContext(ExtractIdentifierBefore(lineText, col - 2), false);
            if (prevChar == ':')
                return new MemberAccessContext(ExtractIdentifierBefore(lineText, col - 2), true);
        }

        // 4. Identifier context
        return new IdentifierContext(IsAtStatementStart(lineText, col));
    }

    // Detects whether the cursor is inside a string literal that is an argument
    // to a function call (e.g., require("...") or Find_Player("...")).
    private static StringArgContext? TryStringArgContext(string line, int col)
    {
        var bound = Math.Min(col, line.Length);

        // Walk backward to find the opening quote of the string we may be inside
        var i = bound - 1;
        while (i >= 0)
        {
            var ch = line[i];
            if (ch is '"' or '\'') break;
            if (ch is ')' or ';') return null;
            i--;
        }

        if (i < 0) return null;
        var quotePos = i;

        // Walk backward from the opening quote counting commas and tracking depth
        i = quotePos - 1;
        var paramIndex = 0;
        var depth = 0;

        while (i >= 0)
        {
            var ch = line[i];
            if (ch is ')' or ']' or '}')
            {
                depth++;
                i--;
                continue;
            }

            if (ch is '(' or '[' or '{')
            {
                if (depth > 0)
                {
                    depth--;
                    i--;
                    continue;
                }

                break;
            }

            if (ch == ',' && depth == 0) paramIndex++;
            if (ch is ';' or '\n') return null;
            i--;
        }

        if (i < 0) return null;
        var parenPos = i;

        // Extract function name immediately before the open paren
        i = parenPos - 1;
        while (i >= 0 && char.IsWhiteSpace(line[i])) i--;
        var nameEnd = i;
        if (nameEnd < 0) return null;

        while (i >= 0 && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i--;
        var nameStart = i + 1;

        if (nameStart > nameEnd) return null;
        return new StringArgContext(line[nameStart..(nameEnd + 1)], paramIndex);
    }

    // Returns true when the cursor is inside a string literal (even one not inside a call).
    private static bool IsInsideStringLiteral(string line, int col)
    {
        char? openQuote = null;
        for (var i = 0; i < col && i < line.Length; i++)
        {
            var c = line[i];
            if (openQuote is null)
            {
                if (c is '"' or '\'') openQuote = c;
            }
            else if (c == openQuote && (i == 0 || line[i - 1] != '\\'))
            {
                openQuote = null;
            }
        }

        return openQuote is not null;
    }

    // Extracts the identifier that ends at or before `pos` in the line.
    private static string? ExtractIdentifierBefore(string line, int pos)
    {
        if (pos < 0) return null;
        while (pos >= 0 && char.IsWhiteSpace(line[pos])) pos--;
        if (pos < 0) return null;
        var end = pos;
        while (pos >= 0 && (char.IsLetterOrDigit(line[pos]) || line[pos] == '_')) pos--;
        var start = pos + 1;
        return start <= end ? line[start..(end + 1)] : null;
    }

    // Returns true when the cursor is at the start of a new statement (only identifier
    // characters precede it on the line, after stripping leading whitespace).
    private static bool IsAtStatementStart(string line, int col)
    {
        var prefix = line[..Math.Min(col, line.Length)].TrimStart();
        return prefix.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}