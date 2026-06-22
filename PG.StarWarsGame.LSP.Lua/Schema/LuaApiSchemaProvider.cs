// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

namespace PG.StarWarsGame.LSP.Lua.Schema;

public sealed partial class LuaApiSchemaProvider : ILuaApiSchemaProvider
{
    private readonly IReadOnlyDictionary<string, LuaClassDefinition> _classes;
    private readonly IReadOnlyDictionary<string, FunctionEntry> _functions;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<LuaTypeMember>> _typeMembers;

    public LuaApiSchemaProvider(IEnumerable<string> fileContents)
    {
        var functions = new Dictionary<string, FunctionEntry>(StringComparer.OrdinalIgnoreCase);
        var typeMembers = new Dictionary<string, List<LuaTypeMember>>(StringComparer.OrdinalIgnoreCase);
        var classes = new Dictionary<string, LuaClassDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in fileContents)
            ParseContent(content, functions, typeMembers, classes);
        _functions = functions;
        _typeMembers = typeMembers.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<LuaTypeMember>)kvp.Value,
            StringComparer.OrdinalIgnoreCase);
        _classes = classes;
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

    public LuaClassDefinition? GetClassDefinition(string typeName)
    {
        return _classes.GetValueOrDefault(typeName);
    }

    private static void ParseContent(
        string content,
        Dictionary<string, FunctionEntry> functions,
        Dictionary<string, List<LuaTypeMember>> typeMembers,
        Dictionary<string, LuaClassDefinition> classes)
    {
        // Accumulate comment lines (--- stripped) for EmmyLuaAnnotationParser.
        var commentLines = new List<string>();
        // Track @xmlref pairing: each entry is (paramIndex, rawXmlRefToken).
        // @xmlref applies to the IMMEDIATELY PRECEDING @param, so we track the
        // current param index as we encounter @param lines.
        var xmlRefPairs = new List<(int ParamIndex, string RawToken)>();
        var paramCount  = 0;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimStart();

            if (line.StartsWith("---@param", StringComparison.Ordinal))
            {
                commentLines.Add(line[3..].TrimStart(' ', '\t'));
                paramCount++;
            }
            else if (line.StartsWith("---@xmlref", StringComparison.Ordinal))
            {
                // Pair with the most-recently-seen @param (paramCount - 1, 0-indexed)
                if (paramCount > 0)
                {
                    var token    = line[3..].Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    var rawToken = token.Length > 1 ? token[1].Trim() : "";
                    var commentI = rawToken.IndexOf("--", StringComparison.Ordinal);
                    if (commentI >= 0) rawToken = rawToken[..commentI].Trim();
                    xmlRefPairs.Add((paramCount - 1, rawToken));
                }
                // Feed to parser as-is so it silently skips @xmlref (Tier 3)
                commentLines.Add(line[3..].TrimStart(' ', '\t'));
            }
            else if (line.StartsWith("---", StringComparison.Ordinal))
            {
                var stripped = line[3..];
                if (stripped.Length > 0 && (stripped[0] == ' ' || stripped[0] == '\t'))
                    stripped = stripped[1..];
                commentLines.Add(stripped);
            }
            else if (line.StartsWith("function ", StringComparison.Ordinal))
            {
                var annotations = EmmyLuaAnnotationParser.Parse(commentLines);

                // Try member function: TypeName.Method or TypeName:Method
                var memberMatch = MemberFunctionDeclRegex().Match(line);
                if (memberMatch.Success)
                {
                    var typeName   = memberMatch.Groups["type"].Value;
                    var methodName = memberMatch.Groups["method"].Value;
                    var isMethod   = memberMatch.Groups["sep"].Value == ":";

                    if (!typeMembers.TryGetValue(typeName, out var memberList))
                        typeMembers[typeName] = memberList = [];

                    var retType = annotations.Returns.IsDefaultOrEmpty
                        ? null : annotations.Returns[0].Type.Raw;
                    memberList.Add(new LuaTypeMember(methodName, isMethod, annotations.Description, retType));
                }
                else
                {
                    var match = FunctionDeclRegex().Match(line);
                    if (match.Success)
                    {
                        var name    = match.Groups["name"].Value;
                        var xmlRefs = BuildXmlRefs(xmlRefPairs);
                        var retType = annotations.Returns.IsDefaultOrEmpty
                            ? null : annotations.Returns[0].Type.Raw;
                        functions[name] = new FunctionEntry(xmlRefs, annotations.Description, retType);
                    }
                }

                commentLines.Clear();
                xmlRefPairs.Clear();
                paramCount = 0;
            }
            else
            {
                if (line.Length == 0 || !line.StartsWith("---", StringComparison.Ordinal))
                {
                    if (commentLines.Count > 0)
                    {
                        var ann = EmmyLuaAnnotationParser.Parse(commentLines);
                        if (ann.ClassDef is { } cls)
                            classes[cls.Name] = cls;
                    }

                    commentLines.Clear();
                    xmlRefPairs.Clear();
                    paramCount = 0;
                }
            }
        }
    }

    private static List<XmlRefEntry> BuildXmlRefs(List<(int ParamIndex, string RawToken)> pairs)
    {
        var result = new List<XmlRefEntry>(pairs.Count);
        foreach (var (paramIdx, rawToken) in pairs)
        {
            string? typeConstraint = null;
            var colonI = rawToken.IndexOf(':', StringComparison.Ordinal);
            if (colonI >= 0)
                typeConstraint = rawToken[(colonI + 1)..].Trim();
            result.Add(new XmlRefEntry(paramIdx, typeConstraint?.Length == 0 ? null : typeConstraint));
        }
        return result;
    }

    [GeneratedRegex(@"^function\s+(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex FunctionDeclRegex();

    [GeneratedRegex(@"^function\s+(?<type>[A-Za-z_]\w*)(?<sep>[.:])(?<method>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex MemberFunctionDeclRegex();

    private sealed record FunctionEntry(
        IReadOnlyList<XmlRefEntry> XmlRefs,
        string? Description,
        string? ReturnTypeName);
}