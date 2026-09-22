// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Xml;
using System.Xml.Linq;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>What a shipped object says about itself that decides its kind.</summary>
/// <param name="Behaviors">Tokens from its own behaviour tags, in written order, without repeats.</param>
/// <param name="Flags">
///     The tracked boolean tags that are TRUE on it. Empty means it was inspected and has none,
///     which is a different answer from never having been looked at.
/// </param>
public readonly record struct ObjectFacts(string[] Behaviors, string[] Flags);

/// <summary>
///     Reads each shipped game object's kind facts back out of the game's own XML files.
/// </summary>
/// <remarks>
///     <para>
///         Behaviours say what an object IS and flags finish the story for heroes, and the engine's
///         object model carries neither: it models the object's art, its icon, its ground company
///         and its variant base, and nothing else this needs. So it goes to the XML.
///     </para>
///     <para>
///         Every object knows the file it came from, so the files are opened once each and every
///         object wanted from one is read in a single pass. These facts ALONE are read rather than
///         the object's whole tag tree: the tree would be the fix for variant merging over shipped
///         objects, but it is also every tag of every shipped object carried verbatim, which is a
///         size question that wants measuring against a real install first.
///     </para>
/// </remarks>
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
            var byName = ReadFile(group.Key, openFile, onProblem);
            if (byName is null) continue;

            foreach (var (name, _) in group)
            {
                if (!byName.TryGetValue(name, out var element)) continue;

                var children = element.Elements().ToList();
                var behaviors = ObjectBehaviors.FromTags(children.Select(c => (c.Name.LocalName, c.Value)));
                if (behaviors.Length == 0) continue;

                var flags = children
                    .Where(c => tracked.Contains(c.Name.LocalName) && EngineBoolean.IsTrue(c.Value))
                    .Select(c => c.Name.LocalName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                result[name] = new ObjectFacts(behaviors, flags);
            }
        }

        return result;
    }

    /// <summary>
    ///     Every named element in one game file, keyed by its <c>Name</c> attribute. Null when the
    ///     file is missing or will not parse - one bad file costs that file, never the run.
    /// </summary>
    private static Dictionary<string, XElement>? ReadFile(
        string path, Func<string, Stream?> openFile, Action<string>? onProblem)
    {
        try
        {
            using var stream = openFile(path);
            if (stream is null) return null;

            var document = XDocument.Load(stream);
            var byName = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);

            // Descendants, not the root's children: some shipped files nest their objects a level
            // further down, and a flat pass would silently find nothing in those.
            foreach (var element in document.Descendants())
            {
                var name = element.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    ?.Value;
                if (string.IsNullOrWhiteSpace(name)) continue;
                byName.TryAdd(name.Trim(), element);
            }

            return byName;
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            onProblem?.Invoke($"{path}: {ex.Message}");
            return null;
        }
    }
}
