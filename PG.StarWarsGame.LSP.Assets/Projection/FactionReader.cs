// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.Files.XML;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     Reads the shipped factions out of the files the FactionFiles registry lists. The engine keeps
///     factions in a manager of their own that PG.StarWarsGame.Engine does not expose, so this is the
///     same direct-parse stopgap as music events and shadow blob materials - through
///     <see cref="MetafileReader" /> and <see cref="XmlUtility" />, so the builder sees the list and the
///     objects the way the indexer does (#308).
/// </summary>
public static class FactionReader
{
    /// <summary>The classification the projector maps to the Faction type.</summary>
    public const string Classification = "FACTION";

    // The registry lists XML-directory-relative names; the repository wants the game path, in the
    // spelling the engine's own object locations use.
    private const string XmlDirectory = "DATA\\XML\\";

    /// <summary>
    ///     Every faction of every file the registry lists, in list order. A listed file that does not
    ///     ship, or cannot be opened, is reported and costs that file, never the run.
    /// </summary>
    /// <param name="registryPath">The FactionFiles registry, as a game path the repository opens.</param>
    /// <param name="openFile">Opens a game file by its game-relative path, or returns null.</param>
    /// <param name="onProblem">Called once per file that could not be read, with a reason.</param>
    public static List<ProjectableEntry> Read(
        string registryPath, Func<string, Stream?> openFile, Action<string>? onProblem = null)
    {
        var entries = new List<ProjectableEntry>();
        var registry = ReadText(registryPath, openFile, onProblem);
        if (registry is null) return entries;

        foreach (var key in MetafileReader.ReadFileKeys(registry))
        {
            var path = XmlDirectory + key.ToUpperInvariant().Replace('/', '\\');
            var text = ReadText(path, openFile, onProblem);
            if (text is null) continue;

            var document = XmlUtility.CreateHtmlDocument(text);
            if (!XmlUtility.TryGetRootNode(document, out var root) || root is null) continue;

            foreach (var node in root.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var name = XmlUtility.GetNameAttributeValue(node);
                if (name is null) continue;
                // The printable line, as the engine's own locations and the other stopgaps carry it.
                entries.Add(new ProjectableEntry(name, Classification,
                    new XmlLocationInfo(path, XmlUtility.GetPrintableLine(node))));
            }
        }

        return entries;
    }

    private static string? ReadText(string path, Func<string, Stream?> openFile, Action<string>? onProblem)
    {
        try
        {
            using var stream = openFile(path);
            if (stream is null)
            {
                onProblem?.Invoke($"{path}: not found");
                return null;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            onProblem?.Invoke($"{path}: {ex.Message}");
            return null;
        }
    }
}