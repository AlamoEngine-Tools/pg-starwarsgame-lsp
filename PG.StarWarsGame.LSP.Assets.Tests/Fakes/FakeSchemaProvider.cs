using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Assets.Tests.Fakes;

internal sealed class FakeSchemaProvider : ISchemaProvider
{
    private readonly HashSet<string> _knownTypes;

    public FakeSchemaProvider(params string[] knownTypeNames)
    {
        _knownTypes = new HashSet<string>(knownTypeNames, StringComparer.OrdinalIgnoreCase);
    }

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];

    public XmlTagDefinition? GetTag(string tagName)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
    {
        return [];
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string enumName)
    {
        return null;
    }

    public GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        return _knownTypes.Contains(typeName)
            ? new GameObjectTypeDefinition { TypeName = typeName }
            : null;
    }
}