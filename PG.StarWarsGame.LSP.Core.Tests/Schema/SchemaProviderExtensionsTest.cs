// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Tests.Schema;

/// <summary>
///     A document is in a scanned directory when the declared directory is a whole run of segments of
///     its path, whatever the slashes or the case (#310); a folder whose name merely starts the same is
///     a different folder.
/// </summary>
public sealed class SchemaProviderExtensionsTest
{
    private static ISchemaProvider Scanning(params string[] directories)
    {
        var builder = new SchemaProviderBuilder();
        foreach (var directory in directories)
            builder.WithScannedDirectory(new ScannedDirectoryDefinition(directory, ["Campaign"]));
        return builder.Build();
    }

    [Theory]
    [InlineData("file:///d:/mod/Data/XML/Campaigns/Foo.xml")]
    [InlineData(@"D:\mod\data\xml\campaigns\foo.xml")]
    [InlineData("/home/x/mod/DATA/XML/CAMPAIGNS/FOO.XML")]
    public void ADocumentUnderTheDeclaredDirectory_IsInIt_WhateverSlashesOrCase(string path)
    {
        Assert.True(Scanning("data/xml/campaigns/").IsInScannedDirectory(path));
    }

    [Fact]
    public void AFolderWhoseNameMerelyStartsTheSame_IsNotTheDirectory()
    {
        Assert.False(Scanning("data/xml/campaigns/")
            .IsInScannedDirectory("file:///d:/mod/Data/XML/CampaignsOld/Foo.xml"));
    }

    [Fact]
    public void ADocumentElsewhere_IsNotInIt()
    {
        Assert.False(Scanning("data/xml/campaigns/").IsInScannedDirectory("file:///d:/mod/Data/XML/Foo.xml"));
    }

    [Fact]
    public void WithoutDeclarations_NothingIsInOne()
    {
        Assert.False(Scanning().IsInScannedDirectory("file:///d:/mod/Data/XML/Campaigns/Foo.xml"));
    }
}