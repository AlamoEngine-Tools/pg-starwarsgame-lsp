// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json.Nodes;

namespace PG.StarWarsGame.LSP.Core.Persistence;

/// <summary>What running a document's migration chain produced.</summary>
/// <param name="Document">The document at the current version, or null when the chain could not get it there.</param>
/// <param name="Failure">Why, when <paramref name="Document" /> is null. Names the versions involved.</param>
/// <param name="Notices">What each step that ran wants the user told, in the order they ran.</param>
public sealed record DocumentMigrationResult(
    JsonNode? Document, string? Failure, IReadOnlyList<string> Notices);

/// <summary>
///     Runs a document's migration chain: the shared half of reading any versioned document,
///     whether it lives in <c>.aetswg/</c> or is the user's own <c>.pgproj</c>.
///     <para>
///         The gate around it - what an absent version means, whether a newer file is refused, what
///         message the user sees - belongs to the caller, because a hidden sidecar and a project
///         file answer those differently. What they share is this: apply the steps in version
///         order, refuse to pretend when one is missing, and collect what the user should be told.
///     </para>
/// </summary>
public static class DocumentMigrator
{
    public static DocumentMigrationResult Run(
        JsonNode document,
        string typeName,
        TypeVersion from,
        TypeVersion current,
        IReadOnlyList<IDocumentMigration> migrations)
    {
        var notices = new List<string>();
        var version = from;
        var guard = 0;

        while (version.CompareTo(current) < 0)
        {
            var step = migrations.FirstOrDefault(
                m => string.Equals(m.TypeName, typeName, StringComparison.Ordinal)
                     && m.From.Equals(version));

            // A gap is a bug in our registration, not a fact about the file. Reading the document
            // at a version whose shape it does not have would paper over it.
            if (step is null)
                return new DocumentMigrationResult(null,
                    $"version {version.Version} has no migration leading to {current.Version}", notices);

            document = step.Migrate(document);
            if (!string.IsNullOrWhiteSpace(step.UserNotice)) notices.Add(step.UserNotice);
            version = step.To;

            if (++guard > migrations.Count)
                return new DocumentMigrationResult(null, "the migration chain does not terminate", notices);
        }

        // Overshooting means a step declared a To beyond current: the chain is mis-registered, and
        // the document now has a shape nothing here can read.
        if (version.CompareTo(current) != 0)
            return new DocumentMigrationResult(null,
                $"the chain ended at {version.Version}, past the {current.Version} this build writes",
                notices);

        return new DocumentMigrationResult(document, null, notices);
    }

    /// <summary>
    ///     Whether a document type is still in its initial typing - i.e. a migration exists that
    ///     reads the version-zero form.
    ///     <para>
    ///         A file carrying no identity is NOT a document at version zero waiting to be migrated.
    ///         It is read as one only while that handler exists, because that handler is precisely
    ///         what an unversioned file is for. Once it retires, a file with no identity is one we
    ///         cannot place, and adopting it would mean guessing which shape it holds.
    ///     </para>
    /// </summary>
    public static bool HasInitialMigration(
        string typeName, TypeVersion current, IReadOnlyList<IDocumentMigration> migrations)
    {
        var zero = TypeVersion.Zero(current.Namespace);
        return migrations.Any(m => string.Equals(m.TypeName, typeName, StringComparison.Ordinal)
                                   && m.From.Equals(zero));
    }
}
