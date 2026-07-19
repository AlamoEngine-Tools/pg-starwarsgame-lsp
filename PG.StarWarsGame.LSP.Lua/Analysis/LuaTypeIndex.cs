// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

public sealed class LuaTypeIndex : ILuaTypeIndex
{
    public static readonly ILuaTypeIndex Empty = new LuaTypeIndex(
        new Dictionary<string, LuaClassDefinition>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, LuaAliasDefinition>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, LuaEnumDefinition>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, LuaAliasDefinition> _aliases;

    private readonly IReadOnlyDictionary<string, LuaClassDefinition> _classes;
    private readonly IReadOnlyDictionary<string, LuaEnumDefinition> _enums;

    private LuaTypeIndex(
        IReadOnlyDictionary<string, LuaClassDefinition> classes,
        IReadOnlyDictionary<string, LuaAliasDefinition> aliases,
        IReadOnlyDictionary<string, LuaEnumDefinition> enums)
    {
        _classes = classes;
        _aliases = aliases;
        _enums = enums;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in classes.Keys) names.Add(k);
        foreach (var k in aliases.Keys) names.Add(k);
        foreach (var k in enums.Keys) names.Add(k);
        AllTypeNames = names;
    }

    public IReadOnlySet<string> AllTypeNames { get; }

    public LuaClassDefinition? GetClass(string typeName)
    {
        return _classes.GetValueOrDefault(typeName);
    }

    public LuaAliasDefinition? GetAlias(string typeName)
    {
        return _aliases.GetValueOrDefault(typeName);
    }

    public LuaEnumDefinition? GetEnum(string typeName)
    {
        return _enums.GetValueOrDefault(typeName);
    }

    public static ILuaTypeIndex Build(IEnumerable<EmmyLuaAnnotations> annotations)
    {
        var classes = new Dictionary<string, LuaClassDefinition>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, LuaAliasDefinition>(StringComparer.OrdinalIgnoreCase);
        var enums = new Dictionary<string, LuaEnumDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var ann in annotations)
        {
            if (ann.ClassDef is { } cls) classes[cls.Name] = cls;
            if (ann.AliasDef is { } alias) aliases[alias.Name] = alias;
            if (ann.EnumDef is { } en) enums[en.Name] = en;
        }

        return new LuaTypeIndex(classes, aliases, enums);
    }
}