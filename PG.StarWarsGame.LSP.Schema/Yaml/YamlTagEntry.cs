namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>YAML deserialization model for a single tag entry.</summary>
internal sealed class YamlTagEntry
{
    public string Tag { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ReferenceKind { get; set; }
    public string? ReferenceType { get; set; }
    public string? EnumName { get; set; }
    public string? SemanticType { get; set; }
    public string? ValueGroup { get; set; }
    public bool Deprecated { get; set; }
    public string? AvailableSince { get; set; }
    public Dictionary<string, string> Description { get; set; } = [];
    public bool MultipleAllowed { get; set; }
}

internal sealed class YamlTagFile
{
    public List<YamlTagEntry> Tags { get; set; } = [];
}