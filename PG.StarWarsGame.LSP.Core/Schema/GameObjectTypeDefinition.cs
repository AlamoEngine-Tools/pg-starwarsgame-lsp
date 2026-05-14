namespace PG.StarWarsGame.LSP.Core.Schema;

public record GameObjectTypeDefinition
{
    public required string TypeName { get; init; }

    /// <summary>XML tag that carries the object's unique name, e.g. "Name". Null for singleton types.</summary>
    public string? NameTag { get; init; }

    /// <summary>Locale → description text.</summary>
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();
}