// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Semver;

namespace PG.StarWarsGame.LSP.Schema.Versioning;

/// <summary>How a schema's declared version relates to what this server understands.</summary>
public enum SchemaVersionCompatibility
{
    /// <summary>Inside the supported range - load normally.</summary>
    Supported,

    /// <summary>
    ///     No <c>schemaVersion</c> in the manifest. Every schema published before the field existed
    ///     looks like this, so it loads without user-facing noise.
    /// </summary>
    Unversioned,

    /// <summary>
    ///     Outside the supported range but not a newer major - an older schema. Loads: the server
    ///     knows more than the schema uses, so tags it expects are merely absent. Missing tags mean
    ///     no validation for them, never wrong validation.
    /// </summary>
    OutOfRange,

    /// <summary>
    ///     A newer major. The contract changed in a way this server does not implement, so it would
    ///     misread files. Refuses to load.
    /// </summary>
    Unsupported,

    /// <summary>Not parseable as semver - compatibility cannot be established, so refuses to load.</summary>
    Malformed
}

/// <summary>
///     The outcome of checking a schema manifest's <c>schemaVersion</c> against this server.
/// </summary>
/// <param name="Compatibility">What the version means for loading.</param>
/// <param name="DeclaredVersion">Verbatim value from the manifest; null when absent.</param>
/// <param name="SupportedRange">The range this server was checked against.</param>
/// <param name="Message">
///     User-facing explanation, empty when there is nothing to say. Written for a
///     <c>window/showMessage</c> balloon, so it names the versions and the remedy.
/// </param>
public sealed record SchemaVersionCheck(
    SchemaVersionCompatibility Compatibility,
    string? DeclaredVersion,
    string SupportedRange,
    string Message)
{
    /// <summary>False only where loading would produce results the server cannot stand behind.</summary>
    public bool CanLoad =>
        Compatibility is not (SchemaVersionCompatibility.Unsupported or SchemaVersionCompatibility.Malformed);
}

/// <summary>
///     Compares a schema's declared <c>schemaVersion</c> against the range this server implements.
///     <para>
///         The version describes the schema <em>contract</em>, not its contents. MAJOR changes when
///         the server must understand something new or it will misread files (a file's shape
///         changing, a value being removed or renamed); MINOR when values are added to
///         <c>referenceKind</c> / <c>semanticType</c> / <c>type</c> or a manifest category appears,
///         which an older server survives with reduced capability; PATCH for content alone.
///     </para>
///     <para>
///         Only MAJOR can make the server actively wrong, so only MAJOR blocks loading.
///     </para>
/// </summary>
public static class SchemaVersionGate
{
    /// <summary>npm-style range of schema contract versions this server implements.</summary>
    public const string SupportedRange = ">=1.0.0 <2.0.0";

    /// <summary>
    ///     Highest MAJOR covered by <see cref="SupportedRange" />. Declared separately because a
    ///     range answers "is this in?" but not "which side is it out on", and the two directions
    ///     have opposite outcomes. <c>SchemaVersionGateTest.SupportedRange_AgreesWith_HighestSupportedMajor</c>
    ///     keeps them in step.
    /// </summary>
    public const int HighestSupportedMajor = 1;

    public static SchemaVersionCheck Check(string? declaredVersion, string supportedRange = SupportedRange)
    {
        if (string.IsNullOrWhiteSpace(declaredVersion))
            return new SchemaVersionCheck(
                SchemaVersionCompatibility.Unversioned, null, supportedRange, string.Empty);

        var raw = declaredVersion.Trim();

        if (!SemVersion.TryParse(raw, SemVersionStyles.Strict, out var version))
            return new SchemaVersionCheck(
                SchemaVersionCompatibility.Malformed, raw, supportedRange,
                $"The game schema declares an unreadable version '{raw}'. It must be a semantic " +
                "version such as '1.0.0'. XML support is disabled because the schema cannot be " +
                "checked for compatibility.");

        var range = SemVersionRange.ParseNpm(supportedRange);
        if (range.Contains(version))
            return new SchemaVersionCheck(
                SchemaVersionCompatibility.Supported, raw, supportedRange, string.Empty);

        if (version.Major > HighestSupportedMajor)
            return new SchemaVersionCheck(
                SchemaVersionCompatibility.Unsupported, raw, supportedRange,
                $"The game schema is version {raw}, which this version of aet-eaw-edit does not " +
                $"support (it understands {supportedRange}). XML support is disabled until you " +
                "update the extension - loading it anyway would report problems that are not real.");

        return new SchemaVersionCheck(
            SchemaVersionCompatibility.OutOfRange, raw, supportedRange,
            $"The game schema is version {raw}, older than this version of aet-eaw-edit expects " +
            $"({supportedRange}). It will be used as-is; tags added since that schema was " +
            "published will have no validation or completion.");
    }
}
