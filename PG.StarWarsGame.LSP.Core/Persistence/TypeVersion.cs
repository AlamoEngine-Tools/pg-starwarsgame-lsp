// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using Semver;

namespace PG.StarWarsGame.LSP.Core.Persistence;

/// <summary>
///     The version a persisted document carries in its own <c>_typeVersion</c> field: a namespace
///     and a semantic version, written <c>aetswg-1.2.0</c>.
///     <para>
///         Eclipse Scout's shape, deliberately - it serializes <c>{"_type": "lorem.ExampleEntity",
///         "_typeVersion": "lorem-1.2.0"}</c>, and this framework is modelled on its data object
///         migration. The namespace says who owns the shape; the version says which revision of it
///         the file on disk holds.
///     </para>
///     <para>
///         The version belongs to the document TYPE, never to the application: stamping a release
///         number rewrites every file on every ship and still does not say whether the shape moved.
///     </para>
/// </summary>
public readonly record struct TypeVersion(string Namespace, SemVersion Version)
    : IComparable<TypeVersion>
{
    /// <summary>
    ///     The version of a document written before any of this existed - one with no
    ///     <c>_typeVersion</c> at all. It is what the first migration of each document declares as
    ///     its <see cref="IDocumentMigration.From" />.
    /// </summary>
    public static TypeVersion Zero(string ns)
    {
        return new TypeVersion(ns, new SemVersion(0));
    }

    /// <summary>
    ///     Builds a version directly, for the constant each document declares. Parsing a literal at
    ///     startup would turn a typo into "unreadable version" against every file the user owns.
    /// </summary>
    public static TypeVersion Of(string ns, int major, int minor = 0, int patch = 0)
    {
        return new TypeVersion(ns, new SemVersion(major, minor, patch));
    }

    /// <summary>
    ///     Reads the <c>namespace-version</c> form. Splits at the FIRST dash: a namespace may carry
    ///     dots but no dash, while the version's own dashes belong to its pre-release part.
    /// </summary>
    public static bool TryParse(string? raw, out TypeVersion value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var dash = raw.IndexOf('-');
        if (dash <= 0 || dash == raw.Length - 1) return false;

        var ns = raw[..dash];
        // Strict, so a two-part "1.2" is refused rather than read as something it does not say.
        if (!SemVersion.TryParse(raw[(dash + 1)..], SemVersionStyles.Strict, out var version)) return false;

        value = new TypeVersion(ns, version);
        return true;
    }

    /// <summary>Namespace first, so the ordering is total; within one namespace it is the version.</summary>
    public int CompareTo(TypeVersion other)
    {
        var ns = string.CompareOrdinal(Namespace, other.Namespace);
        return ns != 0 ? ns : Version.ComparePrecedenceTo(other.Version);
    }

    public override string ToString()
    {
        return $"{Namespace}-{Version}";
    }
}

/// <summary>
///     The signature a document type pins beside its version. Held next to the type it describes so
///     that a reviewer sees both move together, and the build fails when only the shape did.
/// </summary>
/// <param name="TypeName">The document's <c>_type</c>, e.g. <c>aetswg.StoryLayout</c>.</param>
/// <param name="Version">The version the pinned shape belongs to.</param>
/// <param name="Signature">The shape's signature, from <see cref="DocumentSignature.Of" />.</param>
public sealed record DocumentShapePin(string TypeName, TypeVersion Version, string Signature)
{
    /// <summary>
    ///     Whether <paramref name="type" /> still has the pinned shape at the pinned version.
    ///     <para>
    ///         False means one of two things, and both are the same mistake: the shape changed and
    ///         the version did not, or the version moved and the pin was not updated with it.
    ///     </para>
    /// </summary>
    public bool Matches(Type type, TypeVersion version)
    {
        return Version.Equals(version)
               && string.Equals(Signature, DocumentSignature.Of(type), StringComparison.Ordinal);
    }
}

/// <summary>Guards a document's <c>_typeVersion</c> against a shape change nobody versioned.</summary>
/// <remarks>
///     Scout ships <c>AbstractDataObjectSignatureTest</c> for the same reason. Without it the whole
///     framework rests on remembering to bump, and the first forgotten bump silently writes a file
///     the next release cannot read - which is the problem the framework exists to prevent.
/// </remarks>
public static class DocumentSignature
{
    private static readonly HashSet<Type> Leaves =
    [
        typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
        typeof(Guid), typeof(Uri)
    ];

    /// <summary>The shape as a stable hash - what gets pinned.</summary>
    public static string Of(Type type)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Describe(type)));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    ///     The shape as text - what to look at when the hash changed and the diff does not say why.
    ///     Properties are sorted by name: declaration order is not part of a JSON document's shape,
    ///     and reflection does not promise a stable one anyway.
    /// </summary>
    public static string Describe(Type type)
    {
        return Describe(type, []);
    }

    private static string Describe(Type type, HashSet<Type> seen)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying.IsEnum) return underlying.Name + ":enum";
        if (underlying.IsPrimitive || Leaves.Contains(underlying)) return underlying.Name;

        if (TryGetElementType(underlying, out var element))
            return $"[{Describe(element, seen)}]";

        // A document that refers back to its own type describes its shape once; the recursion is
        // structure, not a difference between two shapes.
        if (!seen.Add(underlying)) return underlying.Name + ":recursive";

        var members = underlying
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
            .Select(p => $"{Camel(p.Name)}:{Describe(p.PropertyType, seen)}")
            .OrderBy(text => text, StringComparer.Ordinal);

        seen.Remove(underlying);
        return "{" + string.Join(",", members) + "}";
    }

    private static bool TryGetElementType(Type type, [NotNullWhen(true)] out Type? element)
    {
        if (type.IsArray)
        {
            element = type.GetElementType()!;
            return true;
        }

        var enumerable = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType
                                 && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        element = enumerable?.GetGenericArguments()[0];
        return element is not null;
    }

    /// <summary>Matches the camelCase the sidecars are serialized with, so the text reads like the file.</summary>
    private static string Camel(string name)
    {
        return name.Length == 0 || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
