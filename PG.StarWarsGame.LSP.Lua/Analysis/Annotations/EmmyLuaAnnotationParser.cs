// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

/// <summary>
/// Parses EmmyLua/LuaCATS annotation lines into a structured <see cref="EmmyLuaAnnotations"/> record.
/// </summary>
/// <remarks>
/// Input: comment lines with the leading <c>---</c> marker already stripped by the caller.
/// Lines that start with <c>@</c> are annotation lines; all others are prose description.
/// Unknown or Tier-3 annotations are silently ignored.
/// </remarks>
public static class EmmyLuaAnnotationParser
{
    private static readonly char[] s_spaceSep = [' ', '\t'];

    public static EmmyLuaAnnotations Parse(IReadOnlyList<string> commentLines)
    {
        if (commentLines.Count == 0)
            return EmmyLuaAnnotations.Empty;

        var proseLines         = new List<string>();
        var pendingParams      = new List<LuaParamAnnotation>();
        var pendingReturns     = new List<LuaReturnAnnotation>();
        var overloads          = new List<string>();
        var genericParams      = new List<string>();
        var seeRefs            = new List<string>();
        LuaClassDefinition? classDef       = null;
        LuaAliasDefinition? aliasDef       = null;
        LuaEnumDefinition?  enumDef        = null;
        LuaTypeRef?         typeAnnotation = null;
        var isDeprecated = false;
        var isNodiscard  = false;
        var isAsync      = false;
        LuaAccessModifier? accessModifier = null;

        // Mutable state for multi-line constructs
        string?              pendingClassName   = null;
        bool                 pendingClassExact  = false;
        List<string>?        pendingClassParents = null;
        List<LuaFieldDefinition>? pendingFields = null;
        string?              pendingClassDesc   = null;

        bool          pendingAliasOpen     = false;
        string?       pendingAliasName     = null;
        List<string>? pendingAliasVariants = null;

        foreach (var rawLine in commentLines)
        {
            var line = rawLine.TrimStart();

            // ── union alias continuation (lines starting with |) ──────────────
            if (pendingAliasOpen && line.StartsWith("|", StringComparison.Ordinal))
            {
                (pendingAliasVariants ??= []).Add(line[1..].Trim());
                continue;
            }

            // ── annotation line ───────────────────────────────────────────────
            if (line.StartsWith("@", StringComparison.Ordinal))
            {
                // Flush pending alias if a non-| line appears
                if (pendingAliasOpen)
                {
                    aliasDef = BuildAlias(pendingAliasName!, pendingAliasVariants);
                    pendingAliasOpen = false; pendingAliasName = null; pendingAliasVariants = null;
                }

                // Tier 1: type-system annotations
                if (line.StartsWith("@class", StringComparison.OrdinalIgnoreCase))
                {
                    // Flush any in-progress class
                    if (pendingClassName is not null)
                        classDef = BuildClass(pendingClassName, pendingClassExact, pendingClassParents, pendingFields, pendingClassDesc);

                    ParseClass(line, out pendingClassName, out pendingClassExact, out pendingClassParents);
                    pendingFields = null;
                    pendingClassDesc = null;
                    continue;
                }

                if (line.StartsWith("@field", StringComparison.OrdinalIgnoreCase))
                {
                    // @field belongs to the preceding @class
                    (pendingFields ??= []).Add(ParseField(line));
                    continue;
                }

                // A non-@field annotation closes any open @class block
                if (pendingClassName is not null)
                {
                    classDef = BuildClass(pendingClassName, pendingClassExact, pendingClassParents, pendingFields, pendingClassDesc);
                    pendingClassName = null; pendingClassParents = null; pendingFields = null; pendingClassDesc = null;
                }

                if (line.StartsWith("@param", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseParam(line, out var p)) pendingParams.Add(p);
                    continue;
                }
                if (line.StartsWith("@return", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseReturn(line, out var r)) pendingReturns.Add(r);
                    continue;
                }
                if (line.StartsWith("@alias", StringComparison.OrdinalIgnoreCase))
                {
                    ParseAliasOpen(line, out pendingAliasName, out pendingAliasVariants);
                    pendingAliasOpen = pendingAliasName is not null;
                    if (!pendingAliasOpen && pendingAliasName is not null)
                        aliasDef = BuildAlias(pendingAliasName, pendingAliasVariants);
                    continue;
                }
                if (line.StartsWith("@enum", StringComparison.OrdinalIgnoreCase))
                {
                    enumDef = ParseEnum(line);
                    continue;
                }
                if (line.StartsWith("@type", StringComparison.OrdinalIgnoreCase))
                {
                    typeAnnotation = ParseTypeAnnotation(line);
                    continue;
                }
                if (line.StartsWith("@overload", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = line["@overload".Length..].Trim();
                    if (rest.Length > 0) overloads.Add(rest);
                    continue;
                }
                if (line.StartsWith("@generic", StringComparison.OrdinalIgnoreCase))
                {
                    ParseGenerics(line, genericParams);
                    continue;
                }

                // Tier 2: documentation markers
                if (line.StartsWith("@deprecated", StringComparison.OrdinalIgnoreCase)) { isDeprecated = true; continue; }
                if (line.StartsWith("@nodiscard", StringComparison.OrdinalIgnoreCase))  { isNodiscard  = true; continue; }
                if (line.StartsWith("@async",     StringComparison.OrdinalIgnoreCase))  { isAsync      = true; continue; }
                if (line.StartsWith("@see",       StringComparison.OrdinalIgnoreCase))
                {
                    var sym = line["@see".Length..].Trim();
                    if (sym.Length > 0) seeRefs.Add(sym);
                    continue;
                }
                if (line.StartsWith("@private",   StringComparison.OrdinalIgnoreCase)) { accessModifier = LuaAccessModifier.Private;   continue; }
                if (line.StartsWith("@protected",  StringComparison.OrdinalIgnoreCase)) { accessModifier = LuaAccessModifier.Protected; continue; }
                if (line.StartsWith("@package",   StringComparison.OrdinalIgnoreCase)) { accessModifier = LuaAccessModifier.Package;   continue; }

                // Tier 3: silently skip (@cast, @diagnostic, @meta, @module, @operator,
                //          @source, @vararg, @version, @as, @xmlref, @Override, and others)
                continue;
            }

            // ── prose description ─────────────────────────────────────────────
            // Flush pending alias if a prose line appears after |… variants
            if (pendingAliasOpen)
            {
                aliasDef = BuildAlias(pendingAliasName!, pendingAliasVariants);
                pendingAliasOpen = false; pendingAliasName = null; pendingAliasVariants = null;
            }

            if (line.Length > 0)
                proseLines.Add(line);
        }

        // Flush any remaining open constructs
        if (pendingClassName is not null)
            classDef = BuildClass(pendingClassName, pendingClassExact, pendingClassParents, pendingFields, pendingClassDesc);
        if (pendingAliasOpen && pendingAliasName is not null)
            aliasDef = BuildAlias(pendingAliasName, pendingAliasVariants);

        var description = proseLines.Count > 0 ? string.Join("\n", proseLines) : null;

        return new EmmyLuaAnnotations(
            description,
            classDef, aliasDef, enumDef, typeAnnotation,
            ToImmutable(pendingParams),
            ToImmutable(pendingReturns),
            ToImmutable(overloads),
            ToImmutable(genericParams),
            isDeprecated, isNodiscard, isAsync,
            accessModifier,
            ToImmutable(seeRefs));
    }

    // ── @param ───────────────────────────────────────────────────────────────

    private static bool TryParseParam(string line, out LuaParamAnnotation result)
    {
        // @param name[?] Type [description]
        var parts = line["@param".Length..].TrimStart().Split(s_spaceSep, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) { result = default!; return false; }

        var namePart   = parts[0];
        var isOptional = namePart.EndsWith('?');
        var name       = isOptional ? namePart[..^1] : namePart;
        var type       = parts.Length >= 2 ? new LuaTypeRef(parts[1]) : LuaTypeRef.Unknown;
        var desc       = parts.Length >= 3 ? parts[2].Trim() : null;

        result = new LuaParamAnnotation(name, isOptional, type, string.IsNullOrEmpty(desc) ? null : desc);
        return true;
    }

    // ── @return ──────────────────────────────────────────────────────────────

    private static bool TryParseReturn(string line, out LuaReturnAnnotation result)
    {
        // @return Type [name] [description]
        var parts = line["@return".Length..].TrimStart().Split(s_spaceSep, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) { result = default!; return false; }

        var type       = new LuaTypeRef(parts[0]);
        string? name   = parts.Length >= 2 ? parts[1] : null;
        string? desc   = parts.Length >= 3 ? parts[2].Trim() : null;

        result = new LuaReturnAnnotation(type, string.IsNullOrEmpty(name) ? null : name, string.IsNullOrEmpty(desc) ? null : desc);
        return true;
    }

    // ── @class ───────────────────────────────────────────────────────────────

    private static void ParseClass(string line,
        out string name, out bool exact, out List<string>? parents)
    {
        // @class [(exact)] Name [: Parent1, Parent2]
        var rest  = line["@class".Length..].TrimStart();
        exact     = false;
        parents   = null;

        if (rest.StartsWith("(exact)", StringComparison.OrdinalIgnoreCase))
        {
            exact = true;
            rest  = rest["(exact)".Length..].TrimStart();
        }

        // Split on ':' to separate name from parents
        var colonIdx = rest.IndexOf(':');
        if (colonIdx >= 0)
        {
            name    = rest[..colonIdx].Trim();
            var parentStr = rest[(colonIdx + 1)..].Trim();
            if (parentStr.Length > 0)
                parents = [.. parentStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                        .Select(p => p.Trim())
                                        .Where(p => p.Length > 0)];
        }
        else
        {
            name = rest.Split(s_spaceSep, 2)[0].Trim();
        }

        if (name.Length == 0) name = "?";
    }

    // ── @field ───────────────────────────────────────────────────────────────

    private static LuaFieldDefinition ParseField(string line)
    {
        // @field [scope] name[?] Type [description]
        // Strip the @field tag and check for an optional scope keyword first.
        var rest   = line["@field".Length..].TrimStart();
        var access = LuaAccessModifier.Public;

        var firstSpace = rest.IndexOfAny(s_spaceSep);
        if (firstSpace > 0)
        {
            var firstWord = rest[..firstSpace];
            if (firstWord is "public" or "protected" or "private" or "package")
            {
                access = firstWord switch
                {
                    "protected" => LuaAccessModifier.Protected,
                    "private"   => LuaAccessModifier.Private,
                    "package"   => LuaAccessModifier.Package,
                    _           => LuaAccessModifier.Public
                };
                rest = rest[(firstSpace + 1)..].TrimStart();
            }
        }

        if (rest.Length == 0) return new LuaFieldDefinition("?", false, LuaTypeRef.Unknown, null, access);

        // Rest is now "name[?] Type [description]" — split at most 3 parts so the
        // description may contain spaces.
        var parts      = rest.Split(s_spaceSep, 3, StringSplitOptions.RemoveEmptyEntries);
        var namePart   = parts[0];
        var isOptional = namePart.EndsWith('?');
        var name       = isOptional ? namePart[..^1] : namePart;
        var type       = parts.Length >= 2 ? new LuaTypeRef(parts[1]) : LuaTypeRef.Unknown;
        var desc       = parts.Length >= 3 ? parts[2].Trim() : null;

        return new LuaFieldDefinition(name, isOptional, type, string.IsNullOrEmpty(desc) ? null : desc, access);
    }

    // ── @alias ───────────────────────────────────────────────────────────────

    private static void ParseAliasOpen(string line,
        out string? name, out List<string>? variants)
    {
        // @alias Name [Type | Type2 | ...]  OR  @alias Name  (followed by | lines)
        var rest  = line["@alias".Length..].TrimStart();
        variants  = null;

        var parts = rest.Split(s_spaceSep, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { name = null; return; }
        name = parts[0];

        if (parts.Length < 2) return; // just the name; variants come on subsequent | lines

        // Inline variants separated by |
        var variantStr = parts[1];
        variants = [.. variantStr.Split('|', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(v => v.Trim())
                                  .Where(v => v.Length > 0)];
    }

    private static LuaAliasDefinition BuildAlias(string name, List<string>? variants)
    {
        var refs = variants is { Count: > 0 }
            ? variants.Select(v => new LuaTypeRef(v)).ToImmutableArray()
            : ImmutableArray<LuaTypeRef>.Empty;
        return new LuaAliasDefinition(name, refs);
    }

    // ── @enum ────────────────────────────────────────────────────────────────

    private static LuaEnumDefinition ParseEnum(string line)
    {
        // @enum [(key)] Name
        var rest   = line["@enum".Length..].TrimStart();
        var useKeys = false;
        if (rest.StartsWith("(key)", StringComparison.OrdinalIgnoreCase))
        {
            useKeys = true;
            rest    = rest["(key)".Length..].TrimStart();
        }
        var name = rest.Split(s_spaceSep, 2)[0].Trim();
        return new LuaEnumDefinition(name.Length > 0 ? name : "?", useKeys);
    }

    // ── @type ────────────────────────────────────────────────────────────────

    private static LuaTypeRef ParseTypeAnnotation(string line)
    {
        var rest = line["@type".Length..].Trim();
        return rest.Length > 0 ? new LuaTypeRef(rest) : LuaTypeRef.Unknown;
    }

    // ── @generic ─────────────────────────────────────────────────────────────

    private static void ParseGenerics(string line, List<string> genericParams)
    {
        // @generic T [: Constraint] [, U [: Constraint2] ...]
        var rest = line["@generic".Length..].TrimStart();
        // Split by comma, then take the first word of each segment (the type-param name)
        foreach (var segment in rest.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = segment.Trim().Split(s_spaceSep, 2)[0].Trim().TrimEnd(':');
            if (name.Length > 0 && !genericParams.Contains(name, StringComparer.Ordinal))
                genericParams.Add(name);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static LuaClassDefinition BuildClass(
        string name, bool exact, List<string>? parents,
        List<LuaFieldDefinition>? fields, string? desc)
    {
        return new LuaClassDefinition(
            name, exact,
            parents is { Count: > 0 } ? parents.ToImmutableArray() : ImmutableArray<string>.Empty,
            fields  is { Count: > 0 } ? fields.ToImmutableArray()  : ImmutableArray<LuaFieldDefinition>.Empty,
            desc);
    }

    private static ImmutableArray<T> ToImmutable<T>(List<T> list)
        => list.Count > 0 ? list.ToImmutableArray() : ImmutableArray<T>.Empty;
}
