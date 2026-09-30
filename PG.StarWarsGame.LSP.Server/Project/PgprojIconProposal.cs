// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Text.Json.Nodes;
using PG.StarWarsGame.LSP.Core.Project;

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     Offers an <c>icons</c> node in the migration diff, for a project that has icon sources on
///     disk and no configured way to reach them.
/// </summary>
/// <remarks>
///     <para>
///         Such a project draws base-game artwork everywhere - every icon falls through to the baked
///         baseline - and the setting that fixes it is invisible, because nothing in the editor ever
///         had reason to mention it. The migration diff is the one moment the author is already
///         being asked to change this file, so the lines go in front of them there, with a notice
///         saying what they do.
///     </para>
///     <para>
///         It stays a PROPOSAL. Nothing is inferred at runtime and no default is silently applied -
///         the node only ever appears in a diff that needs a yes, which is what keeps this
///         compatible with the rule that icon paths are the author's to declare.
///     </para>
///     <para>
///         Deliberately not an <see cref="Core.Persistence.IDocumentMigration" />. Those run over the
///         raw tree with no path and no filesystem, by design, so a step cannot tell whether the
///         folder it would name actually exists - and naming a folder that is not there is worse
///         than saying nothing. Here the path IS known, so the proposal is made only for folders
///         that are really on disk and really have something in them.
///     </para>
/// </remarks>
public static class PgprojIconProposal
{
    /// <summary>Where a packer conventionally keeps raw icon sources, under an art directory.</summary>
    private const string ConventionalIconFolder = "textures/icons";

    /// <summary>
    ///     Adds an <c>icons</c> node to <paramref name="document" /> when one is warranted, and
    ///     returns what to tell the user. Null means nothing was proposed and the document is
    ///     untouched.
    /// </summary>
    /// <param name="document">The migrated project tree, modified in place when a node is added.</param>
    /// <param name="projectDirectory">The directory holding the <c>.pgproj</c>.</param>
    /// <param name="fs">Filesystem the project lives on.</param>
    public static string? TryPropose(JsonObject document, string projectDirectory, IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fs);

        // Never second-guess a setting the author already made.
        if (document["icons"] is not null) return null;

        // A mod shipping the packed atlas at the conventional path needs nothing: that is exactly
        // what the convention is for.
        if (HasConventionalMegaTexture(projectDirectory, fs)) return null;

        var roots = ArtDirectories(document)
            .Select(art => Combine(art, ConventionalIconFolder))
            .Where(candidate => HasAnyFile(fs, fs.Path.Combine(projectDirectory, candidate)))
            .ToList();

        if (roots.Count == 0) return null;

        var array = new JsonArray();
        foreach (var root in roots) array.Add(root);
        document["icons"] = new JsonObject { ["sourceRoots"] = array };

        return $"This project has icon sources at {string.Join(", ", roots)} that nothing is "
               + "configured to read, so every icon falls back to the base game. The proposed "
               + "'icons' node points at them.";
    }

    private static bool HasConventionalMegaTexture(string projectDirectory, IFileSystem fs)
    {
        var settings = IconProjectSettings.Default;
        return fs.File.Exists(fs.Path.Combine(projectDirectory, settings.MtdPath))
               && fs.File.Exists(fs.Path.Combine(projectDirectory, settings.TexturePath));
    }

    /// <summary>
    ///     The project's declared art directories. Its own, not the workspace's: these paths are
    ///     written relative to THIS <c>.pgproj</c>, and two projects in one workspace legitimately
    ///     nest their art differently.
    /// </summary>
    private static IEnumerable<string> ArtDirectories(JsonObject document)
    {
        if (document["directories"] is not JsonObject directories) return [];
        if (directories["art"] is not JsonArray art) return [];

        return art
            .Select(node => node?.GetValue<string>())
            .Where(dir => !string.IsNullOrWhiteSpace(dir))
            .Select(dir => dir!.Replace('\\', '/').TrimEnd('/'));
    }

    /// <summary>
    ///     Whether the folder holds anything. An empty scaffold folder is not a set of sources, and
    ///     proposing a node for one would put a setting in the file that changes nothing.
    /// </summary>
    private static bool HasAnyFile(IFileSystem fs, string path)
    {
        if (!fs.Directory.Exists(path)) return false;

        try
        {
            return fs.Directory.EnumerateFiles(path).Any();
        }
        catch (IOException)
        {
            // Unreadable is not "empty", but it is also not something to propose a setting about.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Combine(string art, string folder)
    {
        return art.Length == 0 ? folder : $"{art}/{folder}";
    }
}
