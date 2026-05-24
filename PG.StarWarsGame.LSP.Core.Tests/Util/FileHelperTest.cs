// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Tests.Util;

public sealed class FileHelperTest
{
    private static IFileHelper Build() => new FileHelper(new MockFileSystem());

    // ── PathToFileUri ─────────────────────────────────────────────────────────

    [Fact]
    public void PathToFileUri_WindowsBackslash_ReturnsLowercaseFileUri()
    {
        var sut = Build();
        Assert.Equal("file:///c:/game/data/file.xml", sut.PathToFileUri(@"C:\game\data\file.xml"));
    }

    [Fact]
    public void PathToFileUri_WindowsForwardSlash_ReturnsLowercaseFileUri()
    {
        var sut = Build();
        Assert.Equal("file:///c:/game/data/file.xml", sut.PathToFileUri("C:/game/DATA/File.XML"));
    }

    [Fact]
    public void PathToFileUri_UnixAbsolutePath_ReturnsFileUri()
    {
        var sut = Build();
        Assert.Equal("file:///home/user/file.xml", sut.PathToFileUri("/home/user/file.xml"));
    }

    [Fact]
    public void PathToFileUri_AlreadyLowercase_IsIdempotent()
    {
        var sut = Build();
        var input = "c:/game/file.xml";
        Assert.Equal("file:///c:/game/file.xml", sut.PathToFileUri(input));
    }

    // ── NormalizeUri ──────────────────────────────────────────────────────────

    [Fact]
    public void NormalizeUri_FileTripleSlashMixedCase_ReturnsLowercase()
    {
        var sut = Build();
        Assert.Equal("file:///c:/game/file.xml", sut.NormalizeUri("file:///C:/game/File.xml"));
    }

    [Fact]
    public void NormalizeUri_AlreadyCanonical_IsIdempotent()
    {
        var sut = Build();
        Assert.Equal("file:///c:/game/file.xml", sut.NormalizeUri("file:///c:/game/file.xml"));
    }

    [Fact]
    public void NormalizeUri_BareWindowsPath_ConvertsToFileUri()
    {
        var sut = Build();
        Assert.Equal("file:///c:/game/file.xml", sut.NormalizeUri("c:/game/file.xml"));
    }

    [Fact]
    public void NormalizeUri_UnixFileUri_ReturnsLowercase()
    {
        var sut = Build();
        Assert.Equal("file:///home/user/file.xml", sut.NormalizeUri("file:///home/user/file.xml"));
    }

    [Fact]
    public void NormalizeUri_FileDoubleSlash_NormalizesToTripleSlash()
    {
        // Some tools emit file:// (double-slash) without the authority; we normalise it
        // to the canonical file:/// (triple-slash) form.
        var sut = Build();
        Assert.Equal("file:///c:/game/file.xml", sut.NormalizeUri("file://c:/game/file.xml"));
    }

    [Fact]
    public void NormalizeUri_ShortTestUri_PreservesRelativeForm()
    {
        var sut = Build();
        Assert.Equal("file:///test.xml", sut.NormalizeUri("file:///test.xml"));
    }

    // ── UrisEqual ─────────────────────────────────────────────────────────────

    [Fact]
    public void UrisEqual_SameUriDifferentCase_ReturnsTrue()
    {
        var sut = Build();
        Assert.True(sut.UrisEqual("file:///C:/Game/File.xml", "file:///c:/game/file.xml"));
    }

    [Fact]
    public void UrisEqual_PathVsUri_ReturnsTrue()
    {
        var sut = Build();
        Assert.True(sut.UrisEqual(@"C:\game\file.xml", "file:///c:/game/file.xml"));
    }

    [Fact]
    public void UrisEqual_DifferentFiles_ReturnsFalse()
    {
        var sut = Build();
        Assert.False(sut.UrisEqual("file:///c:/a.xml", "file:///c:/b.xml"));
    }

    [Fact]
    public void UrisEqual_SameUriSameCase_ReturnsTrue()
    {
        var sut = Build();
        Assert.True(sut.UrisEqual("file:///c:/game/file.xml", "file:///c:/game/file.xml"));
    }

    // ── FileUriToPath ─────────────────────────────────────────────────────────

    [Fact]
    public void FileUriToPath_WindowsFileUri_ReturnsLocalPath()
    {
        var sut = Build();
        var result = sut.FileUriToPath("file:///c:/game/data/file.xml");
        Assert.Equal(@"c:\game\data\file.xml", result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FileUriToPath_MixedCaseFileUri_NormalizesAndReturnsPath()
    {
        var sut = Build();
        var result = sut.FileUriToPath("file:///C:/Game/File.xml");
        Assert.Equal(@"c:\game\file.xml", result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FileUriToPath_NonFileUri_ReturnsNull()
    {
        var sut = Build();
        Assert.Null(sut.FileUriToPath("https://example.com/file.xml"));
    }

    [Fact]
    public void FileUriToPath_EmptyString_ReturnsNull()
    {
        var sut = Build();
        Assert.Null(sut.FileUriToPath(""));
    }
}
