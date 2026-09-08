// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Parsing;

public sealed class XmlGameDocumentParser : IGameDocumentParser
{
    // Registered file-types that are referenced as workspace files by workspaceFile tags (campaign
    // *_Story_Name → StoryPlotManifest; manifest Active_Plot/Suspended_Plot → StoryParser). A doc
    // typed one of these is indexed as a navigable file-symbol so those references resolve to it.
    private static readonly string[] WorkspaceFileTypes =
        [StoryReferenceTypes.PlotManifestFileTypeName, StoryReferenceTypes.ThreadFileTypeName];

    private readonly ILspConfigurationProvider? _configProvider;
    private readonly IEaWXmlContext? _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly ILogger<XmlGameDocumentParser> _logger;
    private readonly IXmlParseCache? _parseCache;
    private readonly ISchemaProvider _schema;

    // parseCache is optional so minimal test setups can omit it; production wires the shared
    // cache so the indexing parse seeds it - the diagnostics publish and the first hover/inlay
    // request after an edit then reuse this parse instead of re-parsing. A null configProvider
    // (test convenience) means every feature flag reads as enabled. eaWXmlContext is optional too;
    // without it a doc's xml-relative path is unknown, so workspace-file symbols are skipped.
    public XmlGameDocumentParser(IFileHelper fileHelper, ISchemaProvider schema,
        IFileTypeRegistry fileTypeRegistry, ILogger<XmlGameDocumentParser> logger,
        IXmlParseCache? parseCache = null, ILspConfigurationProvider? configProvider = null,
        IEaWXmlContext? eaWXmlContext = null)
    {
        _fileHelper = fileHelper;
        _schema = schema;
        _fileTypeRegistry = fileTypeRegistry;
        _logger = logger;
        _parseCache = parseCache;
        _configProvider = configProvider;
        _eaWXmlContext = eaWXmlContext;
    }

    public bool CanParse(string fileExtension)
    {
        return fileExtension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<DocumentIndex> ParseAsync(
        string documentUri, string text, int version, CancellationToken ct)
    {
        var canonicalUri = _fileHelper.NormalizeUri(documentUri);
        var parsed = _parseCache?.GetOrParse(canonicalUri, text) ?? ParsedXmlDocument.Parse(text);
        var doc = parsed.Html;

        var registeredTypes = _fileTypeRegistry.GetTypesForFile(canonicalUri);
        var lineIndex = parsed.LineIndex;

        // References are collected first so the symbol passes can append the typed variant-base
        // reference for each object they index (the enclosing object's type is only known here).
        var references = CollectReferences(doc, canonicalUri, lineIndex, ct);
        var symbols = CollectSymbolsFromRegistry(doc, canonicalUri, lineIndex, registeredTypes, references, ct);
        symbols.AddRange(CollectSubObjectListSymbols(doc, canonicalUri, lineIndex, references, ct));
        symbols.AddRange(CollectWorkspaceFileSymbols(canonicalUri, registeredTypes));

        if ((_configProvider?.Current.Features.Story.Symbols ?? true) &&
            registeredTypes.Contains(StoryReferenceTypes.ThreadFileTypeName, StringComparer.OrdinalIgnoreCase))
            StoryDocumentSymbolCollector.Collect(parsed, canonicalUri, _schema, symbols, references);

        var groupMemberships = CollectGroupMemberships(doc, canonicalUri, lineIndex, ct);

        return ValueTask.FromResult(new DocumentIndex(
            canonicalUri, version,
            symbols.ToImmutableArray(),
            references.ToImmutableArray(),
            GroupMemberships: groupMemberships.ToImmutableArray()));
    }

    // A story plot-manifest / thread file is indexed as a navigable file-symbol keyed by its
    // xml-relative path, so workspaceFile references (campaign *_Story_Name, manifest Active_Plot/
    // Suspended_Plot) resolve to it for go-to / find-references / rename. Needs the xml roots to
    // make the path relative; without eaWXmlContext (minimal test setups) it is skipped.
    private List<GameSymbol> CollectWorkspaceFileSymbols(
        string documentUri, ImmutableArray<string> registeredTypes)
    {
        if (_eaWXmlContext is null) return [];

        var fileType = WorkspaceFileTypes.FirstOrDefault(t =>
            registeredTypes.Contains(t, StringComparer.OrdinalIgnoreCase));
        if (fileType is null) return [];

        var relativePath = _eaWXmlContext.TryGetXmlRelativePath(documentUri);
        if (string.IsNullOrEmpty(relativePath)) return [];

        return
        [
            new GameSymbol(
                WorkspaceFileKey.Create(fileType, relativePath),
                GameSymbolKind.WorkspaceFile,
                fileType,
                new FileOrigin(documentUri, 0, 0),
                null)
        ];
    }

    private List<GameSymbol> CollectSymbolsFromRegistry(HtmlDocument doc, string documentUri,
        LineOffsetIndex lineIndex, ImmutableArray<string> registeredTypes, List<GameReference> references,
        CancellationToken ct)
    {
        var typeDef = registeredTypes
            .Select(t => _schema.GetObjectType(t))
            .FirstOrDefault(t => t is not null);

        if (typeDef is null) return [];

        var symbols = new List<GameSymbol>();
        var rootContainer = doc.DocumentNode.ChildNodes
            .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element);
        if (rootContainer is null) return symbols;

        // A SINGLETON type - no NameTag - has exactly one instance, so its type name is its id and
        // the root element itself is the object. Without this, everything in GameConstants that is
        // not enum-shaped (ShipNameTextFiles, the Encyclopedia_* geometry, the Corruption_*
        // block) is unreachable through the index, and each consumer has to re-read the file off
        // disk - losing mod-over-baseline layering in the process.
        if (typeDef.NameTag is null)
        {
            // The registry says which types a FILE may hold; it does not prove the root element is
            // one of them. Matching the name keeps a mistyped registry entry from inventing an
            // object out of an unrelated document.
            if (!string.Equals(rootContainer.Name, typeDef.TypeName, StringComparison.OrdinalIgnoreCase))
                return symbols;

            symbols.Add(new GameSymbol(
                typeDef.TypeName, GameSymbolKind.XmlObject, typeDef.TypeName,
                new FileOrigin(documentUri, rootContainer.Line - 1,
                    XmlUtility.GetTagBracketColumn(rootContainer)),
                null));
            return symbols;
        }

        foreach (var node in rootContainer.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            ct.ThrowIfCancellationRequested();
            var id = GetNameAttribute(node, typeDef.NameTag);
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogDebug("Type '{Type}' element at line {Line} has no Name attribute - skipped",
                    typeDef.TypeName, node.Line);
            }
            else
            {
                var col = FindNameAttributeValueColumn(node, typeDef.NameTag, lineIndex);
                var (variantBaseId, variantRef) = ResolveVariant(node, typeDef.TypeName, documentUri, lineIndex);
                if (variantRef is not null) references.Add(variantRef);
                symbols.Add(new GameSymbol(id, GameSymbolKind.XmlObject, typeDef.TypeName,
                    new FileOrigin(documentUri, node.Line - 1, col), null, variantBaseId));
            }
        }

        return symbols;
    }

    /// <summary>
    ///     Detects a <c>Variant_Of_Existing_Type</c> child (a tag with
    ///     <see cref="TagSemanticType.VariantParent" />) on an object node and returns its base id plus
    ///     a typed <see cref="GameReference" /> to that base. <paramref name="enclosingTypeName" /> is the
    ///     variant object's own type, so the base must be of the same type - this lets the existing
    ///     unresolved-reference and type-mismatch handlers validate the inheritance link for free.
    /// </summary>
    private (string? BaseId, GameReference? Reference) ResolveVariant(
        HtmlNode objectNode, string enclosingTypeName, string documentUri, LineOffsetIndex lineIndex,
        string? ownerPrefix = null)
    {
        foreach (var child in objectNode.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var tagDef = _schema.GetTag(child.Name);
            if (tagDef?.SemanticType != TagSemanticType.VariantParent) continue;

            var innerText = child.InnerText;
            var trimmed = innerText.Trim();
            if (trimmed.Length == 0) return (null, null);

            var (line, column, length) = XmlUtility.GetValuePosition(child, lineIndex);

            // The base id/reference must live in the same id-space as objectNode's own id - for
            // top-level objects that's the bare name (ownerPrefix is null); for abilities it's
            // owner-scoped ("{ownerId}$Name", see CollectSubObjectListSymbols) to avoid coincidentally
            // resolving to an unrelated object's same-named ability.
            var baseId = ownerPrefix is not null ? $"{ownerPrefix}${trimmed}" : trimmed;
            var reference = new GameReference(baseId, GameSymbolKind.XmlObject,
                enclosingTypeName, documentUri, line, column, length);
            return (baseId, reference);
        }

        return (null, null);
    }

    private static int? FindNameAttributeValueColumn(HtmlNode node, string nameTag, LineOffsetIndex lineIndex)
    {
        var attr = node.Attributes.FirstOrDefault(a =>
            a.Name.Equals(nameTag, StringComparison.OrdinalIgnoreCase));
        if (attr is null) return null;
        return lineIndex.GetPosition(attr.ValueStartIndex).Col;
    }

    private List<GameReference> CollectReferences(HtmlDocument doc, string documentUri, LineOffsetIndex lineIndex,
        CancellationToken ct)
    {
        var references = new List<GameReference>();
        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var child in node.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var tagDef = _schema.GetTag(child.Name);
                if (tagDef is null) continue;

                if (tagDef.ReferenceKind == ReferenceKind.Enum &&
                    tagDef.Enum?.Kind == EnumKind.DynamicXml)
                {
                    if (HasChildElement(child)) continue;
                    CollectEnumReferences(child, tagDef.Enum.Name, lineIndex, documentUri, references);
                    continue;
                }

                // Presence_Induced_Animations: "AnimationStateId, ObjectName, ..." - the first
                // token is an engine animation state (not indexable), the rest are game objects
                // whose presence triggers it. Record those as object references so
                // go-to-definition and unresolved-reference validation cover them.
                if (tagDef.ValidationOverride?.ValidationId == "presence-induced-animations")
                {
                    if (HasChildElement(child)) continue;
                    CollectPresenceInducedObjectReferences(child, lineIndex, documentUri, references);
                    continue;
                }

                // (GameObjectCategoryType, float) tuples: record slot 0 as an enum: reference
                // so go-to-definition/rename work on the category token. Membership is validated
                // by InaccuracyMapHandler; XmlIndexFactProducer skips enum: ids.
                if (tagDef.ValueType == XmlValueType.InaccuracyMap)
                {
                    if (HasChildElement(child)) continue;
                    CollectInaccuracyCategoryReference(child, lineIndex, documentUri, references);
                    continue;
                }

                // Repeated (planet, mode) pairs flattened into one list. Only the even slots name an
                // object; the odd ones are battle modes, and indexing those as objects would make
                // every "land"/"space" token an unresolved reference.
                if (tagDef.SemanticType == TagSemanticType.PlanetModePairList)
                {
                    if (HasChildElement(child)) continue;
                    CollectPlanetModePairReferences(child, tagDef, lineIndex, documentUri, references);
                    continue;
                }

                // (key, SFXEvent) tuples: slot 0 is an enum / hardcoded ability code / unit type and
                // slot 1 is the SFXEvent name. These tags carry no referenceKind - the pair is
                // validated by the *SfxMap handlers - so without this the SFXEvent half is invisible
                // to go-to-definition even though the same event resolves from a plain
                // SFXEventReference tag. The slot is optional and legitimately left empty.
                if (tagDef.ValueType is XmlValueType.HardPointSfxMap or XmlValueType.AbilitySfxMap
                    or XmlValueType.ConditionalSfxEvent)
                {
                    if (HasChildElement(child)) continue;
                    CollectTupleSfxEventReference(child, lineIndex, documentUri, references);
                    continue;
                }

                // (unit type, int count) tuples e.g. "StarViper_Squadron, 2": slot 0 names a game
                // object. These tags carry no referenceKind - the pair is validated by
                // UnitSpawnTableHandler - so without this the unit half is invisible to
                // go-to-definition/rename. Slot 0 is recorded as a wildcard-typed object reference so
                // the generic unresolved-reference pipeline owns its existence check (the handler then
                // validates only the tuple shape and the count).
                if (tagDef.ValueType == XmlValueType.UnitSpawnTable)
                {
                    if (HasChildElement(child)) continue;
                    CollectUnitSpawnUnitReference(child, lineIndex, documentUri, references);
                    continue;
                }

                // (damage type, clone) pairs - `Death_Clone`. Both slots carry a reference and
                // neither was recorded: the tag has no referenceKind because DeathCloneSpecHandler
                // validates the shape, so go-to-definition on the clone name found nothing at all.
                // Slot 0 is a DamageType enum value, slot 1 an ordinary GameObjectType.
                if (tagDef.ValueType == XmlValueType.DeathCloneSpec)
                {
                    if (HasChildElement(child)) continue;
                    CollectDeathCloneReferences(child, lineIndex, documentUri, references);
                    continue;
                }

                // Campaign per-faction / force-deployment tuples with fixed-meaning comma slots
                // (Home_Location "Faction, Planet"; Starting_Credits/Tech_Level/Max_Tech_Level
                // "Faction, Number"; Starting_Forces/Special_Case_Production "Faction, Planet,
                // Unit"). They carry no referenceKind - the shape/number is validated by the
                // Per*Handler/ForceDeploymentListHandler - so without this the faction and object
                // tokens are invisible to go-to-definition. Faction slots resolve against the
                // Faction pool, planet/unit slots against GameObjectType; numeric slots carry no
                // type and are left to the handler.
                if (FactionTupleSlotTypes(tagDef.ValueType) is { Count: > 0 } slotTypes)
                {
                    if (HasChildElement(child)) continue;
                    CollectFactionTupleReferences(child, slotTypes, lineIndex, documentUri, references);
                    continue;
                }

                // Campaign Markup_Filename "Faction, MarkupFile": only the leading faction is an
                // indexable object; the GUI hint-markup file is not a workspace object, so slot 1 is
                // intentionally left unmodelled (a reference to it would only be a false unresolved).
                if (tagDef.SemanticType == TagSemanticType.FactionMarkupPairList)
                {
                    if (HasChildElement(child)) continue;
                    CollectFactionTupleReferences(child, FactionOnlySlotTypes, lineIndex, documentUri, references);
                    continue;
                }

                // File references (campaign *_Story_Name / Story_Name, manifest Active_Plot /
                // Suspended_Plot / Lua_Script). Emitted so go-to / find-references / rename resolve
                // to the file-symbol; existence is owned by the campaign story chain, so these are
                // exempt from the generic unresolved-reference diagnostic (XmlIndexFactProducer).
                if (tagDef.ReferenceKind == ReferenceKind.WorkspaceFile)
                {
                    if (HasChildElement(child)) continue;
                    CollectWorkspaceFileReferences(child, tagDef, lineIndex, documentUri, references);
                    continue;
                }

                if (tagDef.ReferenceKind != ReferenceKind.XmlObject) continue;
                if (tagDef.SemanticType == TagSemanticType.ReferenceGroup) continue;
                // Variant base references are emitted by the symbol passes with the enclosing
                // object's type as ExpectedTypeName; skip here to avoid a duplicate wildcard-typed one.
                if (tagDef.SemanticType == TagSemanticType.VariantParent) continue;
                // A reference value is leaf text. An element that itself contains child elements is an
                // object definition whose tag name collides with a reference tag (e.g. the
                // <Faction Name="X">…</Faction> container vs. a <Faction>X</Faction> reference) - using
                // its InnerText would capture the whole object as one bogus reference.
                if (HasChildElement(child)) continue;

                var innerText = child.InnerText;
                var ownerPrefix = tagDef.SemanticType == TagSemanticType.OwnerScopedReference
                    ? FindEnclosingObjectId(node)
                    : null;

                // The mirror case: the value names an ability without saying which object owns it, so
                // the bare name cannot match the owner-scoped id it was indexed under. Marking the
                // reference lets resolution search across owners without every other bare reference
                // gaining that fallback (and losing its unresolved-reference diagnostic with it).
                var ownerAgnostic = tagDef.SemanticType == TagSemanticType.OwnerAgnosticReference;

                foreach (var (name, tokenOffset) in SplitReferenceNames(tagDef, innerText))
                {
                    var (line, column, length) =
                        XmlUtility.GetInnerOffsetValuePosition(child, tokenOffset, name.Length, lineIndex);
                    var targetId = ownerPrefix is not null
                        ? $"{ownerPrefix}{GameIndex.OwnerScopeSeparator}{name}"
                        : ownerAgnostic
                            ? OwnerAgnosticReferenceId.Create(name)
                            : name;

                    references.Add(new GameReference(
                        targetId,
                        GameSymbolKind.XmlObject,
                        tagDef.ObjectType?.TypeName,
                        documentUri,
                        line,
                        column,
                        length));
                }
            }
        }

        return references;
    }

    // Every token AFTER the leading animation-state id of a Presence_Induced_Animations value,
    // as wildcard-typed object references.
    private static void CollectPresenceInducedObjectReferences(HtmlNode child,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var innerText = child.InnerText;
        var first = true;
        foreach (var (token, tokenOffset) in XmlUtility.SplitListWithOffsets(innerText))
        {
            if (first)
            {
                first = false; // the animation state id - not a game object
                continue;
            }

            var (line, column, length) =
                XmlUtility.GetInnerOffsetValuePosition(child, tokenOffset, token.Length, lineIndex);
            references.Add(new GameReference(
                token,
                GameSymbolKind.XmlObject,
                null,
                documentUri,
                line,
                column,
                length));
        }
    }

    // Slot 0 of a UnitSpawnTable tuple ("StarViper_Squadron, 2") as a wildcard-typed game object
    // reference. Only the unit half is an object; the count is validated by UnitSpawnTableHandler.
    // ExpectedTypeName stays null so resolution is by name across any object type, matching the
    // untyped lookup the handler used to perform.
    private static void CollectUnitSpawnUnitReference(HtmlNode child,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var innerText = child.InnerText;
        var comma = innerText.IndexOf(',');
        var slot = comma < 0 ? innerText : innerText[..comma];
        var token = slot.Trim();
        if (token.Length == 0) return;

        var tokenOffset = slot.IndexOf(token, StringComparison.Ordinal);
        var (line, column, length) =
            XmlUtility.GetInnerOffsetValuePosition(child, tokenOffset, token.Length, lineIndex);
        references.Add(new GameReference(
            token,
            GameSymbolKind.XmlObject,
            null,
            documentUri,
            line,
            column,
            length));
    }

    // Fixed-meaning slot types per Campaign tuple ValueType, indexed by comma slot. A null entry
    // marks a slot that is not an object reference (the per-faction numeric value) and is left to
    // the shape handler; an absent index (slot beyond the array) is likewise not emitted.
    private static readonly string?[] PerFactionPlanetSlotTypes = ["Faction", "GameObjectType"];
    private static readonly string?[] PerFactionValueSlotTypes = ["Faction"];
    private static readonly string?[] ForceDeploymentSlotTypes = ["Faction", "GameObjectType", "GameObjectType"];

    // Markup_Filename: slot 0 is a Faction, slot 1 (the markup file) is not indexable and not emitted.
    private const string FactionTypeName = "Faction";

    private static readonly string?[] FactionOnlySlotTypes = [FactionTypeName];

    private static IReadOnlyList<string?> FactionTupleSlotTypes(XmlValueType valueType)
    {
        return valueType switch
        {
            XmlValueType.PerFactionPlanet => PerFactionPlanetSlotTypes,
            XmlValueType.PerFactionValue => PerFactionValueSlotTypes,
            XmlValueType.ForceDeploymentList => ForceDeploymentSlotTypes,
            _ => []
        };
    }

    // Each comma slot of a fixed-meaning Campaign tuple, emitted as a typed object reference when
    // that slot position carries a target type. Empty slots yield nothing; a slot beyond the
    // declared arity (e.g. a stray extra token) is ignored, leaving the tuple shape to the handler.
    private static void CollectFactionTupleReferences(HtmlNode child, IReadOnlyList<string?> slotTypes,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        foreach (var (index, token, offset) in CommaSlotsWithOffsets(child.InnerText))
        {
            if (index >= slotTypes.Count) break;
            var typeName = slotTypes[index];
            if (typeName is null) continue;

            var (line, column, length) =
                XmlUtility.GetInnerOffsetValuePosition(child, offset, token.Length, lineIndex);
            references.Add(new GameReference(
                token, GameSymbolKind.XmlObject, typeName, documentUri, line, column, length));
        }
    }

    // Positional comma split: yields (slot index, trimmed token, offset) for every non-empty slot,
    // preserving the slot index across empty slots so fixed-meaning positions stay aligned. Splits
    // on commas ONLY - faction/object names never contain a comma, and a positional split must not
    // also break on the spaces the authors write around tokens.
    private static IEnumerable<(int Index, string Token, int Offset)> CommaSlotsWithOffsets(string input)
    {
        var pos = 0;
        var index = 0;
        foreach (var part in input.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                yield return (index, trimmed, pos + part.IndexOf(trimmed, StringComparison.Ordinal));
            pos += part.Length + 1; // +1 for the consumed comma
            index++;
        }
    }

    // Slot 0 of an InaccuracyMap tuple ("Bomber, 15.0") as an enum: reference, mirroring
    // CollectEnumReferences' id format for plain dynamic-enum tags.
    private static void CollectInaccuracyCategoryReference(HtmlNode child,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var innerText = child.InnerText;
        var comma = innerText.IndexOf(',');
        var slot = comma < 0 ? innerText : innerText[..comma];
        var token = slot.Trim();
        if (token.Length == 0) return;

        var tokenOffset = slot.IndexOf(token, StringComparison.Ordinal);
        var (line, column, length) =
            XmlUtility.GetInnerOffsetValuePosition(child, tokenOffset, token.Length, lineIndex);
        references.Add(new GameReference(
            $"enum:GameObjectCategoryType/{token}",
            null,
            null,
            documentUri,
            line,
            column,
            length));
    }

    /// <summary>
    ///     The planet half of each (planet, mode) pair, as an object reference. Slots alternate, so
    ///     every even token is a planet and every odd one a battle mode; a trailing planet with no
    ///     mode is still indexed, leaving the malformed pair to the diagnostics handler.
    /// </summary>
    // A workspaceFile tag references a file by path/name. The generic list split normalises '/'
    // and '\' to separators, which would shred a file path, so single-value tags take the whole
    // trimmed value and the Story_Name pair list splits on commas ONLY. Both halves of that pair
    // are indexed: the odd slot as the plot-manifest file, the even slot as the Faction object it
    // names - without the latter a mistyped faction resolves to nothing, is never validated, and
    // offers no go-to.
    private static void CollectWorkspaceFileReferences(HtmlNode child, XmlTagDefinition tagDef,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var referenceType = tagDef.ReferenceTypeName;
        if (referenceType is null) return;

        var innerText = child.InnerText;

        if (tagDef.SemanticType == TagSemanticType.FactionPlotFilePairList)
        {
            var slot = 0;
            foreach (var (token, offset) in XmlUtility.SplitCommaWithOffsets(innerText))
                if (slot++ % 2 == 1)
                    AddWorkspaceFileReference(child, referenceType, token, offset, lineIndex,
                        documentUri, references);
                else
                    AddFactionSlotReference(child, token, offset, lineIndex, documentUri, references);
            return;
        }

        var value = innerText.Trim();
        if (value.Length == 0) return;
        AddWorkspaceFileReference(child, referenceType, value,
            innerText.IndexOf(value, StringComparison.Ordinal), lineIndex, documentUri, references);
    }

    private static void AddWorkspaceFileReference(HtmlNode child, string referenceType, string token,
        int offset, LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var (line, column, length) =
            XmlUtility.GetInnerOffsetValuePosition(child, offset, token.Length, lineIndex);
        references.Add(new GameReference(
            WorkspaceFileKey.Create(referenceType, token),
            GameSymbolKind.WorkspaceFile,
            referenceType,
            documentUri, line, column, length));
    }

    private static void AddFactionSlotReference(HtmlNode child, string token, int offset,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var (line, column, length) =
            XmlUtility.GetInnerOffsetValuePosition(child, offset, token.Length, lineIndex);
        references.Add(new GameReference(
            token, GameSymbolKind.XmlObject, FactionTypeName, documentUri, line, column, length));
    }

    private static void CollectPlanetModePairReferences(HtmlNode child, XmlTagDefinition tagDef,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var innerText = child.InnerText;
        var slot = 0;
        foreach (var (token, offset) in XmlUtility.SplitListWithOffsets(innerText))
        {
            if (slot++ % 2 != 0) continue; // odd slot: battle mode, validated not indexed

            var (line, column, length) =
                XmlUtility.GetInnerOffsetValuePosition(child, offset, token.Length, lineIndex);
            references.Add(new GameReference(
                token,
                GameSymbolKind.XmlObject,
                tagDef.ObjectType?.TypeName,
                documentUri,
                line,
                column,
                length));
        }
    }

    // Slot 1 of a (key, SFXEvent) tuple, as an SFXEvent object reference. Vanilla data never uses
    // more than two slots, and the SFXEvent slot is frequently absent ("DEFEND, ") - both the
    // one-token and empty-second-token forms simply yield no reference.
    private static void CollectTupleSfxEventReference(HtmlNode child,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var innerText = child.InnerText;
        var comma = innerText.IndexOf(',');
        if (comma < 0) return;

        var slot = innerText[(comma + 1)..];
        var token = slot.Trim();
        if (token.Length == 0) return;

        var tokenOffset = comma + 1 + slot.IndexOf(token, StringComparison.Ordinal);
        var (line, column, length) =
            XmlUtility.GetInnerOffsetValuePosition(child, tokenOffset, token.Length, lineIndex);
        references.Add(new GameReference(
            token,
            GameSymbolKind.XmlObject,
            "SFXEvent",
            documentUri,
            line,
            column,
            length));
    }

    /// <summary>
    ///     Both slots of a <c>Death_Clone</c> pair: the damage type, then the clone it produces.
    /// </summary>
    /// <remarks>
    ///     The clone is recorded with no expected type name, so it resolves BY NAME. A death clone
    ///     is an ordinary GameObjectType and the tag does not narrow it - naming a type here would
    ///     manufacture mismatches on the perfectly legal variety the data uses.
    ///     <para>
    ///         A row with one slot records nothing. Half a pair is the handler's diagnostic to
    ///         make; inventing a reference for it would report the missing clone twice.
    ///     </para>
    /// </remarks>
    private static void CollectDeathCloneReferences(HtmlNode child,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var innerText = child.InnerText;
        var comma = innerText.IndexOf(',');
        if (comma < 0) return;

        AddSlot(innerText[..comma], 0, $"enum:{DamageTypeEnum}/", null, null);
        AddSlot(innerText[(comma + 1)..], comma + 1, string.Empty, GameSymbolKind.XmlObject, null);

        void AddSlot(string slot, int slotOffset, string prefix, GameSymbolKind? kind, string? type)
        {
            var token = slot.Trim();
            if (token.Length == 0) return;

            var tokenOffset = slotOffset + slot.IndexOf(token, StringComparison.Ordinal);
            var (line, column, length) =
                XmlUtility.GetInnerOffsetValuePosition(child, tokenOffset, token.Length, lineIndex);

            references.Add(new GameReference(
                prefix + token, kind, type, documentUri, line, column, length));
        }
    }

    /// <summary>The schema enum a <c>Death_Clone</c>'s first slot names.</summary>
    private const string DamageTypeEnum = "DamageType";

    private static void CollectEnumReferences(HtmlNode child, string enumName,
        LineOffsetIndex lineIndex, string documentUri, List<GameReference> references)
    {
        var innerText = child.InnerText;
        foreach (var (token, tokenOffset) in XmlUtility.SplitListWithOffsets(innerText))
        {
            var (line, column, length) =
                XmlUtility.GetInnerOffsetValuePosition(child, tokenOffset, token.Length, lineIndex);
            references.Add(new GameReference(
                $"enum:{enumName}/{token}",
                null,
                null,
                documentUri,
                line,
                column,
                length));
        }
    }

    private List<DocumentGroupMembership> CollectGroupMemberships(HtmlDocument doc, string documentUri,
        LineOffsetIndex lineIndex, CancellationToken ct)
    {
        var memberships = new List<DocumentGroupMembership>();

        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var child in node.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var tagDef = _schema.GetTag(child.Name);
                if (tagDef?.SemanticType != TagSemanticType.ReferenceGroup) continue;
                if (tagDef.ReferenceKind != ReferenceKind.XmlObject) continue;
                // Skip object-definition containers whose name collides with a reference-group tag.
                if (HasChildElement(child)) continue;

                var innerText = child.InnerText;
                var trimmed = innerText.Trim();
                if (trimmed.Length == 0) continue;

                // Tag-value cursor position
                var tokenOffset = innerText.IndexOf(trimmed, StringComparison.Ordinal);
                var absPos = child.InnerStartIndex + tokenOffset;
                var (tagLine, tagColumn) = lineIndex.GetPosition(absPos);

                // Parent-name navigation target
                var memberTypeName = tagDef.ObjectType?.TypeName;
                var nameTag = memberTypeName is not null
                    ? _schema.GetObjectType(memberTypeName)?.NameTag
                    : null;

                var memberLine = node.Line - 1;
                int? memberColumn = null;
                if (nameTag is not null)
                {
                    var parentId = GetNameAttribute(node, nameTag);
                    if (parentId.Length > 0)
                        memberColumn = FindNameAttributeValueColumn(node, nameTag, lineIndex);
                }

                memberships.Add(new DocumentGroupMembership(
                    new GroupMembership(trimmed, memberTypeName,
                        new FileOrigin(documentUri, memberLine, memberColumn)),
                    tagLine, tagColumn, trimmed.Length));
            }
        }

        return memberships;
    }

    // Walks up from node to find the nearest ancestor that identifies a game object, then returns
    // that ancestor's name-attribute value. Used for owner-scoped ability symbols and
    // OwnerScopedReference tags. An ancestor qualifies when its element name is a registered
    // object type (whose NameTag then applies) OR when it simply carries a Name attribute - real
    // game files use concrete element names (<SpaceUnit>, <SpecialStructure>, …) that are NOT
    // schema object types (the schema models one umbrella GameObjectType), so without the
    // Name-attribute fallback every owner lookup fails and ability ids collide across objects.
    private string? FindEnclosingObjectId(HtmlNode node)
    {
        var current = node.ParentNode;
        while (current is { NodeType: HtmlNodeType.Element })
        {
            var typeDef = _schema.GetObjectType(XmlUtility.ToPascalCase(current.Name));
            if (typeDef?.NameTag is not null)
            {
                var id = GetNameAttribute(current, typeDef.NameTag);
                return string.IsNullOrEmpty(id) ? null : id;
            }

            var nameAttr = GetNameAttribute(current, "Name");
            if (!string.IsNullOrEmpty(nameAttr))
                return nameAttr;

            current = current.ParentNode;
        }

        return null;
    }

    private static bool HasChildElement(HtmlNode node)
    {
        return node.ChildNodes.Any(n => n.NodeType == HtmlNodeType.Element);
    }

    private static string GetNameAttribute(HtmlNode node, string nameTag)
    {
        // HAP lowercases attribute names; match case-insensitively.
        var attr = node.Attributes.FirstOrDefault(a =>
            a.Name.Equals(nameTag, StringComparison.OrdinalIgnoreCase));
        return attr?.Value?.Trim() ?? string.Empty;
    }

    private static IEnumerable<(string Name, int Offset)> SplitReferenceNames(
        XmlTagDefinition tagDef, string innerText)
    {
        var multiValue = tagDef.SemanticType == TagSemanticType.PrerequisiteExpression
                         || tagDef.ValueType is XmlValueType.GameObjectTypeReferenceList
                             or XmlValueType.TypeReferenceList
                             or XmlValueType.NameReferenceList
                             or XmlValueType.PerFactionObjectList;

        if (!multiValue)
        {
            var trimmed = innerText.Trim();
            if (trimmed.Length > 0)
                yield return (trimmed, innerText.IndexOf(trimmed, StringComparison.Ordinal));
            yield break;
        }

        foreach (var (token, offset) in XmlUtility.SplitListWithOffsets(innerText))
            yield return (token, offset);
    }

    private List<GameSymbol> CollectSubObjectListSymbols(
        HtmlDocument doc, string documentUri, LineOffsetIndex lineIndex, List<GameReference> references,
        CancellationToken ct)
    {
        var symbols = new List<GameSymbol>();

        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var child in node.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            {
                var tagDef = _schema.GetTag(child.Name);
                if (tagDef?.ValueType != XmlValueType.AbilityDefinitionSubObjectList) continue;

                // Find the enclosing game object's ID so abilities can be scoped to their owner,
                // preventing false duplicate-symbol errors when two units share an ability name.
                // Same resolution as OwnerScopedReference tags (incl. the Name-attribute fallback
                // for concrete element names that are not schema object types).
                var ownerId = FindEnclosingObjectId(child);

                foreach (var abilityNode in child.ChildNodes
                             .Where(n => n.NodeType == HtmlNodeType.Element))
                {
                    var typeName = XmlUtility.ToPascalCase(abilityNode.Name);
                    var objectType = _schema.GetObjectType(typeName);
                    if (objectType?.NameTag is null) continue;

                    var abilityName = GetNameAttribute(abilityNode, objectType.NameTag);
                    if (string.IsNullOrEmpty(abilityName)) continue;

                    var ownerPrefix = string.IsNullOrEmpty(ownerId) ? null : ownerId;
                    var id = ownerPrefix is null ? abilityName : $"{ownerPrefix}${abilityName}";
                    var col = FindNameAttributeValueColumn(abilityNode, objectType.NameTag, lineIndex);
                    var (variantBaseId, variantRef) =
                        ResolveVariant(abilityNode, typeName, documentUri, lineIndex, ownerPrefix);
                    if (variantRef is not null) references.Add(variantRef);
                    symbols.Add(new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
                        new FileOrigin(documentUri, abilityNode.Line - 1, col), null, variantBaseId));
                }
            }
        }

        return symbols;
    }
}