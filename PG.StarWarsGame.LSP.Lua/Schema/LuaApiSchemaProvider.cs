// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

namespace PG.StarWarsGame.LSP.Lua.Schema;

public sealed partial class LuaApiSchemaProvider : ILuaApiSchemaProvider
{
    private readonly IReadOnlyDictionary<string, FunctionEntry> _functions;

    // Reference tags on class methods, keyed by the bare method name: a call site sees
    // `obj.Play_SFX_Event("x")` and nothing about obj's type, so the method name is the key.
    private readonly IReadOnlyDictionary<string, IReadOnlyList<XmlRefEntry>> _memberRefs;

    // Entry points are documentation, kept apart from the stubs: no declared global, no fingerprint
    // (nothing an index records depends on them).
    private readonly IReadOnlyDictionary<string, LuaEntryPoint> _entryPoints;

    public LuaApiSchemaProvider(IEnumerable<string> fileContents, IEnumerable<LuaEntryPoint>? entryPoints = null)
    {
        _entryPoints = (entryPoints ?? []).GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var contents = fileContents.ToList();
        var functions = new Dictionary<string, FunctionEntry>(StringComparer.OrdinalIgnoreCase);
        var memberRefs = new Dictionary<string, List<XmlRefEntry>>(StringComparer.OrdinalIgnoreCase);
        var declaredGlobals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in contents)
            ParseContent(content, functions, memberRefs, declaredGlobals);
        Fingerprint = contents.Count == 0
            ? ""
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', contents))));
        _functions = functions;
        _memberRefs = memberRefs.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<XmlRefEntry>)kvp.Value,
            StringComparer.OrdinalIgnoreCase);
        AllFunctionNames = new HashSet<string>(functions.Keys, StringComparer.OrdinalIgnoreCase);
        declaredGlobals.UnionWith(functions.Keys);
        DeclaredGlobalNames = declaredGlobals;
    }

    public IReadOnlySet<string> AllFunctionNames { get; }

    public IReadOnlySet<string> DeclaredGlobalNames { get; }

    public string Fingerprint { get; }

    public IReadOnlyList<XmlRefEntry> GetXmlRefs(string functionName)
    {
        if (_functions.TryGetValue(functionName, out var entry)) return entry.XmlRefs;
        return _memberRefs.TryGetValue(functionName, out var refs) ? refs : [];
    }

    public string? GetFunctionDescription(string functionName)
    {
        return _functions.TryGetValue(functionName, out var entry) ? entry.Description : null;
    }

    public string? GetReturnTypeName(string functionName)
    {
        return _functions.TryGetValue(functionName, out var entry) ? entry.ReturnTypeName : null;
    }

    public IReadOnlyList<LuaParamAnnotation> GetFunctionParams(string functionName)
    {
        return _functions.TryGetValue(functionName, out var entry) ? entry.Params : [];
    }

    public LuaEntryPoint? GetEntryPoint(string functionName)
    {
        return _entryPoints.GetValueOrDefault(functionName);
    }

    private static void ParseContent(
        string content,
        Dictionary<string, FunctionEntry> functions,
        Dictionary<string, List<XmlRefEntry>> memberRefs,
        HashSet<string> declaredGlobals)
    {
        // Accumulate comment lines (--- stripped) for EmmyLuaAnnotationParser.
        var commentLines = new List<string>();
        // A reference tag applies to the IMMEDIATELY PRECEDING @param, so the current param index
        // is tracked as @param lines go by.
        var refs = new List<XmlRefEntry>();
        var paramCount = 0;
        IReadOnlyList<string> paramLiterals = [];

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimStart();

            if (line.StartsWith("---@param", StringComparison.Ordinal))
            {
                commentLines.Add(line[3..].TrimStart(' ', '\t'));
                paramCount++;
                paramLiterals = ParamLiterals(line);
            }
            else if (line.StartsWith("---@aetref", StringComparison.Ordinal))
            {
                if (paramCount > 0 && TryParseReferenceTag(line, paramCount - 1, paramLiterals) is { } entry)
                    refs.Add(entry);
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

                // A member function (TypeName.Method or TypeName:Method) contributes its reference
                // tags only; what the type offers is the analyzer's to complete (#155).
                var memberMatch = MemberFunctionDeclRegex().Match(line);
                if (memberMatch.Success)
                {
                    var methodName = memberMatch.Groups["method"].Value;
                    if (refs.Count > 0)
                    {
                        if (!memberRefs.TryGetValue(methodName, out var refList))
                            memberRefs[methodName] = refList = [];
                        refList.AddRange(refs);
                    }
                }
                else
                {
                    var match = FunctionDeclRegex().Match(line);
                    if (match.Success)
                    {
                        var name = match.Groups["name"].Value;
                        var retType = annotations.Returns.IsDefaultOrEmpty
                            ? null
                            : annotations.Returns[0].Type.Raw;
                        var @params = annotations.Params.IsDefaultOrEmpty
                            ? (IReadOnlyList<LuaParamAnnotation>)[]
                            : [.. annotations.Params];
                        functions[name] = new FunctionEntry([.. refs], annotations.Description, retType, @params);
                    }
                }

                commentLines.Clear();
                refs.Clear();
                paramCount = 0;
            }
            else
            {
                // A top-level assignment declares a global value (`Script = nil`, `string = {}`).
                // Stub files have no nesting, so every assignment line is top level.
                var assignment = GlobalAssignmentRegex().Match(line);
                if (assignment.Success)
                    declaredGlobals.Add(assignment.Groups["name"].Value);

                if (line.Length == 0 || !line.StartsWith("---", StringComparison.Ordinal))
                {
                    commentLines.Clear();
                    refs.Clear();
                    paramCount = 0;
                }
            }
        }
    }

    // `---@aetref <ReferenceKind>[:<referenceType>]`. An unknown kind drops the tag rather than guessing.
    private static XmlRefEntry? TryParseReferenceTag(string line, int paramIndex, IReadOnlyList<string> literals)
    {
        var token = line[3..].Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var rawToken = token.Length > 1 ? token[1].Trim() : "";
        var commentI = rawToken.IndexOf("--", StringComparison.Ordinal);
        if (commentI >= 0) rawToken = rawToken[..commentI].Trim();
        if (rawToken.Length == 0) return null;

        string kindName;
        string? typeConstraint = null;
        var colonI = rawToken.IndexOf(':', StringComparison.Ordinal);
        if (colonI >= 0)
        {
            kindName = rawToken[..colonI].Trim();
            typeConstraint = rawToken[(colonI + 1)..].Trim();
        }
        else
        {
            kindName = rawToken;
        }

        if (!Enum.TryParse<ReferenceKind>(kindName, true, out var kind) || kind == ReferenceKind.None)
            return null;
        return new XmlRefEntry(paramIndex, typeConstraint?.Length == 0 ? null : typeConstraint, kind,
            literals.Count == 0 ? null : literals);
    }

    // The string literals of a `---@param <name> <type> [doc]` line's type: `string|"local"` gives
    // ["local"]. Only the type token is read, so a quoted word in the description is not one.
    private static IReadOnlyList<string> ParamLiterals(string line)
    {
        var parts = line[3..].Trim().Split((char[])[' ', '\t'], 4, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return [];
        return [.. ParamLiteral().Matches(parts[2]).Select(m => m.Groups["value"].Value)];
    }

    [GeneratedRegex("\"(?<value>[^\"]*)\"")]
    private static partial Regex ParamLiteral();

    [GeneratedRegex(@"^function\s+(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex FunctionDeclRegex();

    [GeneratedRegex(@"^function\s+[A-Za-z_]\w*[.:](?<method>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex MemberFunctionDeclRegex();

    [GeneratedRegex(@"^(?<name>[A-Za-z_]\w*)\s*=")]
    private static partial Regex GlobalAssignmentRegex();

    private sealed record FunctionEntry(
        IReadOnlyList<XmlRefEntry> XmlRefs,
        string? Description,
        string? ReturnTypeName,
        IReadOnlyList<LuaParamAnnotation> Params);
}