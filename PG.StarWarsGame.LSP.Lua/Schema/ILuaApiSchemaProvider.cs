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
    ///     The call forms of a global function (its <c>---@overload fun(...)</c> lines, in order):
    ///     the argument lists a binding tells apart by count and by the type it finds at a position.
    ///     Empty when the function has none. Lookup is case-insensitive.
    /// </summary>
    IReadOnlyList<IReadOnlyList<LuaParamAnnotation>> GetCallForms(string functionName);

    /// <summary>
    ///     The type an engine stub declares for a global value (<c>---@type Player</c> above
    ///     <c>PlayerObject = nil</c>), or <c>null</c>. Lookup is case-insensitive.
    /// </summary>
    string? GetGlobalTypeName(string globalName);

    /// <summary>
    ///     The entry point of that name - a function a script defines for the game or a library to
    ///     call - or <c>null</c>. Not a declared global. Lookup is case-insensitive.
    /// </summary>
    LuaEntryPoint? GetEntryPoint(string functionName);

    /// <summary>
    ///     A hash of the stub text this provider was built from. The stubs decide which string
    ///     arguments a script's index records as references, so a cached index is only valid under
    ///     the stubs it was built with; empty when no stubs are loaded.
    /// </summary>
    string Fingerprint { get; }
}

/// <summary>
///     Describes one reference-carrying string parameter of an engine function: which parameter,
///     what kind of thing its value names (<c>---@aetref &lt;ReferenceKind&gt;[:&lt;referenceType&gt;]</c>)
///     and, for an XML object, the object type it must have.
/// </summary>
/// <param name="Literals">
///     String literals the parameter's declared type names (<c>string|"local"</c>): values the
///     engine takes as they are, which name no object and are no reference.
/// </param>
public readonly record struct XmlRefEntry(
    int ParamIndex,
    string? ExpectedTypeName,
    ReferenceKind Kind = ReferenceKind.XmlObject,
    IReadOnlyList<string>? Literals = null);