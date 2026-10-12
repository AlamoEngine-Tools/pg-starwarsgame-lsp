// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Server.Assets;

/// <summary>Which folder an archive was found in.</summary>
public enum MegArchiveSource
{
    /// <summary>One of the workspace's project folders.</summary>
    Mod,

    /// <summary>The folder the game runs from: the expansion when one is configured.</summary>
    Game,

    /// <summary>The base game folder, when the game runs from the expansion.</summary>
    BaseGame
}

/// <summary>One archive the game loads, by its absolute file path.</summary>
public sealed record MegArchive(string Path, MegArchiveSource Source);

/// <summary>The folders an archive name resolves against.</summary>
/// <param name="ModRoots">The project folders, the leaf first - the game's mod paths, in order.</param>
/// <param name="GameRoot">The folder the game runs from, or null when none is configured.</param>
/// <param name="BaseGameRoot">The base game folder when the game runs from the expansion, else null.</param>
public sealed record MegArchiveRoots(
    IReadOnlyList<string> ModRoots,
    string? GameRoot,
    string? BaseGameRoot)
{
    /// <summary>
    ///     The workspace's project folders, leaf first, over the configured game folders.
    /// </summary>
    /// <remarks>
    ///     A project folder is the folder of its <c>.pgproj</c>, which is the folder that holds its
    ///     <c>Data</c> - the shape the game's mod path has. With an expansion configured the game runs
    ///     from it and the base game folder is its fallback; with only the base game configured, the
    ///     base game is what runs.
    /// </remarks>
    public static MegArchiveRoots For(WorkspaceConfiguration? workspace, LspConfiguration config)
    {
        IReadOnlyList<string> mods = workspace is null
            ? []
            : [.. workspace.Layers.OrderByDescending(l => l.Rank).Select(l => l.ProjectDirectory).OfType<string>()];

        var expansion = NullIfBlank(config.ExpansionPath);
        var baseGame = NullIfBlank(config.GamePath);
        return expansion is null
            ? new MegArchiveRoots(mods, baseGame, null)
            : new MegArchiveRoots(mods, expansion, baseGame);
    }

    private static string? NullIfBlank(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}

/// <summary>
///     The archives the game loads, in the order it searches them (#219).
/// </summary>
/// <remarks>
///     <para>
///         Measured. The game reads <c>Data\MegaFiles.xml</c> and adds every non-empty value (trimmed)
///         in document order, then <c>Data\Patch.meg</c>, <c>Data\patch2.meg</c> and
///         <c>Data\64Patch.meg</c>; later the sound system adds <c>SFX2D_Non_Localized.meg</c>, the
///         language's <c>SFX2D_*.meg</c> and <c>SFX3D_Non_Localized.meg</c> from
///         <c>Data\Audio\SFX</c>. Nothing else is read - an archive no list names never loads. Each
///         archive is put at the FRONT of the search list, so the last one added is searched first;
///         an archive that cannot be opened is dropped, and a name already added is refused.
///     </para>
///     <para>
///         A name under <c>Data\</c> - the list file itself included - is looked for in each mod
///         folder in turn, then in the game folder, then in the base game folder. So a mod's own
///         <c>Data\MegaFiles.xml</c> replaces the game's list, and a mod archive of a listed name
///         replaces the game's archive of that name.
///     </para>
///     <para>
///         The expansion runs with the base game folder as its fallback. The base game's list,
///         patches and sound archives are added FIRST, from the base game folder alone (their names
///         carry that folder, so no mod folder applies), and are therefore searched after
///         everything of the expansion's.
///     </para>
///     <para>
///         The language archive is the English one: the game picks it by its language setting,
///         which this server does not know, and English is the language it documents in.
///     </para>
/// </remarks>
public sealed class MegArchiveLoadOrder(IFileHelper fileHelper)
{
    private const string ListFile = "data/megafiles.xml";

    private static readonly string[] PatchArchives = ["data/patch.meg", "data/patch2.meg", "data/64patch.meg"];

    private static readonly string[] SoundArchives =
    [
        "data/audio/sfx/sfx2d_non_localized.meg",
        "data/audio/sfx/sfx2d_english.meg",
        "data/audio/sfx/sfx3d_non_localized.meg"
    ];

    /// <summary>Every archive the game loads, the one it searches first first.</summary>
    public IReadOnlyList<MegArchive> SearchOrder(MegArchiveRoots roots)
    {
        var registered = new List<MegArchive>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        void Add(string key, MegArchive? archive)
        {
            if (archive is null || !names.Add(key)) return;
            registered.Add(archive);
        }

        // The base game's set first: only its own folder, and its names are distinct from the
        // expansion's because they carry that folder.
        if (roots.BaseGameRoot is { } baseGame)
        {
            foreach (var entry in ReadList(InFolder(baseGame, ListFile, MegArchiveSource.BaseGame)))
                Add("fallback:" + entry, InFolder(baseGame, entry, MegArchiveSource.BaseGame));
            foreach (var patch in PatchArchives)
                Add("fallback:" + patch, InFolder(baseGame, patch, MegArchiveSource.BaseGame));
        }

        foreach (var entry in ReadList(Resolve(roots, ListFile)))
            Add("run:" + entry, Resolve(roots, entry));
        foreach (var patch in PatchArchives)
            Add("run:" + patch, Resolve(roots, patch));

        // Each sound archive is added from the base game folder first, then by its plain name.
        foreach (var sound in SoundArchives)
        {
            if (roots.BaseGameRoot is { } fallback)
                Add("fallback:" + sound, InFolder(fallback, sound, MegArchiveSource.BaseGame));
            Add("sound:" + sound, Resolve(roots, sound));
        }

        registered.Reverse();

        // One archive reached by two names is searched once, at its first place; the second
        // place could never answer anything the first had not.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. registered.Where(a => seen.Add(a.Path))];
    }

    /// <summary>A name as the game opens it: mod folders, then the game folder, then the base game's.</summary>
    private MegArchive? Resolve(MegArchiveRoots roots, string name)
    {
        var normalized = fileHelper.NormalizeGamePath(name);

        // Only a name under Data\ is looked for in a mod folder or the base game folder.
        var underData = normalized.StartsWith("data/", StringComparison.Ordinal);
        if (underData)
            foreach (var mod in roots.ModRoots)
                if (InFolder(mod, normalized, MegArchiveSource.Mod) is { } hit)
                    return hit;

        if (roots.GameRoot is { } game && InFolder(game, normalized, MegArchiveSource.Game) is { } inGame)
            return inGame;

        return underData && roots.BaseGameRoot is { } baseGame
            ? InFolder(baseGame, normalized, MegArchiveSource.BaseGame)
            : null;
    }

    private MegArchive? InFolder(string folder, string name, MegArchiveSource source)
    {
        var found = fileHelper.FindInWorkspace([folder], fileHelper.NormalizeGamePath(name));
        return found is null ? null : new MegArchive(found, source);
    }

    /// <summary>The trimmed, non-empty values of the list's elements, in document order.</summary>
    private IEnumerable<string> ReadList(MegArchive? list)
    {
        if (list is null) return [];

        string text;
        try
        {
            text = fileHelper.FileSystem.File.ReadAllText(list.Path);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        if (!XmlUtility.TryGetRootNode(XmlUtility.CreateHtmlDocument(text), out var root) || root is null)
            return [];

        return
        [
            .. root.ChildNodes
                .Where(n => n.NodeType == HtmlNodeType.Element)
                .Select(n => n.InnerText.Trim(' ', '\t', '\r', '\n'))
                .Where(v => v.Length > 0)
        ];
    }
}