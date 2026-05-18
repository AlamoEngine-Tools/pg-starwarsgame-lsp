using PG.StarWarsGame.Files.XML;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     A named game object ready for projection into a <see cref="Core.Symbols.GameSymbol" />.
///     Wraps a <see cref="PG.StarWarsGame.Files.XML.Data.NamedXmlObject" /> without depending on
///     the concrete engine type, keeping the projector testable.
/// </summary>
public readonly record struct ProjectableEntry(
    string Name,
    string ClassificationName,
    XmlLocationInfo Location
);