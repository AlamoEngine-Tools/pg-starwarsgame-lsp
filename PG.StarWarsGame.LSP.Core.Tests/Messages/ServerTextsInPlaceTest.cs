// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Core.Tests.Messages;

/// <summary>
///     The server's texts come from <c>ServerMessages.resx</c>, so a text written in place at a sink
///     a reader sees is one no translation reaches and no text rule checks. This finds them: a
///     diagnostic's or an action's message, title or reason set from a literal, a notification shown
///     from one, a refusal returned or thrown with one.
/// </summary>
/// <remarks>
///     Text that is file CONTENT rather than interface - written into a project file a person may
///     later read, where a locale would make the file differ between machines - is marked with a
///     <c>// file content, not UI</c> comment on the line before, and passes.
/// </remarks>
public sealed partial class ServerTextsInPlaceTest
{
    private const string FileContentMarker = "// file content, not UI";

    /// <summary>A literal with words, as a C# regular or interpolated string starts it.</summary>
    private const string Literal = """\$?"[^"\n]*[A-Za-z]{2,}[ :][A-Za-z]""";

    private const string Property = @"\b(Message|Title|Reason|Error|FixTitle|Tooltip|Label)\s*[:=]\s*" + Literal;
    private const string Notification = @"\.Show(Error|Warning|Info|Information)\(\s*" + Literal;
    private const string Result = @"new XmlDiagnosticResult\([^,;]+,\s*" + Literal;
    private const string Refusal = @"\b(Fail|Failed|Failure|Refused)\(\s*(null,\s*)?" + Literal;
    private const string Thrown = @"throw new (LuaDebugAdapterException|ModProjectLoadException)\(\s*" + Literal;
    private const string Sink = Property + "|" + Notification + "|" + Result + "|" + Refusal + "|" + Thrown;

    [GeneratedRegex(Sink)]
    private static partial Regex TextAtSink();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No solution above the test binaries.");
    }

    private static IEnumerable<string> ServerSources(string root)
    {
        return Directory.EnumerateDirectories(root, "PG.StarWarsGame.LSP.*")
            .Where(d => !d.EndsWith(".Tests") && !d.Contains("Client") && !d.Contains("BaselineBuilder"))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    /// <summary>Each text at a sink in one file, as <c>file:line code</c>, unless it is marked file content.</summary>
    private static IEnumerable<string> TextsInPlace(string root, string file)
    {
        var text = File.ReadAllText(file);
        var lines = text.Split('\n');
        return TextAtSink().Matches(text)
            .Select(match => text[..match.Index].Count(c => c == '\n'))
            .Where(line => !lines[line].TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(line => line == 0 || !lines[line - 1].Contains(FileContentMarker, StringComparison.Ordinal))
            .Select(line => $"{Path.GetRelativePath(root, file)}:{line + 1} {lines[line].Trim()}");
    }

    [Fact]
    public void NoTextAReaderSeesIsWrittenInPlace()
    {
        var root = RepositoryRoot();
        Assert.Empty(ServerSources(root).SelectMany(file => TextsInPlace(root, file)));
    }
}