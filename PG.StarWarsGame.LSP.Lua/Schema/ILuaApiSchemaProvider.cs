// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

namespace PG.StarWarsGame.LSP.Lua.Schema;

/// <summary>
///     Provides information about C++ injected Lua API functions.
/// </summary>
public interface ILuaApiSchemaProvider
{
    /// <summary>
    ///     All known C++ engine global function names (case-insensitive).
    ///     Used as a whitelist to avoid false-positive "missing require" warnings.
    /// </summary>
    IReadOnlySet<string> AllFunctionNames { get; }

    /// <summary>
    ///     Every global name the stub files declare: the engine functions plus the values declared
    ///     by a top-level assignment (<c>Script = nil</c>, <c>string = {}</c>). This is the whole
    ///     built-in surface a script sees before its own code runs, so a name outside it that no
    ///     file defines is unknown to the engine. Case-insensitive.
    /// </summary>
    IReadOnlySet<string> DeclaredGlobalNames { get; }

    /// <summary>
    ///     Returns the reference entries for a function, or an empty list if none. A global function
    ///     is found by its name; a class method (<c>function GameObject.Play_SFX_Event</c>) by the
    ///     method name alone, since a call site does not know its receiver's type. Lookup is
    ///     case-insensitive.
    /// </summary>
    IReadOnlyList<XmlRefEntry> GetXmlRefs(string functionName);

    /// <summary>
    ///     Returns the description for a function, or <c>null</c> if not documented.
    ///     Lookup is case-insensitive.
    /// </summary>
    string? GetFunctionDescription(string functionName);

    /// <summary>
    ///     Returns the engine return type name for a function (from <c>---@return</c> annotation),
    ///     or <c>null</c> if unknown. Lookup is case-insensitive.
    /// </summary>
    string? GetReturnTypeName(string functionName);

    /// <summary>
    ///     Returns the documented parameters for a function (from <c>---@param</c> annotations),
    ///     or an empty list if none. Lookup is case-insensitive.
    /// </summary>
    IReadOnlyList<LuaParamAnnotation> GetFunctionParams(string functionName);

    /// <summary>
    ///     Returns all documented members of the given engine type (from <c>function TypeName.X</c>
    ///     / <c>function TypeName:X</c> declarations). Empty when the type is unknown.
    /// </summary>
    IReadOnlyList<LuaTypeMember> GetMembersOf(string typeName);

    /// <summary>
    ///     Returns the <c>---@class</c> definition for the given type name as declared in
    ///     the engine API schema (<c>api.d.lua</c>), or <c>null</c> if not found.
    ///     Lookup is case-insensitive.
    /// </summary>
    LuaClassDefinition? GetClassDefinition(string typeName);
}

/// <summary>
///     Describes one reference-carrying string parameter of an engine function: which parameter,
///     what kind of thing its value names (<c>---@aetref &lt;ReferenceKind&gt;[:&lt;referenceType&gt;]</c>;
///     the older <c>---@xmlref</c> spelling is read the same way) and, for an XML object, the
///     object type it must have.
/// </summary>
public readonly record struct XmlRefEntry(
    int ParamIndex,
    string? ExpectedTypeName,
    ReferenceKind Kind = ReferenceKind.XmlObject);

/// <summary>Describes one member of an engine-exposed Lua type.</summary>
public readonly record struct LuaTypeMember(
    string Name,
    bool IsMethod,
    string? Description,
    string? ReturnTypeName);