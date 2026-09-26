// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Schema.Versioning;

namespace PG.StarWarsGame.LSP.Schema.Tests;

public sealed class SchemaVersionGateTest
{
    // ── the two constants must agree ─────────────────────────────────────────

    // SupportedRange is what the check runs on; HighestSupportedMajor is what classifies an
    // out-of-range version as "newer major" (refuse) rather than "just outside" (load + warn).
    // They are separate declarations, so pin them to each other.
    [Fact]
    public void SupportedRange_AgreesWith_HighestSupportedMajor()
    {
        var major = SchemaVersionGate.HighestSupportedMajor;

        Assert.Equal(SchemaVersionCompatibility.Supported,
            SchemaVersionGate.Check($"{major}.0.0").Compatibility);
        Assert.Equal(SchemaVersionCompatibility.Unsupported,
            SchemaVersionGate.Check($"{major + 1}.0.0").Compatibility);
    }

    // ── in range ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("2.0.5")]
    [InlineData("2.4.0")]
    [InlineData("2.99.99")]
    public void VersionInRange_IsSupported(string version)
    {
        var result = SchemaVersionGate.Check(version);

        Assert.Equal(SchemaVersionCompatibility.Supported, result.Compatibility);
        Assert.True(result.CanLoad);
    }

    // ── newer major: refuse ──────────────────────────────────────────────────

    // The case the gate exists for. A newer MAJOR means the server would misread files, so it
    // loads nothing at all rather than emitting diagnostics it cannot stand behind.
    [Theory]
    [InlineData("3.0.0")]
    [InlineData("3.3.1")]
    [InlineData("11.0.0")]
    public void NewerMajor_IsUnsupported_AndBlocksLoading(string version)
    {
        var result = SchemaVersionGate.Check(version);

        Assert.Equal(SchemaVersionCompatibility.Unsupported, result.Compatibility);
        Assert.False(result.CanLoad);
        Assert.Contains(version, result.Message);
    }

    // ── older than supported ─────────────────────────────────────────────────

    // Same major, older minor: the server knows more than the schema uses, so tags it expects are
    // simply absent. That degrades to no validation for those tags rather than to wrong validation.
    [Fact]
    public void OlderMinor_LoadsWithAWarning()
    {
        var result = SchemaVersionGate.Check("2.0.0", ">=2.1.0 <3.0.0");

        Assert.Equal(SchemaVersionCompatibility.OutOfRange, result.Compatibility);
        Assert.True(result.CanLoad);
    }

    // An older MAJOR is a different matter, and it changed with the 2.0.0 contract. MAJOR means the
    // files' shape changed, not that they hold fewer tags, so such a schema cannot be read at all -
    // loading it would fail in the middle of parsing, which is what this gate exists to prevent.
    [Theory]
    [InlineData("1.99.99")]
    [InlineData("0.9.0")]
    public void OlderMajor_IsRefused(string version)
    {
        var result = SchemaVersionGate.Check(version);

        Assert.Equal(SchemaVersionCompatibility.Unsupported, result.Compatibility);
        Assert.False(result.CanLoad);
        Assert.Contains(version, result.Message);
    }

    // ── missing version: legacy schema ───────────────────────────────────────

    // Every schema published before this field existed omits it. Treating that as an error would
    // fire for every current user, so it loads with nothing louder than a debug log.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingVersion_IsUnversioned_AndLoads(string? version)
    {
        var result = SchemaVersionGate.Check(version);

        Assert.Equal(SchemaVersionCompatibility.Unversioned, result.Compatibility);
        Assert.True(result.CanLoad);
    }

    // ── malformed: refuse ────────────────────────────────────────────────────

    // Compatibility cannot be established, so the gate does not guess. CI validates the field
    // against a semver pattern, so reaching this means the schema bypassed its own checks.
    [Theory]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    [InlineData("one.two.three")]
    [InlineData("1.0.0.0")]
    public void MalformedVersion_IsRefused(string version)
    {
        var result = SchemaVersionGate.Check(version);

        Assert.Equal(SchemaVersionCompatibility.Malformed, result.Compatibility);
        Assert.False(result.CanLoad);
    }

    // ── prerelease ───────────────────────────────────────────────────────────

    // A prerelease of an unsupported major must not slip through: npm range semantics exclude
    // prereleases unless the range itself names one, and 2.0.0-rc.1 is still major 2.
    [Fact]
    public void PrereleaseOfNewerMajor_IsUnsupported()
    {
        var result = SchemaVersionGate.Check("3.0.0-rc.1");

        Assert.Equal(SchemaVersionCompatibility.Unsupported, result.Compatibility);
        Assert.False(result.CanLoad);
    }

    // ── message quality ──────────────────────────────────────────────────────

    // The message is shown to the user in a balloon, so it must say what happened and what to do.
    [Fact]
    public void UnsupportedMessage_NamesBothVersionsAndTheRemedy()
    {
        var result = SchemaVersionGate.Check("3.0.0");

        Assert.Contains("3.0.0", result.Message);
        Assert.Contains(">=2.0.0 <3.0.0", result.Message);
        Assert.Contains("update", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportedResult_HasNoMessage()
    {
        Assert.Empty(SchemaVersionGate.Check("2.2.3").Message);
    }
}