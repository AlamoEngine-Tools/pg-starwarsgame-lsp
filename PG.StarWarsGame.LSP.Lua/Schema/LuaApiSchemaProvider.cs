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

    // `---@type Player` above `PlayerObject = nil`, engine stubs only.
    private readonly IReadOnlyDictionary<string, string> _globalTypes;

    // The same for every stub, a library's included, with the description above it.
    private readonly IReadOnlyDictionary<string, LuaGlobalAnnotation> _globalAnnotations;

    /// <summary>
    ///     The three stub files that describe what the ENGINE binds into every script. Everything
    ///     else the manifest lists documents a vanilla library - names a script gets only by
    ///     requiring that library, so they are known for hover and parameters, never declared.
    /// </summary>
    private static readonly HashSet<string> EngineStubFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.d.lua", "stdlib.d.lua", "globals.d.lua"
    };

    /// <summary>Unnamed content: the tests' shorthand, counted as the engine's.</summary>
    public LuaApiSchemaProvider(IEnumerable<string> fileContents, IEnumerable<LuaEntryPoint>? entryPoints = null)
        : this(fileContents.Select(c => new LuaStubFile("api.d.lua", c)).ToList(), entryPoints)
    {
    }

    /// <summary>
    ///     The provider over the stub files as the manifest lists them, each with its name - the
    ///     way the server loads them. A factory rather than a second constructor so that an empty
    ///     collection expression still resolves to the unnamed-content form.
    /// </summary>
    public static LuaApiSchemaProvider FromFiles(
        IEnumerable<LuaStubFile> files, IEnumerable<LuaEntryPoint>? entryPoints = null)
    {
        return new LuaApiSchemaProvider(files.ToList(), entryPoints);
    }

    private LuaApiSchemaProvider(List<LuaStubFile> stubs, IEnumerable<LuaEntryPoint>? entryPoints)
    {
        _entryPoints = (entryPoints ?? []).GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var functions = new Dictionary<string, FunctionEntry>(StringComparer.OrdinalIgnoreCase);
        var memberRefs = new Dictionary<string, List<XmlRefEntry>>(StringComparer.OrdinalIgnoreCase);
        var declaredGlobals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var globalTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var globalAnnotations = new Dictionary<string, LuaGlobalAnnotation>(StringComparer.OrdinalIgnoreCase);
        var engineFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stub in stubs)
        {
            // A library stub's typed globals and functions are documentation; only the engine's
            // own files declare what every script can call.
            var isEngine = EngineStubFiles.Contains(stub.Name);
            var found = new Dictionary<string, FunctionEntry>(StringComparer.OrdinalIgnoreCase);
            ParseContent(stub.Content, found, memberRefs, isEngine ? declaredGlobals : [],
                isEngine ? globalTypes : new Dictionary<string, string>(), globalAnnotations);
            foreach (var (name, entry) in found)
            {
                functions[name] = entry;
                if (isEngine) engineFunctions.Add(name);
            }
        }

        Fingerprint = stubs.Count == 0
            ? ""
            : Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', stubs.Select(s => s.Content)))));
        _functions = functions;
        _memberRefs = memberRefs.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<XmlRefEntry>)kvp.Value,
            StringComparer.OrdinalIgnoreCase);
        AllFunctionNames = new HashSet<string>(functions.Keys, StringComparer.OrdinalIgnoreCase);
        declaredGlobals.UnionWith(engineFunctions);
        DeclaredGlobalNames = declaredGlobals;
        _globalTypes = globalTypes;
        _globalAnnotations = globalAnnotations;
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

    public IReadOnlyList<IReadOnlyList<LuaParamAnnotation>> GetCallForms(string functionName)
    {
        return _functions.TryGetValue(functionName, out var entry) ? entry.Forms : [];
    }

    public string? GetGlobalTypeName(string globalName)
    {
        return _globalTypes.GetValueOrDefault(globalName);
    }

    public LuaGlobalAnnotation? GetGlobalAnnotation(string globalName)
    {
        return _globalAnnotations.GetValueOrDefault(globalName);
    }

    public LuaEntryPoint? GetEntryPoint(string functionName)
    {
        return _entryPoints.GetValueOrDefault(functionName);
    }

    private static void ParseContent(
        string content,
        Dictionary<string, FunctionEntry> functions,
        Dictionary<string, List<XmlRefEntry>> memberRefs,
        HashSet<string> declaredGlobals,
        Dictionary<string, string> globalTypes,
        Dictionary<string, LuaGlobalAnnotation> globalAnnotations)
    {
        // Accumulate comment lines (--- stripped) for EmmyLuaAnnotationParser.
        var commentLines = new List<string>();
        // A reference tag applies to the IMMEDIATELY PRECEDING @param, so the current param index
        // is tracked as @param lines go by.
        var refs = new List<XmlRefEntry>();
        var paramCount = 0;
        IReadOnlyList<string> paramLiterals = [];
        // A function's call forms (`---@overload fun(...)`); an engine table's class (`---@class X`
        // above `X = {}`), whose overload is the table's own call and no form; a value's `---@type`.
        var overloads = new List<IReadOnlyList<LuaParamAnnotation>>();
        string? className = null;
        string? typeName = null;

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
            else if (line.StartsWith("---@overload", StringComparison.Ordinal))
            {
                if (OverloadRegex().Match(line) is { Success: true } overload)
                    overloads.Add(ParseFormParams(overload.Groups["params"].Value));
            }
            else if (line.StartsWith("---@class", StringComparison.Ordinal))
            {
                className = line[9..].Trim().Split(' ', ':')[0];
            }
            else if (line.StartsWith("---@type", StringComparison.Ordinal))
            {
                typeName = line[8..].Trim().Split(' ')[0];
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
                        functions[name] = new FunctionEntry([.. refs], annotations.Description, retType, @params,
                            [.. overloads]);
                    }
                }

                Reset();
            }
            else
            {
                // A top-level assignment declares a global value (`Script = nil`, `string = {}`).
                // Stub files have no nesting, so every assignment line is top level.
                var assignment = GlobalAssignmentRegex().Match(line);
                if (assignment.Success)
                {
                    var name = assignment.Groups["name"].Value;
                    declaredGlobals.Add(name);
                    if (typeName is not null)
                    {
                        globalTypes[name] = typeName;
                        var description = EmmyLuaAnnotationParser.Parse(commentLines).Description;
                        globalAnnotations[name] = new LuaGlobalAnnotation(typeName,
                            string.IsNullOrEmpty(description) ? null : description);
                    }

                    // An engine table (`---@class FogOfWar` ... `FogOfWar = {}`) is also callable: its
                    // @param and @aetref lines describe the call, as a function's would.
                    if (string.Equals(className, name, StringComparison.Ordinal))
                    {
                        var annotations = EmmyLuaAnnotationParser.Parse(commentLines);
                        functions[name] = new FunctionEntry([.. refs], annotations.Description,
                            annotations.Returns.IsDefaultOrEmpty ? null : annotations.Returns[0].Type.Raw,
                            annotations.Params.IsDefaultOrEmpty ? [] : [.. annotations.Params], []);
                    }
                }

                if (line.Length == 0 || !line.StartsWith("---", StringComparison.Ordinal))
                    Reset();
            }
        }

        return;

        void Reset()
        {
            commentLines.Clear();
            refs.Clear();
            paramCount = 0;
            overloads.Clear();
            className = null;
            typeName = null;
        }
    }

    // `name: Type, other?: Type, ...: Type` -> parameters. A name and its type are split at the first
    // `: `; types themselves hold no top-level comma in the stubs (no `fun(...)` or table types here).
    private static IReadOnlyList<LuaParamAnnotation> ParseFormParams(string text)
    {
        return
        [
            .. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p =>
                {
                    var colon = p.IndexOf(':', StringComparison.Ordinal);
                    var name = colon < 0 ? p : p[..colon].Trim();
                    var type = colon < 0 ? "any" : p[(colon + 1)..].Trim();
                    return new LuaParamAnnotation(name.TrimEnd('?'), name.EndsWith('?'), new LuaTypeRef(type), null);
                })
        ];
    }

    [GeneratedRegex(@"^---@overload\s+fun\((?<params>[^)]*)\)")]
    private static partial Regex OverloadRegex();

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
        IReadOnlyList<LuaParamAnnotation> Params,
        IReadOnlyList<IReadOnlyList<LuaParamAnnotation>> Forms);
}