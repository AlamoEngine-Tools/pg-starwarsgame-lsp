// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Deprecated: nothing can reach this handler, because no tag declares
///     <c>variantMode: ignored</c>.
/// </summary>
/// <remarks>
///     <para>
///         The only producer of <see cref="VariantIgnoredOverrideFact" /> is
///         <c>XmlVariantFactProducer</c>, and it emits one only for a tag whose
///         <see cref="VariantMode" /> is <see cref="VariantMode.Ignored" />. Every tag in
///         <c>schema/eaw/tags</c> is either <c>replace</c> or <c>merge</c>, so the branch is dead.
///     </para>
///     <para>
///         Kept registered rather than deleted because the engine reading it was written for has
///         not been refuted - only unused. If a tag the engine genuinely refuses to let a variant
///         set is ever found, this is the handler for it; until then it costs one DI registration
///         and reports nothing.
///     </para>
/// </remarks>
[Obsolete("Unreachable: no tag declares variantMode: ignored, so the fact is never produced.")]
public sealed class VariantIgnoredOverrideHandler : XmlDiagnosticsHandler<VariantIgnoredOverrideFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.VariantIgnoredOverride;

    protected override IEnumerable<XmlDiagnosticResult> Handle(VariantIgnoredOverrideFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"Tag '{fact.TagName}' is ignored on variants; the engine will not apply it here.")
        ];
    }
}