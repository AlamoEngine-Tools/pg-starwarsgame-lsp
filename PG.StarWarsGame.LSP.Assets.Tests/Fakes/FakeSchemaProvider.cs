using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Assets.Tests.Fakes;

internal sealed class FakeSchemaProvider : ISchemaProvider
{
    private readonly HashSet<string> _knownTypes;

    public event EventHandler? SchemaRefreshed { add { } remove { } }

    public IReadOnlyList<XmlTagDefinition>       AllTags        => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition>          AllEnums       => [];

    public FakeSchemaProvider(params string[] knownTypeNames)
    {
        _knownTypes = new HashSet<string>(knownTypeNames, StringComparer.OrdinalIgnoreCase);
    }

    public XmlTagDefinition?                 GetTag(string tagName)               => null;
    public IReadOnlyList<XmlTagDefinition>   GetAllTagDefinitions(string tagName)  => [];
    public IReadOnlyList<XmlTagDefinition>   GetTagsForType(string typeName)       => [];
    public EnumDefinition?                   GetEnum(string enumName)              => null;

    public GameObjectTypeDefinition? GetObjectType(string typeName) =>
        _knownTypes.Contains(typeName)
            ? new GameObjectTypeDefinition { TypeName = typeName }
            : null;
}
