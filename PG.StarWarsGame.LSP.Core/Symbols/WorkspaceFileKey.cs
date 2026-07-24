// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Builds the shared symbol/reference id for a <see cref="ReferenceKind.WorkspaceFile" />
///     target, so a file-symbol (emitted when the file itself is parsed) and a reference to it
///     (emitted from a <c>workspaceFile</c> tag) agree on one key regardless of separator style,
///     <c>DATA\XML\</c> prefix, or casing. Form: <c>&lt;lowercased file-type&gt;:&lt;normalized
///     value&gt;</c>. XML file-types (<c>StoryPlotManifest</c>, <c>StoryParser</c>) normalize as
///     xml-relative paths; <see cref="LuaScriptType" /> keys by extensionless base name, because
///     Lua scripts are referenced by bare name against the script roots.
/// </summary>
public static class WorkspaceFileKey
{
    public const string LuaScriptType = "LuaScript";

    public static string Create(string fileType, string rawValue)
    {
        var normalized = string.Equals(fileType, LuaScriptType, StringComparison.OrdinalIgnoreCase)
            ? BaseName(rawValue)
            : StoryReferenceTypes.NormalizeRelativePath(rawValue).ToLowerInvariant();
        return fileType.ToLowerInvariant() + ":" + normalized;
    }

    /// <summary>Whether an id is a workspace-file key of the given (lowercased) file-type.</summary>
    public static bool HasType(string id, string fileType)
    {
        return id.StartsWith(fileType.ToLowerInvariant() + ":", StringComparison.Ordinal);
    }

    private static string BaseName(string value)
    {
        var forward = value.Replace('\\', '/');
        var slash = forward.LastIndexOf('/');
        var name = slash >= 0 ? forward[(slash + 1)..] : forward;
        var dot = name.LastIndexOf('.');
        if (dot > 0) name = name[..dot];
        return name.Trim().ToLowerInvariant();
    }
}
