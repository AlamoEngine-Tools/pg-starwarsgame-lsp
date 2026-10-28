// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Assets.Projection;

public readonly record struct ObjectFacts(string[] Behaviors, string[] Flags);

/// <summary>
///     Reads the kind facts of game objects out of their files. Parsed through <see cref="XmlUtility" />
///     like every other game XML (#308): a file a strict parser rejects is read the way the game
///     reads it, and only a file that cannot be opened is reported.
/// </summary>
public static class GameObjectFactsReader
{
    /// <summary>
    ///     Kind facts per object name, for the objects that declare any behaviour. Objects with no
    ///     behaviour tag, no file, or an unreadable file are simply absent.
    /// </summary>
    /// <param name="objects">Each object's name and the game-relative XML file it was read from.</param>
    /// <param name="openFile">Opens a game file by its game-relative path, or returns null.</param>
    /// <param name="flagTags">The boolean tags to record when true - the ones some kind tests.</param>
    /// <param name="onProblem">Called once per file that could not be read, with a reason.</param>
    public static Dictionary<string, ObjectFacts> Read(
        IEnumerable<(string Name, string? XmlFile)> objects,
        Func<string, Stream?> openFile,
        IReadOnlyCollection<string>? flagTags = null,
        Action<string>? onProblem = null)
    {
        var result = new Dictionary<string, ObjectFacts>(StringComparer.OrdinalIgnoreCase);
        var tracked = new HashSet<string>(flagTags ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var group in objects
                     .Where(o => !string.IsNullOrEmpty(o.XmlFile))
                     .GroupBy(o => o.XmlFile!, StringComparer.OrdinalIgnoreCase))
        {
            var file = ReadFile(group.Key, openFile, onProblem);
            if (file is null) continue;
            var (byName, text) = file.Value;

            foreach (var (name, _) in group)
            {
                if (!byName.TryGetValue(name, out var element)) continue;

                // Tag names as the file spells them: the parser lower-cases every element name.
                var children = element.ChildNodes
                    .Where(c => c.NodeType == HtmlNodeType.Element)
                    .Select(c => (Name: XmlUtility.GetOriginalTagName(c, text), Value: c.InnerText))
                    .ToList();
                var behaviors = ObjectBehaviors.FromTags(children);
                if (behaviors.Length == 0) continue;

                var flags = children
                    .Where(c => tracked.Contains(c.Name) && EngineBoolean.IsTrue(c.Value))
                    .Select(c => c.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                result[name] = new ObjectFacts(behaviors, flags);
            }
        }

        return result;
    }

    /// <summary>
    ///     Every named element in one game file, keyed by its <c>Name</c> attribute, with the text
    ///     the names are spelled in. Null when the file is missing or cannot be opened - one bad file
    ///     costs that file, never the run.
    /// </summary>
    private static (Dictionary<string, HtmlNode> ByName, string Text)? ReadFile(
        string path, Func<string, Stream?> openFile, Action<string>? onProblem)
    {
        try
        {
            using var stream = openFile(path);
            if (stream is null) return null;

            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            var document = XmlUtility.CreateHtmlDocument(text);
            var byName = new Dictionary<string, HtmlNode>(StringComparer.OrdinalIgnoreCase);

            // Descendants, not the root's children: some shipped files nest their objects a level
            // further down, and a flat pass would silently find nothing in those.
            foreach (var element in document.DocumentNode.Descendants()
                         .Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var name = element.GetAttributeValue("name", null);
                if (string.IsNullOrWhiteSpace(name)) continue;
                byName.TryAdd(name.Trim(), element);
            }

            return (byName, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            onProblem?.Invoke($"{path}: {ex.Message}");
            return null;
        }
    }
}