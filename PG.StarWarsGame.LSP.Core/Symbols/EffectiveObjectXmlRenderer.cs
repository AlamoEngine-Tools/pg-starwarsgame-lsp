// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Renders an <see cref="EffectiveObject" /> as a read-only XML document, annotating each tag with an
///     XML comment describing its provenance (inherited / overridden / merged / added). Consumed by the
///     <c>aet/getEffectiveObject</c> request to back the effective-object virtual document.
/// </summary>
public static class EffectiveObjectXmlRenderer
{
    public static string Render(EffectiveObject obj)
    {
        var element = obj.TypeName ?? "GameObject";
        var sb = new StringBuilder();

        sb.Append("<!-- Effective object: ").Append(obj.ObjectId);
        if (obj.TypeName is not null)
            sb.Append(" (").Append(obj.TypeName).Append(')');
        sb.AppendLine(" -->");

        if (obj.Chain.Length > 1)
            sb.Append("<!-- variant chain: ").Append(string.Join(" -> ", obj.Chain)).AppendLine(" -->");

        if (obj.Cyclic)
            sb.Append("<!-- WARNING: cyclic inheritance detected at ").Append(obj.CycleObjectId).AppendLine(" -->");

        sb.Append('<').Append(element).Append(" Name=\"").Append(obj.ObjectId).AppendLine("\">");

        foreach (var tag in obj.Tags)
        {
            var note = ProvenanceNote(tag);
            if (note is not null)
                sb.Append("    <!-- ").Append(note).AppendLine(" -->");
            foreach (var line in tag.Fragment.Split('\n'))
                sb.Append("    ").AppendLine(line.TrimEnd('\r'));
        }

        sb.Append("</").Append(element).AppendLine(">");
        return sb.ToString();
    }

    private static string? ProvenanceNote(EffectiveTag tag)
    {
        return tag.Provenance switch
        {
            VariantProvenance.Inherited => $"inherited from {tag.OriginObjectId}",
            VariantProvenance.Overridden => tag.BaseValue is null
                ? "overrides base"
                : $"overrides base - was {Describe(tag.BaseValue)}",
            VariantProvenance.Merged => tag.BaseValue is null
                ? "merged with base"
                : $"merged with base - base contributes {Describe(tag.BaseValue)}",
            VariantProvenance.Added => "added by variant",
            _ => null
        };
    }

    /// <summary>
    ///     The replaced value, rendered for an XML comment. Reported in full - this document is the
    ///     place to read the whole value, so truncating here would leave it nowhere to be seen. Only
    ///     the two transforms XML comments require are applied: collapse to a single line (a
    ///     multi-line value would run past the <c>--&gt;</c> and corrupt every following line) and
    ///     neutralise <c>--</c>, which terminates a comment early.
    /// </summary>
    private static string Describe(string value)
    {
        var collapsed = string.Join(' ',
            value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Replace("--", "- -", StringComparison.Ordinal);
    }
}