// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     Seeding an empty credits file writes a run of rows whose keys repeat - HEADER, CENTER,
///     CENTER, HEADER - because a credits key is a formatting directive rather than an identifier.
///     <para>
///         The order those land in IS the content: it is what the crawl plays. This walks the whole
///         way round - compose the inserts, write, read back - because the order can be lost at
///         either end and the symptom looks the same from the editor.
///     </para>
/// </summary>
public sealed class CreditsSeedRoundTripTest
{
    // The shape a seeded German file takes: the English list copied in, ready to translate over.
    private static readonly (string Key, string Value)[] Seeded =
    [
        ("HEADER", "Directed by"),
        ("CENTER", "ALICE"),
        ("CENTER", "BOB"),
        ("HEADER", "Produced by"),
        ("CENTER", "CAROL"),
    ];

    // .xml is deliberately absent: an empty .xml file has no root element and is malformed rather
    // than empty, so the reader refuses it with a clear message - which is the house rule for bad
    // input, not something to parse leniently around.
    [Theory]
    [InlineData(".properties")]
    [InlineData(".csv")]
    public void SeedingAnEmptyCreditsFile_KeepsEveryLineInOrder(string extension)
    {
        var path = $"/mod/text/creditstext_english{extension}";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [path] = new(string.Empty) });
        var editor = Build(fs);

        // Exactly what the editor stages: one insert per line, each at the end of what is there.
        var commands = Seeded
            .Select((row, at) => new LocEditCommandDto(
                "insertRow", at, Key: row.Key, Values: [new LocValueDto("ENGLISH", row.Value)]))
            .ToList();

        var result = editor.Apply(string.Empty, extension, commands, Path.GetFileName(path));
        Assert.True(result.Success, result.Error);

        var readBack = editor.DryRunRows(
            WriteAndReturn(fs, path, result.NewText!), []);

        Assert.Equal(
            Seeded.Select(r => r.Key).ToArray(),
            readBack.Rows.Select(r => r.Key).ToArray());
        Assert.Equal(
            Seeded.Select(r => r.Value).ToArray(),
            readBack.Rows.Select(r => r.Values.Single().Value).ToArray());
    }

    private static string WriteAndReturn(MockFileSystem fs, string path, string text)
    {
        fs.File.WriteAllText(path, text);
        return path;
    }

    private static ILocalisationDocumentEditor Build(MockFileSystem fs)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.TryAddSingleton<ILspConfigurationProvider>(new FakeLspConfigurationProvider());
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        services.AddSingleton<ILocalisationDocumentEditor, LocalisationDocumentEditor>();

        return services.BuildServiceProvider().GetRequiredService<ILocalisationDocumentEditor>();
    }
}
