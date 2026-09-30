// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     A project's raw icon sources: every name known up front, each image decoded only when
///     something asks for it.
/// </summary>
/// <remarks>
///     <para>
///         Building the catalog used to decode every source image. On a mod shipping 1655 loose
///         icons across 21 MB that converted a whole workspace's art to PNG so a card could show two
///         of them, and it happened on the first request that touched icons.
///     </para>
///     <para>
///         The split is what makes that avoidable: the NAMES decide which layer answers a lookup and
///         which sources are missing from the mega texture, and both questions are answered by the
///         directory scan alone. Only the pixels are expensive, and they are wanted one at a time.
///     </para>
///     <para>
///         Every result is remembered, failures included - a source that will not decode will not
///         decode on the next card either, and retrying it would pay the cost repeatedly for an
///         answer that cannot change.
///     </para>
/// </remarks>
public sealed class LooseIconStore
{
    private readonly IReadOnlyDictionary<string, LooseIcon> _catalog;
    private readonly Func<LooseIcon, byte[]?> _decode;

    /// <summary>Decoded pixels per name; a null value records a decode that failed.</summary>
    private readonly Dictionary<string, byte[]?> _decoded =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _gate = new();

    /// <param name="catalog">Output of <see cref="LooseIconCatalog.Scan" />.</param>
    /// <param name="decode">
    ///     Turns one source into PNG bytes, or null when the format is unsupported or the file will
    ///     not decode. Injected so the cost can be counted in a test, and so this type needs no
    ///     filesystem of its own.
    /// </param>
    public LooseIconStore(IReadOnlyDictionary<string, LooseIcon> catalog, Func<LooseIcon, byte[]?> decode)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(decode);

        _catalog = catalog;
        _decode = decode;
    }

    /// <summary>Every catalogued source name. Available without decoding anything.</summary>
    public IReadOnlySet<string> Names =>
        _catalog.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Names that were asked for and would not decode - an unsupported format, or a corrupt
    ///     file.
    /// </summary>
    /// <remarks>
    ///     Reports what was actually ATTEMPTED, so it stays empty until something is looked up. That
    ///     is the honest reading: a source nobody has asked for is not known to be broken, and
    ///     claiming otherwise would mean decoding everything, which is the cost this type exists to
    ///     avoid.
    /// </remarks>
    public IReadOnlyList<string> Undecodable
    {
        get
        {
            lock (_gate)
            {
                return _decoded.Where(e => e.Value is null).Select(e => e.Key).ToList();
            }
        }
    }

    /// <summary>The usual production wiring: decode through <see cref="LooseIconDecoder" />.</summary>
    public static LooseIconStore Over(IFileSystem fileSystem, IReadOnlyDictionary<string, LooseIcon> catalog)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        return new LooseIconStore(catalog, icon => LooseIconDecoder.TryDecode(fileSystem, icon));
    }

    /// <summary>
    ///     A store over bytes the caller already holds.
    /// </summary>
    /// <remarks>
    ///     Nothing here is lazy - the work is already done - but it lets every caller take the same
    ///     type, so the baseline sidecar and the tests that hand over fixtures need no second path.
    /// </remarks>
    public static LooseIconStore FromDecoded(IReadOnlyDictionary<string, byte[]> decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        var catalog = decoded.Keys.ToDictionary(
            name => name,
            name => new LooseIcon(name, name, string.Empty),
            StringComparer.OrdinalIgnoreCase);

        return new LooseIconStore(catalog, icon => decoded[icon.Name]);
    }

    /// <summary>
    ///     The PNG for <paramref name="name" />, decoding it the first time it is asked for.
    ///     False when the name is not catalogued, or its source will not decode.
    /// </summary>
    public bool TryGet(string name, out byte[]? png)
    {
        png = null;
        if (string.IsNullOrWhiteSpace(name)) return false;

        lock (_gate)
        {
            if (_decoded.TryGetValue(name, out var remembered))
            {
                png = remembered;
                return remembered is not null;
            }

            if (!_catalog.TryGetValue(name, out var icon)) return false;

            png = _decode(icon);
            _decoded[name] = png;
            return png is not null;
        }
    }
}
