// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using MessagePack;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

/// <summary>
///     Persists one Lua document's annotation contribution - what <c>ParseAsync</c> writes into
///     <see cref="ILuaAnnotationRepository" /> - so a document served from a cached index snapshot
///     can put it back without being parsed.
/// </summary>
/// <remarks>
///     <para>
///         Parallel DTOs rather than attributes on the runtime records: the runtime shapes use
///         <see cref="ImmutableArray{T}" /> and nested records that would each need a formatter,
///         and a persisted layout is a contract that should not move every time a convenience
///         member is added to a domain type.
///     </para>
///     <para>
///         The layout is cached, so ANY change to it must be accompanied by a bump of
///         <c>ProjectIndexSnapshot.CurrentSchemaVersion</c> - otherwise old bytes are read back
///         under the new shape and the annotations come back subtly wrong rather than absent.
///     </para>
///     <para>MessagePack types must be public - an internal one throws at runtime, not at compile time.</para>
/// </remarks>
public static class LuaAnnotationStateCodec
{
    public static byte[] Serialize(
        ImmutableArray<EmmyLuaAnnotations> annotations,
        IReadOnlyList<(string Name, EmmyLuaAnnotations Ann)> functions)
    {
        var state = new SerializedLuaAnnotationState
        {
            Annotations = annotations.IsDefaultOrEmpty
                ? []
                : annotations.Select(SerializedLuaAnnotation.From).ToArray(),
            Functions = functions
                .Select(f => new SerializedLuaFunctionAnnotation
                {
                    Name = f.Name, Annotation = SerializedLuaAnnotation.From(f.Ann)
                })
                .ToArray()
        };

        return MessagePackSerializer.Serialize(state);
    }

    /// <summary>
    ///     Returns the round-tripped state, or <see langword="null" /> when the bytes cannot be
    ///     read - a snapshot from another build, or a truncated file. Never throws.
    /// </summary>
    public static (ImmutableArray<EmmyLuaAnnotations> Annotations,
        List<(string Name, EmmyLuaAnnotations Ann)> Functions)? Deserialize(byte[] data)
    {
        try
        {
            var state = MessagePackSerializer.Deserialize<SerializedLuaAnnotationState>(data);
            return (
                state.Annotations.Select(a => a.ToRuntime()).ToImmutableArray(),
                state.Functions.Select(f => (f.Name, f.Annotation.ToRuntime())).ToList());
        }
        catch
        {
            return null;
        }
    }
}

[MessagePackObject]
public sealed class SerializedLuaAnnotationState
{
    [Key(0)] public SerializedLuaAnnotation[] Annotations { get; set; } = [];
    [Key(1)] public SerializedLuaFunctionAnnotation[] Functions { get; set; } = [];
}

[MessagePackObject]
public sealed class SerializedLuaFunctionAnnotation
{
    [Key(0)] public string Name { get; set; } = string.Empty;
    [Key(1)] public SerializedLuaAnnotation Annotation { get; set; } = new();
}

[MessagePackObject]
public sealed class SerializedLuaAnnotation
{
    [Key(0)] public string? Description { get; set; }
    [Key(1)] public SerializedLuaClass? ClassDef { get; set; }
    [Key(2)] public SerializedLuaAlias? AliasDef { get; set; }
    [Key(3)] public string? EnumName { get; set; }
    [Key(4)] public bool EnumUseKeys { get; set; }
    [Key(5)] public string? TypeAnnotation { get; set; }
    [Key(6)] public SerializedLuaParam[] Params { get; set; } = [];
    [Key(7)] public SerializedLuaReturn[] Returns { get; set; } = [];
    [Key(8)] public string[] Overloads { get; set; } = [];
    [Key(9)] public string[] GenericParams { get; set; } = [];
    [Key(10)] public bool IsDeprecated { get; set; }
    [Key(11)] public bool IsNodiscard { get; set; }
    [Key(12)] public bool IsAsync { get; set; }

    /// <summary>The access modifier, or -1 for none - the runtime type is nullable.</summary>
    [Key(13)] public int AccessModifier { get; set; } = -1;

    [Key(14)] public string[] SeeRefs { get; set; } = [];

    public static SerializedLuaAnnotation From(EmmyLuaAnnotations a)
    {
        return new SerializedLuaAnnotation
        {
            Description = a.Description,
            ClassDef = a.ClassDef is null ? null : SerializedLuaClass.From(a.ClassDef),
            AliasDef = a.AliasDef is null
                ? null
                : new SerializedLuaAlias
                {
                    Name = a.AliasDef.Name,
                    Variants = Raw(a.AliasDef.Variants)
                },
            EnumName = a.EnumDef?.Name,
            EnumUseKeys = a.EnumDef?.UseKeys ?? false,
            TypeAnnotation = a.TypeAnnotation?.Raw,
            Params = a.Params.IsDefaultOrEmpty
                ? []
                : a.Params.Select(p => new SerializedLuaParam
                {
                    Name = p.Name, IsOptional = p.IsOptional, Type = p.Type.Raw, Description = p.Description
                }).ToArray(),
            Returns = a.Returns.IsDefaultOrEmpty
                ? []
                : a.Returns.Select(r => new SerializedLuaReturn
                {
                    Type = r.Type.Raw, Name = r.Name, Description = r.Description
                }).ToArray(),
            Overloads = a.Overloads.IsDefaultOrEmpty ? [] : [.. a.Overloads],
            GenericParams = a.GenericParams.IsDefaultOrEmpty ? [] : [.. a.GenericParams],
            IsDeprecated = a.IsDeprecated,
            IsNodiscard = a.IsNodiscard,
            IsAsync = a.IsAsync,
            AccessModifier = a.AccessModifier is { } m ? (int)m : -1,
            SeeRefs = a.SeeRefs.IsDefaultOrEmpty ? [] : [.. a.SeeRefs]
        };
    }

    public EmmyLuaAnnotations ToRuntime()
    {
        return new EmmyLuaAnnotations(
            Description,
            ClassDef?.ToRuntime(),
            AliasDef is null
                ? null
                : new LuaAliasDefinition(AliasDef.Name, Refs(AliasDef.Variants)),
            EnumName is null ? null : new LuaEnumDefinition(EnumName, EnumUseKeys),
            TypeAnnotation is null ? null : new LuaTypeRef(TypeAnnotation),
            Params
                .Select(p => new LuaParamAnnotation(p.Name, p.IsOptional, new LuaTypeRef(p.Type), p.Description))
                .ToImmutableArray(),
            Returns
                .Select(r => new LuaReturnAnnotation(new LuaTypeRef(r.Type), r.Name, r.Description))
                .ToImmutableArray(),
            [..Overloads],
            [..GenericParams],
            IsDeprecated,
            IsNodiscard,
            IsAsync,
            AccessModifier < 0 ? null : (LuaAccessModifier)AccessModifier,
            [..SeeRefs]);
    }

    internal static string[] Raw(ImmutableArray<LuaTypeRef> refs)
    {
        return refs.IsDefaultOrEmpty ? [] : refs.Select(r => r.Raw).ToArray();
    }

    internal static ImmutableArray<LuaTypeRef> Refs(string[] raw)
    {
        return raw.Select(r => new LuaTypeRef(r)).ToImmutableArray();
    }
}

[MessagePackObject]
public sealed class SerializedLuaClass
{
    [Key(0)] public string Name { get; set; } = string.Empty;
    [Key(1)] public bool IsExact { get; set; }
    [Key(2)] public string[] Parents { get; set; } = [];
    [Key(3)] public SerializedLuaField[] Fields { get; set; } = [];
    [Key(4)] public string? Description { get; set; }

    public static SerializedLuaClass From(LuaClassDefinition c)
    {
        return new SerializedLuaClass
        {
            Name = c.Name,
            IsExact = c.IsExact,
            Parents = c.Parents.IsDefaultOrEmpty ? [] : [.. c.Parents],
            Fields = c.Fields.IsDefaultOrEmpty
                ? []
                : c.Fields.Select(f => new SerializedLuaField
                {
                    Name = f.Name, IsOptional = f.IsOptional, Type = f.Type.Raw,
                    Description = f.Description, Access = (int)f.Access
                }).ToArray(),
            Description = c.Description
        };
    }

    public LuaClassDefinition ToRuntime()
    {
        return new LuaClassDefinition(
            Name, IsExact, [..Parents],
            Fields
                .Select(f => new LuaFieldDefinition(
                    f.Name, f.IsOptional, new LuaTypeRef(f.Type), f.Description, (LuaAccessModifier)f.Access))
                .ToImmutableArray(),
            Description);
    }
}

[MessagePackObject]
public sealed class SerializedLuaAlias
{
    [Key(0)] public string Name { get; set; } = string.Empty;
    [Key(1)] public string[] Variants { get; set; } = [];
}

[MessagePackObject]
public sealed class SerializedLuaField
{
    [Key(0)] public string Name { get; set; } = string.Empty;
    [Key(1)] public bool IsOptional { get; set; }
    [Key(2)] public string Type { get; set; } = string.Empty;
    [Key(3)] public string? Description { get; set; }
    [Key(4)] public int Access { get; set; }
}

[MessagePackObject]
public sealed class SerializedLuaParam
{
    [Key(0)] public string Name { get; set; } = string.Empty;
    [Key(1)] public bool IsOptional { get; set; }
    [Key(2)] public string Type { get; set; } = string.Empty;
    [Key(3)] public string? Description { get; set; }
}

[MessagePackObject]
public sealed class SerializedLuaReturn
{
    [Key(0)] public string Type { get; set; } = string.Empty;
    [Key(1)] public string? Name { get; set; }
    [Key(2)] public string? Description { get; set; }
}
