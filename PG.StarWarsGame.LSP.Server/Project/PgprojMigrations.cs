// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json.Nodes;
using PG.StarWarsGame.LSP.Core.Persistence;

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     The <c>.pgproj</c> migration chain.
///     <para>
///         When the next one lands: add it here, bump <see cref="PgprojFormat.Current" />, and give
///         it a <see cref="IDocumentMigration.UserNotice" /> if the change needs anything of the
///         user. That notice is what the proposal says.
///     </para>
/// </summary>
public static class PgprojMigrations
{
    public static readonly IReadOnlyList<IDocumentMigration> All = [new StampInitialIdentity()];

    /// <summary>
    ///     Gives a project file written before any of this the identity it now needs, and changes
    ///     nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The migration a document owes its existing files the moment it stops being something
    ///         we merely read and becomes something we version and write - Scout has the same step
    ///         for a data object that turns from runtime payload into persistent storage. Without
    ///         it, the fields would only ever appear on projects we happen to create or rewrite, so
    ///         the format contract would describe nothing and the first real change would have
    ///         nothing to migrate FROM.
    ///     </para>
    ///     <para>
    ///         It is also what makes an unversioned file readable at all: the store and the loader
    ///         adopt one only while a migration from zero exists. When every project in the wild
    ///         carries its identity and this is retired, an unstamped file stops being adopted and
    ///         starts being refused, which is the intended end state.
    ///     </para>
    ///     <para>
    ///         Idempotent by construction: it sets two fields to constants and touches nothing else,
    ///         so running it twice is running it once.
    ///     </para>
    /// </remarks>
    private sealed class StampInitialIdentity : IDocumentMigration
    {
        public string TypeName => PgprojFormat.TypeName;
        public TypeVersion From => TypeVersion.Zero(PgprojFormat.Current.Namespace);
        public TypeVersion To => PgprojFormat.Current;

        /// <summary>
        ///     Nothing is asked of the user beyond agreeing to the write: no setting moves, no path
        ///     changes, and every key this build does not model is carried through untouched.
        /// </summary>
        public string? UserNotice => null;

        public JsonNode Migrate(JsonNode document)
        {
            if (document is not JsonObject root) return document;

            root["_type"] = PgprojFormat.TypeName;
            root["_typeVersion"] = PgprojFormat.Current.ToString();
            return root;
        }
    }
}

/// <summary>
///     Told when a project file was brought forward in memory, so that something with a user in
///     front of it can offer to persist it.
///     <para>
///         The loader does not prompt: it runs during startup and reload, where a modal question is
///         either ignored or asked several times over. It reports, and the offer is made once the
///         workspace has settled.
///     </para>
/// </summary>
public interface IPgprojMigrationSink
{
    void Migrated(string pgprojPath, JsonNode migrated, IReadOnlyList<string> notices);
}

/// <summary>For contexts with no user to ask - tests, and the baseline builder.</summary>
public sealed class NullPgprojMigrationSink : IPgprojMigrationSink
{
    public void Migrated(string pgprojPath, JsonNode migrated, IReadOnlyList<string> notices)
    {
    }
}
