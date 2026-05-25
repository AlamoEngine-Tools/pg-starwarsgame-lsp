// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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
    ///     Returns the XML reference entries for a function, or an empty list if none.
    ///     Lookup is case-insensitive.
    /// </summary>
    IReadOnlyList<XmlRefEntry> GetXmlRefs(string functionName);

    /// <summary>
    ///     Returns the description for a function, or <c>null</c> if not documented.
    ///     Lookup is case-insensitive.
    /// </summary>
    string? GetFunctionDescription(string functionName);
}

/// <summary>
///     Describes one XML object reference parameter in a C++ API function.
/// </summary>
public readonly record struct XmlRefEntry(int ParamIndex, string? ExpectedTypeName);
