// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json.Nodes;

namespace PG.StarWarsGame.LSP.E2E.Tests.Protocol;

/// <summary>
///     The protocol's tests of the project-wide suppressions file, XML-12 and XML-15: each step needs the
///     server started anew, since it reads <c>.aetswg\suppressions.json</c> once, at startup.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class XmlSuppressionsProtocolTest
{
    private const string Workspace = @"C:\temp\aet-test-xml";
    private const string Joined = "<?xml version=\"1.0\"?>\n<SpaceUnits>\n\t<SpaceUnit Name=\"Scratch_A\">\n" +
                                  "\t\t<Max_Speed>1.<!-- joined -->5</Max_Speed>\n\t</SpaceUnit>\n</SpaceUnits>\n";

    private static readonly string Suppressions = Path.Combine(Workspace, ".aetswg", "suppressions.json");

    private static void Prepare()
    {
        ProtocolWorkspaceFixture.RunSetup("XML-01");
        if (!Directory.Exists(Workspace))
            throw new Exception("$XunitDynamicSkip$C:\\temp\\aet-test-xml not found; run the protocol setup script.");
    }

    private static void WriteSuppressions(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Suppressions)!);
        File.WriteAllText(Suppressions, json);
    }

    // Starts a server, opens Xml_Test.xml with a comment inside a value, and returns its 015-0006 findings.
    private static async Task<(int Count, ProtocolDocument Document, AdHocWorkspaceFixture Server)> OpenJoined()
    {
        var server = await AdHocWorkspaceFixture.StartAsync(Workspace);
        var document = await ProtocolDocument.OpenAsync(server, Path.Combine(Workspace, "Data", "XML", "Xml_Test.xml"),
            Joined);
        return (document.WithCode("aetswg-015-0006").Count, document, server);
    }

    private static JsonNode ReadSuppressions()
    {
        return JsonNode.Parse(File.ReadAllText(Suppressions))!;
    }

    private static List<string> Ids(JsonNode document)
    {
        return [.. document["entries"]!.AsArray().Select(e => (string)e!["id"]!)];
    }

    [Fact]
    public async Task XML_12_ACommentInsideAValue_IsTurnedOnByAFileWithoutItsEntry()
    {
        Prepare();
        const string optedIn = "{ \"_type\": \"aetswg.Suppressions\", \"_typeVersion\": \"aetswg-2.0.0\", \"entries\": [] }";
        WriteSuppressions(optedIn);
        try
        {
            var (_, document, server) = await OpenJoined();
            await using (server)
            {
                var comment = Assert.Single(document.WithCode("aetswg-015-0006"));
                Assert.Equal(3, comment.Range.Start.Line);
                Assert.Equal("Comment inside a value: The game cuts it out and joins the text on both sides, but " +
                             "standard XML readers return two separate text nodes, so other tools may read a " +
                             "different value", comment.Message.TrimEnd('.'));
                Assert.Equal(optedIn, File.ReadAllText(Suppressions));

                var move = Assert.Single(await document.CodeActionsAsync(comment),
                    a => a.Title == "Move the comment above the element");
                Assert.True(move.IsPreferred);
                await document.ApplyAsync(move);
                Assert.Equal("\t\t<!-- joined -->", document.Lines[3]);
                Assert.Equal("\t\t<Max_Speed>1.5</Max_Speed>", document.Lines[4]);
                Assert.Empty(document.WithCode("aetswg-015-0006"));
            }
        }
        finally
        {
            File.Delete(Suppressions);
        }

        var (count, _, cleanup) = await OpenJoined();
        await using (cleanup)
            Assert.Equal(0, count);
    }

    [Fact]
    public async Task XML_15_AVersion1File_GainsTheDefaultEntryOnce()
    {
        Prepare();
        try
        {
            WriteSuppressions("{ \"_type\": \"aetswg.Suppressions\", \"_typeVersion\": \"aetswg-1.0.0\", \"entries\": " +
                              "[ { \"id\": \"aetswg-010-0001\" } ] }");
            var (first, _, server) = await OpenJoined();
            await using (server)
            {
                Assert.Equal(0, first);
                lock (server.MessagesShown)
                    Assert.DoesNotContain(server.MessagesShown, m => m.Message.Contains("suppressions",
                        StringComparison.OrdinalIgnoreCase));
            }

            var migrated = ReadSuppressions();
            Assert.Equal("aetswg-2.0.0", (string?)migrated["_typeVersion"]);
            Assert.Equal(["aetswg-010-0001", "aetswg-015-0006"], Ids(migrated));
            Assert.Equal("Off by default: comments inside values are common practice; delete this entry to report them",
                (string?)migrated["entries"]![1]!["reason"]);
            var written = File.ReadAllText(Suppressions);

            var (_, _, again) = await OpenJoined();
            await using (again)
            {
            }

            Assert.Equal(written, File.ReadAllText(Suppressions));

            var without = ReadSuppressions();
            without["entries"]!.AsArray().RemoveAt(1);
            File.WriteAllText(Suppressions, without.ToJsonString());
            var (shown, _, deleted) = await OpenJoined();
            await using (deleted)
                Assert.Equal(1, shown);
            Assert.Equal(["aetswg-010-0001"], Ids(ReadSuppressions()));

            WriteSuppressions("[ { \"id\": \"aetswg-010-0001\" } ]");
            var (legacy, _, bare) = await OpenJoined();
            await using (bare)
                Assert.Equal(0, legacy);
            var fromArray = ReadSuppressions();
            Assert.Equal("aetswg-2.0.0", (string?)fromArray["_typeVersion"]);
            Assert.Equal(["aetswg-010-0001", "aetswg-015-0006"], Ids(fromArray));
        }
        finally
        {
            File.Delete(Suppressions);
        }
    }
}
