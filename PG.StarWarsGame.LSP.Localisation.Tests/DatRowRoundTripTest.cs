// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PG.StarWarsGame.LSP.Localisation.Rows;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Localisation.Tests;

/// <summary>
///     Compiled DAT files, read through the editor's row model. They are not written from it: a DAT
///     is what a build makes of the project.
///     <para>
///         Exercised against the real <c>creditstext_english.dat</c> the workspace ships rather than
///         a fixture: the format is binary and produced by the game's own tools, so a hand-built
///         sample would only prove the code agrees with itself.
///     </para>
/// </summary>
public sealed class DatRowRoundTripTest
{
    private static string RealCreditsDat()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PG.StarWarsGame.LSP.slnx")))
            dir = dir.Parent;

        var path = Path.Combine(dir!.FullName, "eaw", "Data", "Text", "creditstext_english.dat");
        if (!File.Exists(path))
            Assert.Skip("Needs the checked-in eaw/ tree.");
        return path;
    }

    private static ILocalisationRowReader Reader()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportPetroglyphHashing();
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.TryAddSingleton<ILspConfigurationProvider>(new FakeLspConfigurationProvider());
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        services.AddSingleton<IMessageCatalog>(ResxMessageCatalog.Server("en"));
        return services.BuildServiceProvider().GetRequiredService<ILocalisationRowReader>();
    }

    [Fact]
    public void ReadFile_RealCreditsDat_YieldsRowsInOrder()
    {
        var reader = Reader();

        var document = reader.ReadFile(RealCreditsDat());

        Assert.NotEmpty(document.Rows);
        Assert.Equal(["ENGLISH"], document.Languages);
        Assert.Equal(Enumerable.Range(0, document.Rows.Count), document.Rows.Select(r => r.Index));
        Assert.All(document.Rows, r => Assert.Single(r.Values));
    }

    // A credits DAT is unsorted and repeats keys - that is the format, not a defect - so the reader
    // must not collapse or reorder anything.
    [Fact]
    public void ReadFile_RealCreditsDat_KeepsDuplicateKeys()
    {
        var reader = Reader();

        var rows = reader.ReadFile(RealCreditsDat()).Rows;

        Assert.True(rows.Select(r => r.Key).Distinct().Count() < rows.Count,
            "expected the shipped credits file to repeat keys");
    }
}
