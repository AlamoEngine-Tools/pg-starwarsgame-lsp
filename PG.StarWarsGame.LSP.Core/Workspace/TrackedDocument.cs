namespace PG.StarWarsGame.LSP.Core.Workspace;

public sealed record TrackedDocument(string Uri, string Text, int Version);