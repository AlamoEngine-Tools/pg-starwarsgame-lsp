namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>Deserialized form of _index.json from the schema repository.</summary>
public sealed class SchemaManifest
{
    public List<string> Tags { get; set; } = [];
    public List<string> Types { get; set; } = [];
    public List<string> Enums { get; set; } = [];
}