// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json.Nodes;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Persistence;
using PG.StarWarsGame.LSP.Core.Project;

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
    public static readonly IReadOnlyList<IDocumentMigration> All = [new StampInitialIdentity(), new NameTheGame()];

    /// <summary>The version the files written before the target game carried.</summary>
    private static readonly TypeVersion IdentityOnly = TypeVersion.Of(PgprojFormat.Current.Namespace, 1);

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
        public TypeVersion To => IdentityOnly;

        /// <summary>
        ///     Nothing is asked of the user beyond agreeing to the write: no setting moves, no path
        ///     changes, and every key this build does not model is carried through untouched.
        /// </summary>
        public CatalogueText? UserNotice => null;

        public JsonNode Migrate(JsonNode document)
        {
            if (document is not JsonObject root) return document;

            root["_type"] = PgprojFormat.TypeName;
            root["_typeVersion"] = To.ToString();
            return root;
        }
    }

    /// <summary>
    ///     Writes the game a project is built for into every file that predates the field: FoC, the
    ///     more modded game and the one every such project was checked against until now.
    /// </summary>
    /// <remarks>
    ///     It only fills a gap. A value already there - written by hand ahead of the format - is the
    ///     author's and stays, so running it twice is running it once.
    /// </remarks>
    private sealed class NameTheGame : IDocumentMigration
    {
        public string TypeName => PgprojFormat.TypeName;
        public TypeVersion From => IdentityOnly;
        public TypeVersion To => PgprojFormat.Current;

        /// <summary>An Empire at War mod has to change the value, so the proposal says how.</summary>
        public CatalogueText UserNotice => new("PgprojMigrations_TargetsForcesOfCorruption");

        public JsonNode Migrate(JsonNode document)
        {
            if (document is not JsonObject root) return document;

            if (!root.ContainsKey("game"))
                root["game"] = TargetGameNames.Of(TargetGame.FOC);
            root["_typeVersion"] = To.ToString();
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

/// <summary>
///     The other half of <see cref="IPgprojMigrationSink" />: asks the user about everything
///     reported to the sink since the last call.
///     <para>
///         Separate from the sink because the two are called from opposite sides of the startup
///         gate. Reporting happens while the workspace loads; ASKING blocks on a dialog, so it must
///         come after the gate is open - held in front of it, a user who takes twenty seconds to
///         read the diff is a server that answers nothing for twenty seconds.
///     </para>
/// </summary>
public interface IPgprojMigrationOffer
{
    Task OfferPendingAsync(CancellationToken ct);
}

/// <summary>For contexts with no user to ask - tests, and the baseline builder.</summary>
public sealed class NullPgprojMigrationSink : IPgprojMigrationSink
{
    public void Migrated(string pgprojPath, JsonNode migrated, IReadOnlyList<string> notices)
    {
    }
}