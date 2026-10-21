// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Instances;

/// <summary>
///     Where the files of one script instance live for the analyzer: a folder of the instance's own,
///     holding nothing else. The analyzer takes a document the server sends under its root as part of
///     the workspace and ignores one sent from anywhere else (measured, 0.25.1), so every file of the
///     instance is sent at a place below the root and every answer is mapped back.
/// </summary>
/// <remarks>
///     A file keeps its path below its layer's script root, so the analyzer's own module lookup finds a
///     required file where the script names it. Two files of one place - a library the leaf overrides
///     is never held twice, but a relative require can name a file of another layer - get apart under a
///     numbered folder. Files outside every script root, and the engine stubs the analyzer reads as its
///     library, are left as they are.
/// </remarks>
public sealed class LuaInstanceMirror
{
    private const string Outside = "_outside";

    private readonly IFileHelper _fileHelper;
    private readonly string _root;
    private readonly IReadOnlyList<string> _scriptRoots;
    private readonly Dictionary<string, string> _places = new(DocumentUris.Comparer);
    private readonly Dictionary<string, string> _files = new(DocumentUris.Comparer);

    /// <param name="root">The instance's folder.</param>
    /// <param name="scriptRoots">Every layer's script roots, as paths.</param>
    /// <param name="fileHelper">The URI normalization of the server.</param>
    public LuaInstanceMirror(string root, IReadOnlyList<string> scriptRoots, IFileHelper fileHelper)
    {
        _fileHelper = fileHelper;
        _root = fileHelper.PathToFileUri(root).TrimEnd('/');
        // Longest first: a script root nested in another one claims its own files.
        _scriptRoots = scriptRoots.Select(r => fileHelper.PathToFileUri(r).TrimEnd('/') + "/")
            .OrderByDescending(r => r.Length).ToList();
    }

    /// <summary>The place of <paramref name="realUri" /> below the root, given on first use.</summary>
    public string Add(string realUri)
    {
        var real = _fileHelper.NormalizeUri(realUri);
        if (_places.TryGetValue(real, out var known)) return known;

        var below = RelativeOf(real);
        var place = $"{_root}/{below}";
        for (var n = 2; _files.ContainsKey(place); n++) place = $"{_root}/_{n}/{below}";
        _places[real] = place;
        _files[place] = real;
        return place;
    }

    /// <summary>The place of <paramref name="realUri" />, or null when the instance does not hold it.</summary>
    public string? PlaceOf(string realUri)
    {
        return _places.GetValueOrDefault(_fileHelper.NormalizeUri(realUri));
    }

    /// <summary>The file at <paramref name="uri" />, in any spelling of it, or null when it is no place here.</summary>
    public string? RealOf(string uri)
    {
        return _files.GetValueOrDefault(_fileHelper.NormalizeUri(uri));
    }

    public void Remove(string realUri)
    {
        if (_places.Remove(_fileHelper.NormalizeUri(realUri), out var place)) _files.Remove(place);
    }

    /// <summary>Every URI of a held file under <paramref name="token" /> becomes its place, in place.</summary>
    public void ToMirror(JToken token)
    {
        Rewrite(token, PlaceOf);
    }

    /// <summary>Every URI of a place under <paramref name="token" /> becomes its file, in place.</summary>
    public void ToReal(JToken token)
    {
        Rewrite(token, RealOf);
    }

    private string RelativeOf(string real)
    {
        foreach (var scriptRoot in _scriptRoots)
            if (real.StartsWith(scriptRoot, StringComparison.OrdinalIgnoreCase))
                return real[scriptRoot.Length..];

        var slash = real.LastIndexOf('/');
        return $"{Outside}/{real[(slash + 1)..]}";
    }

    // URIs are values (a location's uri) and keys (a workspace edit's changes, by document).
    private static void Rewrite(JToken token, Func<string, string?> map)
    {
        switch (token)
        {
            case JObject obj:
                foreach (var property in obj.Properties().ToList())
                {
                    Rewrite(property.Value, map);
                    if (IsFileUri(property.Name) && map(property.Name) is { } key)
                        property.Replace(new JProperty(key, property.Value));
                }

                break;
            case JArray array:
                foreach (var item in array) Rewrite(item, map);
                break;
            case JValue { Type: JTokenType.String } value when IsFileUri(value.Value<string>()!):
                if (map(value.Value<string>()!) is { } mapped) value.Value = mapped;
                break;
        }
    }

    private static bool IsFileUri(string text)
    {
        return text.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }
}
