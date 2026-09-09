// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json.Nodes;

namespace PG.StarWarsGame.LSP.Core.Persistence;

/// <summary>
///     One step of a persisted document's history: the shape at <see cref="From" /> rewritten into
///     the shape at <see cref="To" />.
///     <para>
///         Migrations run over the RAW tree rather than over an old DTO class, so a superseded shape
///         never has to stay alive in the codebase to be readable. They chain - 1.0.0 to 1.1.0 to
///         2.0.0 - so each one only has to know about the step it owns.
///     </para>
///     <para>
///         Write them so that running one again is harmless. The chain applies each step once, by
///         version, but a document's INITIAL typing has to assume an unversioned file is at the
///         oldest version there is and run everything from there - so a step that is only correct
///         the first time is a step that is wrong on exactly the files it was written for.
///     </para>
/// </summary>
public interface IDocumentMigration
{
    /// <summary>The document's <c>_type</c>. A migration only ever sees its own document.</summary>
    string TypeName { get; }

    /// <summary>The version this step reads. The first migration of a document reads <see cref="TypeVersion.Zero" />.</summary>
    TypeVersion From { get; }

    /// <summary>The version this step writes.</summary>
    TypeVersion To { get; }

    /// <summary>
    ///     What to tell the user when this step runs on a file they own, or null when the change
    ///     needs nothing from them.
    ///     <para>
    ///         This is what the <c>.pgproj</c> upgrade prompt says. A migration that requires a
    ///         manual step and stays silent about it is worse than one that refuses.
    ///     </para>
    /// </summary>
    string? UserNotice { get; }

    /// <summary>
    ///     Rewrites the document. The envelope fields are present on a versioned document and may
    ///     be read.
    ///     <para>
    ///         Takes a <see cref="JsonNode" /> rather than a <see cref="JsonObject" /> because a
    ///         version-zero file need not be an object at all: both sidecars that exist today are
    ///         bare collections, and an array cannot carry an envelope. Giving it one is exactly
    ///         what their first migration does. Everything from <see cref="TypeVersion.Zero" />
    ///         onwards must return an object.
    ///     </para>
    /// </summary>
    JsonNode Migrate(JsonNode document);
}

/// <summary>
///     Where a sidecar file lives. Returns null when there is no project to hold one, which is not
///     an error: the store then keeps its value for the session.
/// </summary>
public interface ISidecarLocator
{
    string? TryLocate(string fileName);
}

/// <summary>How a load ended. Every outcome except <see cref="RefusedNewer" /> yields a usable value.</summary>
public enum SidecarStatus
{
    /// <summary>Read at the current version, nothing to do.</summary>
    Loaded,

    /// <summary>Read at an older version and brought forward; the file has been rewritten.</summary>
    Migrated,

    /// <summary>Nothing readable was there. The value is the default, and the file is left alone.</summary>
    Defaulted,

    /// <summary>
    ///     The file is NEWER than this build understands. It is not read, and it is not written
    ///     either - see <see cref="SidecarStore{T}.TrySave" />.
    /// </summary>
    RefusedNewer
}

/// <summary>The outcome of a load: always a value, plus what happened to get it.</summary>
/// <param name="Value">The document, or the default when nothing readable was found.</param>
/// <param name="Status">How the load ended.</param>
/// <param name="Message">Why, when the outcome was not a plain success. User-facing.</param>
/// <param name="UserNotices">What each migration that ran wants the user told, in the order they ran.</param>
public sealed record SidecarLoad<T>(
    T Value, SidecarStatus Status, string? Message, IReadOnlyList<string> UserNotices);
