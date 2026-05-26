// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Lua.Schema;

public sealed partial class LuaApiSchemaProvider : ILuaApiSchemaProvider
{
    private readonly IReadOnlyDictionary<string, FunctionEntry> _functions;

    public LuaApiSchemaProvider(IEnumerable<string> fileContents)
    {
        var functions = new Dictionary<string, FunctionEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in fileContents)
            ParseContent(content, functions);
        _functions = functions;
        AllFunctionNames = new HashSet<string>(functions.Keys, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> AllFunctionNames { get; }

    public IReadOnlyList<XmlRefEntry> GetXmlRefs(string functionName)
    {
        return _functions.TryGetValue(functionName, out var entry) ? entry.XmlRefs : [];
    }

    public string? GetFunctionDescription(string functionName)
    {
        return _functions.TryGetValue(functionName, out var entry) ? entry.Description : null;
    }

    private static void ParseContent(
        string content,
        Dictionary<string, FunctionEntry> functions)
    {
        var pendingParams = new List<PendingParam>();
        string? pendingDescription = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimStart();

            if (line.StartsWith("---@param", StringComparison.Ordinal))
            {
                // ---@param name type
                var parts = line[3..].Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    pendingParams.Add(new PendingParam(parts[1], null));
            }
            else if (line.StartsWith("---@xmlref", StringComparison.Ordinal))
            {
                // ---@xmlref XmlObject  or  ---@xmlref XmlObject:TypeConstraint
                if (pendingParams.Count > 0)
                {
                    var token = line[3..].Trim().Split(' ', 2)[1].Trim(); // after "@xmlref"
                    // strip any trailing comment
                    var commentIdx = token.IndexOf("--", StringComparison.Ordinal);
                    if (commentIdx >= 0) token = token[..commentIdx].Trim();

                    string? typeConstraint = null;
                    var colonIdx = token.IndexOf(':', StringComparison.Ordinal);
                    if (colonIdx >= 0)
                        typeConstraint = token[(colonIdx + 1)..].Trim();

                    pendingParams[^1] = pendingParams[^1] with { XmlRefTypeName = typeConstraint ?? "" };
                }
            }
            else if (line.StartsWith("---@", StringComparison.Ordinal))
            {
                // other EmmyLua annotation — ignore but don't reset state
            }
            else if (line.StartsWith("---", StringComparison.Ordinal))
            {
                // plain comment line: accumulate as description
                var text = line[3..].TrimStart();
                if (text.Length > 0)
                    pendingDescription = pendingDescription is null ? text : pendingDescription + " " + text;
            }
            else if (line.StartsWith("function ", StringComparison.Ordinal))
            {
                // function Name(params) end  — only simple (non-member) names
                var match = FunctionDeclRegex().Match(line);
                if (match.Success)
                {
                    var name = match.Groups["name"].Value;
                    var xmlRefs = new List<XmlRefEntry>();
                    for (var i = 0; i < pendingParams.Count; i++)
                    {
                        if (pendingParams[i].XmlRefTypeName is not { } xmlRef)
                            continue;
                        var typeName = xmlRef.Length == 0 ? null : xmlRef;
                        xmlRefs.Add(new XmlRefEntry(i, typeName));
                    }

                    functions[name] = new FunctionEntry(xmlRefs, pendingDescription);
                }

                pendingParams.Clear();
                pendingDescription = null;
            }
            else
            {
                // blank line, regular code, or type alias — reset
                if (line.Length == 0 || !line.StartsWith("---", StringComparison.Ordinal))
                {
                    pendingParams.Clear();
                    pendingDescription = null;
                }
            }
        }
    }

    [GeneratedRegex(@"^function\s+(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex FunctionDeclRegex();

    private sealed record FunctionEntry(
        IReadOnlyList<XmlRefEntry> XmlRefs,
        string? Description);

    private record struct PendingParam(string Name, string? XmlRefTypeName);
}