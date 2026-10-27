// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;

namespace PG.StarWarsGame.LSP.Core.Xml;

/// <summary>
///     Reads the file list of a metafile - a registry such as <c>GameObjectFiles.xml</c> or
///     <c>CampaignFiles.xml</c> whose <c>&lt;File&gt;</c> entries name the files the engine loads for
///     one type. One reader for the indexer, the baseline builder and the story chain scanner, so the
///     entries they see are the same entries (#308).
/// </summary>
public static class MetafileReader
{
    /// <summary>
    ///     Every <c>&lt;File&gt;</c> entry in document order, as written: the trimmed element text,
    ///     or the <c>filename</c> attribute when the element carries no text. An entry with neither
    ///     is skipped. The element and attribute names match in any case. What a path means - bare
    ///     name, sub-directory, fully qualified - is the caller's to decide, since each keys differently.
    /// </summary>
    public static IReadOnlyList<string> ReadFileList(string xml)
    {
        var document = XmlUtility.CreateHtmlDocument(xml);
        var files = new List<string>();
        foreach (var node in document.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element &&
                                 n.Name.Equals("File", StringComparison.OrdinalIgnoreCase)))
        {
            var name = node.InnerText.Trim();
            if (name.Length == 0)
                name = node.GetAttributeValue("filename", string.Empty).Trim();
            if (name.Length > 0)
                files.Add(name);
        }

        return files;
    }
}