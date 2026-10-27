// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Renders an <see cref="EffectiveObject" /> as a read-only XML document, annotating each tag with an
///     XML comment describing its provenance (inherited / overridden / merged / added). Consumed by the
///     <c>aet/getEffectiveObject</c> request to back the effective-object virtual document.
/// </summary>
public static class EffectiveObjectXmlRenderer
{
    public static string Render(EffectiveObject obj, IMessageCatalog messages)
    {
        var element = obj.TypeName ?? "GameObject";
        var sb = new StringBuilder();

        sb.Append("<!-- ")
            .Append(obj.TypeName is null
                ? messages.Format("EffectiveObject_Header", obj.ObjectId)
                : messages.Format("EffectiveObject_HeaderTyped", obj.ObjectId, obj.TypeName))
            .AppendLine(" -->");

        if (obj.Chain.Length > 1)
            sb.Append("<!-- ").Append(messages.Format("EffectiveObject_Chain", string.Join(" -> ", obj.Chain)))
                .AppendLine(" -->");

        if (obj.Cyclic)
            sb.Append("<!-- ").Append(messages.Format("EffectiveObject_Cyclic", obj.CycleObjectId)).AppendLine(" -->");

        sb.Append('<').Append(element).Append(" Name=\"").Append(obj.ObjectId).AppendLine("\">");

        foreach (var tag in obj.Tags)
        {
            var note = ProvenanceNote(tag, messages);
            if (note is not null)
                sb.Append("    <!-- ").Append(note).AppendLine(" -->");
            foreach (var line in tag.Fragment.Split('\n'))
                sb.Append("    ").AppendLine(line.TrimEnd('\r'));
        }

        sb.Append("</").Append(element).AppendLine(">");
        return sb.ToString();
    }

    private static string? ProvenanceNote(EffectiveTag tag, IMessageCatalog messages)
    {
        return tag.Provenance switch
        {
            VariantProvenance.Inherited => messages.Format("EffectiveObject_InheritedFrom", tag.OriginObjectId),
            VariantProvenance.Overridden => tag.BaseValue is null
                ? messages.Format("EffectiveObject_OverridesBase")
                : messages.Format("EffectiveObject_OverridesBaseWas", Describe(tag.BaseValue)),
            VariantProvenance.Merged => tag.BaseValue is null
                ? messages.Format("EffectiveObject_MergedWithBase")
                : messages.Format("EffectiveObject_MergedWithBaseContributes", Describe(tag.BaseValue)),
            VariantProvenance.Added => messages.Format("EffectiveObject_AddedByVariant"),
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