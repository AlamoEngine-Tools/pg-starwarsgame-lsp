// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Lua.Schema;

public sealed partial class LuaApiSchemaProvider : ILuaApiSchemaProvider
{
    private readonly IReadOnlyDictionary<string, FunctionEntry> _functions;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<LuaTypeMember>> _typeMembers;

    public LuaApiSchemaProvider(IEnumerable<string> fileContents)
    {
        var functions = new Dictionary<string, FunctionEntry>(StringComparer.OrdinalIgnoreCase);
        var typeMembers = new Dictionary<string, List<LuaTypeMember>>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in fileContents)
            ParseContent(content, functions, typeMembers);
        _functions = functions;
        _typeMembers = typeMembers.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<LuaTypeMember>)kvp.Value,
            StringComparer.OrdinalIgnoreCase);
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

    public string? GetReturnTypeName(string functionName)
    {
        return _functions.TryGetValue(functionName, out var entry) ? entry.ReturnTypeName : null;
    }

    public IReadOnlyList<LuaTypeMember> GetMembersOf(string typeName)
    {
        return _typeMembers.TryGetValue(typeName, out var members) ? members : [];
    }

    private static void ParseContent(
        string content,
        Dictionary<string, FunctionEntry> functions,
        Dictionary<string, List<LuaTypeMember>> typeMembers)
    {
        var pendingParams = new List<PendingParam>();
        string? pendingDescription = null;
        string? pendingReturnType = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimStart();

            if (line.StartsWith("---@param", StringComparison.Ordinal))
            {
                var parts = line[3..].Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    pendingParams.Add(new PendingParam(parts[1], null));
            }
            else if (line.StartsWith("---@return", StringComparison.Ordinal))
            {
                // ---@return TypeName [optional description]
                var after = line[3..].Trim()["@return".Length..].Trim();
                var typePart = after.Split(' ', 2)[0].Trim();
                if (typePart.Length > 0)
                    pendingReturnType = typePart;
            }
            else if (line.StartsWith("---@xmlref", StringComparison.Ordinal))
            {
                if (pendingParams.Count > 0)
                {
                    var token = line[3..].Trim().Split(' ', 2)[1].Trim();
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
                var text = line[3..].TrimStart();
                if (text.Length > 0)
                    pendingDescription = pendingDescription is null ? text : pendingDescription + " " + text;
            }
            else if (line.StartsWith("function ", StringComparison.Ordinal))
            {
                // Try member function: TypeName.Method or TypeName:Method
                var memberMatch = MemberFunctionDeclRegex().Match(line);
                if (memberMatch.Success)
                {
                    var typeName = memberMatch.Groups["type"].Value;
                    var methodName = memberMatch.Groups["method"].Value;
                    var isMethod = memberMatch.Groups["sep"].Value == ":";

                    if (!typeMembers.TryGetValue(typeName, out var memberList))
                        typeMembers[typeName] = memberList = [];

                    memberList.Add(new LuaTypeMember(methodName, isMethod, pendingDescription, pendingReturnType));
                    pendingParams.Clear();
                    pendingDescription = null;
                    pendingReturnType = null;
                    continue;
                }

                // Simple global function
                var match = FunctionDeclRegex().Match(line);
                if (match.Success)
                {
                    var name = match.Groups["name"].Value;
                    var xmlRefs = new List<XmlRefEntry>();
                    for (var i = 0; i < pendingParams.Count; i++)
                    {
                        if (pendingParams[i].XmlRefTypeName is not { } xmlRef) continue;
                        var typeName = xmlRef.Length == 0 ? null : xmlRef;
                        xmlRefs.Add(new XmlRefEntry(i, typeName));
                    }

                    functions[name] = new FunctionEntry(xmlRefs, pendingDescription, pendingReturnType);
                }

                pendingParams.Clear();
                pendingDescription = null;
                pendingReturnType = null;
            }
            else
            {
                if (line.Length == 0 || !line.StartsWith("---", StringComparison.Ordinal))
                {
                    pendingParams.Clear();
                    pendingDescription = null;
                    pendingReturnType = null;
                }
            }
        }
    }

    [GeneratedRegex(@"^function\s+(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex FunctionDeclRegex();

    [GeneratedRegex(@"^function\s+(?<type>[A-Za-z_]\w*)(?<sep>[.:])(?<method>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex MemberFunctionDeclRegex();

    private sealed record FunctionEntry(
        IReadOnlyList<XmlRefEntry> XmlRefs,
        string? Description,
        string? ReturnTypeName);

    private record struct PendingParam(string Name, string? XmlRefTypeName);
}