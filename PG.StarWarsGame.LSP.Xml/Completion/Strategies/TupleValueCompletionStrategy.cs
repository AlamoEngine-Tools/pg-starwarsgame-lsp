// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Completion;

/// <summary>
///     Positional completion for comma-separated tuple value types (e.g. <c>HardPointSfxMap</c>,
///     <c>UnitSpawnTable</c>). Each such type has a shape - which slot holds an enum, a hardcoded
///     set, an object reference, or a free-form value with no completion source. The fixed-shape
///     types are hardcoded here; a TupleList reads its slots from the schema. This strategy looks up
///     that shape per (ValueType, <see cref="TagValueCompletionContext.TupleSlotIndex" />)
///     and queries the same registries <see cref="StandardValueCompletionStrategy" /> uses, via a
///     synthetic single-purpose <see cref="XmlTagDefinition" /> for the slot in question.
/// </summary>
internal sealed class TupleValueCompletionStrategy : IXmlTagValueCompletionStrategy
{
    private readonly IXmlCompletionRegistry _completionRegistry;
    private readonly IXmlValueProposalRegistry _proposals;
    private readonly ISchemaProvider _schema;

    public TupleValueCompletionStrategy(
        ISchemaProvider schema,
        IXmlValueProposalRegistry proposals,
        IXmlCompletionRegistry completionRegistry)
    {
        _schema = schema;
        _proposals = proposals;
        _completionRegistry = completionRegistry;
    }

    public IEnumerable<CompletionItem> Handle(TagValueCompletionContext ctx)
    {
        if (ctx.StoryParamSide is not null || ctx.TagDef is null) return [];

        var proposals = ctx.TagDef.ValueType switch
        {
            XmlValueType.HardPointSfxMap => ctx.TupleSlotIndex switch
            {
                0 => EnumValues(ctx, "HardPointType"),
                _ => ObjectReference(ctx, "SFXEvent")
            },
            XmlValueType.AbilitySfxMap => ctx.TupleSlotIndex switch
            {
                0 => HardcodedSetValues(ctx, "AbilityType"),
                _ => ObjectReference(ctx, "SFXEvent")
            },
            XmlValueType.ConditionalSfxEvent => ctx.TupleSlotIndex switch
            {
                0 => [],
                _ => ObjectReference(ctx, "SFXEvent")
            },
            XmlValueType.UnitSpawnTable => ctx.TupleSlotIndex switch
            {
                0 => ObjectReference(ctx, "GameObjectType"),
                _ => []
            },
            XmlValueType.AbilityModMultiplier => ctx.TupleSlotIndex switch
            {
                0 => EnumValues(ctx, "AbilityMultiplierType"),
                _ => []
            },
            XmlValueType.InaccuracyMap => ctx.TupleSlotIndex switch
            {
                0 => EnumValues(ctx, "GameObjectCategoryType"),
                _ => [] // plain float distance
            },
            XmlValueType.TupleList => TupleListProposals(ctx),
            XmlValueType.ListMap => ListMapProposals(ctx),
            _ => []
        };

        return proposals.Select(p => new CompletionItem
        {
            Label = p.Label,
            Detail = p.Detail,
            LabelDetails = p.Description is not null
                ? new CompletionItemLabelDetails { Description = p.Description }
                : null,
            InsertText = p.InsertText ?? p.Label,
            Kind = CompletionItemKind.Value
        });
    }

    // A TupleList's shape is in the schema: its slots repeat for the whole value, so the item
    // index picks the slot modulo the slot count. A tag without slots, or an untyped slot, has no
    // completion source.
    private IReadOnlyList<ValueProposal> TupleListProposals(TagValueCompletionContext ctx)
    {
        var tag = ctx.TagDef!;
        return tag.Slots.Count == 0 ? [] : SlotProposals(ctx, tag.Slots[ctx.TupleSlotIndex % tag.Slots.Count]);
    }

    // The engine drops a value whose first item is no key, so the first item proposes keys alone.
    // Any later item may start a new group or join the current one - an item is a key when it IS
    // one - so both are offered.
    private IReadOnlyList<ValueProposal> ListMapProposals(TagValueCompletionContext ctx)
    {
        var tag = ctx.TagDef!;
        if (tag.Slots.Count != 2)
            return [];

        var keys = SlotProposals(ctx, tag.Slots[0]);
        return ctx.TupleSlotIndex == 0 ? keys : [.. keys, .. SlotProposals(ctx, tag.Slots[1])];
    }

    private IReadOnlyList<ValueProposal> SlotProposals(TagValueCompletionContext ctx, TupleSlotDefinition slot)
    {
        var tag = ctx.TagDef!;
        switch (slot.ReferenceKind)
        {
            case ReferenceKind.Enum when slot.Enum is not null:
                var enumTag = new XmlTagDefinition
                {
                    Tag = tag.Tag, ValueType = XmlValueType.DynamicEnumValue,
                    ReferenceKind = ReferenceKind.Enum, Enum = slot.Enum
                };
                return _proposals.GetProposals(XmlValueType.DynamicEnumValue, enumTag, ctx.PartialValue);
            case ReferenceKind.ModelFile or ReferenceKind.TextureFile or ReferenceKind.AudioFile
                or ReferenceKind.MapFile:
                var assetTag = new XmlTagDefinition
                {
                    Tag = tag.Tag, ValueType = tag.ValueType, ReferenceKind = slot.ReferenceKind
                };
                return _completionRegistry.GetProposals(assetTag, ctx.PartialValue, ctx.Index);
            case ReferenceKind.XmlObject when (slot.ObjectType?.TypeName ?? slot.ReferenceTypeName) is { } type:
                return ObjectReference(ctx, type);
            default:
                return [];
        }
    }

    private IReadOnlyList<ValueProposal> EnumValues(TagValueCompletionContext ctx, string enumName)
    {
        var enumDef = _schema.GetEnum(enumName);
        if (enumDef is null) return [];

        var synthetic = new XmlTagDefinition
        {
            Tag = ctx.TagDef!.Tag, ValueType = XmlValueType.DynamicEnumValue,
            ReferenceKind = ReferenceKind.Enum, Enum = enumDef
        };
        return _proposals.GetProposals(XmlValueType.DynamicEnumValue, synthetic, ctx.PartialValue);
    }

    private IReadOnlyList<ValueProposal> HardcodedSetValues(TagValueCompletionContext ctx, string setName)
    {
        var set = _schema.AllHardcodedSets.FirstOrDefault(s => s.Name == setName);
        if (set is null) return [];

        var synthetic = new XmlTagDefinition
        {
            Tag = ctx.TagDef!.Tag, ValueType = ctx.TagDef.ValueType,
            ReferenceKind = ReferenceKind.HardcodedSet, HardcodedSet = set
        };
        return _completionRegistry.GetProposals(synthetic, ctx.PartialValue, ctx.Index);
    }

    private IReadOnlyList<ValueProposal> ObjectReference(TagValueCompletionContext ctx, string typeName)
    {
        var synthetic = new XmlTagDefinition
        {
            Tag = ctx.TagDef!.Tag, ValueType = ctx.TagDef.ValueType,
            ReferenceKind = ReferenceKind.XmlObject,
            ObjectType = new GameObjectTypeDefinition { TypeName = typeName }
        };
        return _completionRegistry.GetProposals(synthetic, ctx.PartialValue, ctx.Index);
    }
}