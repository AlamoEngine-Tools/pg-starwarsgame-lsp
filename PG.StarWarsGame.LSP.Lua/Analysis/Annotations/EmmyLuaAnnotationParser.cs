// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

/// <summary>
///     Reads the prose, <c>@param</c> and <c>@return</c> lines of an EmmyLua/LuaCATS doc-comment
///     block. Every other tag is the analyzer's to read and is skipped here (#155).
/// </summary>
/// <remarks>
///     Input: comment lines with the leading <c>---</c> marker already stripped by the caller.
///     Lines that start with <c>@</c> are annotation lines; all others are prose description.
/// </remarks>
public static class EmmyLuaAnnotationParser
{
    private static readonly char[] s_spaceSep = [' ', '\t'];

    public static EmmyLuaAnnotations Parse(IReadOnlyList<string> commentLines)
    {
        if (commentLines.Count == 0)
            return EmmyLuaAnnotations.Empty;

        var proseLines = new List<string>();
        var parameters = new List<LuaParamAnnotation>();
        var returns = new List<LuaReturnAnnotation>();

        foreach (var rawLine in commentLines)
        {
            var line = rawLine.TrimStart();
            if (line.StartsWith("@param", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseParam(line, out var p)) parameters.Add(p);
            }
            else if (line.StartsWith("@return", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseReturn(line, out var r)) returns.Add(r);
            }
            else if (!line.StartsWith('@') && line.Length > 0)
            {
                proseLines.Add(line);
            }
        }

        return new EmmyLuaAnnotations(
            proseLines.Count > 0 ? string.Join("\n", proseLines) : null,
            [.. parameters],
            [.. returns]);
    }

    private static bool TryParseParam(string line, out LuaParamAnnotation result)
    {
        // @param name[?] Type [description]
        var parts = line["@param".Length..].TrimStart().Split(s_spaceSep, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1)
        {
            result = default!;
            return false;
        }

        var namePart = parts[0];
        var isOptional = namePart.EndsWith('?');
        var name = isOptional ? namePart[..^1] : namePart;
        var type = parts.Length >= 2 ? new LuaTypeRef(parts[1]) : LuaTypeRef.Unknown;
        var desc = parts.Length >= 3 ? parts[2].Trim() : null;

        result = new LuaParamAnnotation(name, isOptional, type, string.IsNullOrEmpty(desc) ? null : desc);
        return true;
    }

    private static bool TryParseReturn(string line, out LuaReturnAnnotation result)
    {
        // @return Type [name] [description]
        var parts = line["@return".Length..].TrimStart().Split(s_spaceSep, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1)
        {
            result = default!;
            return false;
        }

        var type = new LuaTypeRef(parts[0]);
        var name = parts.Length >= 2 ? parts[1] : null;
        var desc = parts.Length >= 3 ? parts[2].Trim() : null;

        result = new LuaReturnAnnotation(type, string.IsNullOrEmpty(name) ? null : name,
            string.IsNullOrEmpty(desc) ? null : desc);
        return true;
    }
}