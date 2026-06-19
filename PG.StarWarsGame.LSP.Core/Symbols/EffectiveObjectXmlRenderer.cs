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
            VariantProvenance.Overridden => "overrides base",
            VariantProvenance.Merged => "merged with base",
            VariantProvenance.Added => "added by variant",
            _ => null
        };
    }
}