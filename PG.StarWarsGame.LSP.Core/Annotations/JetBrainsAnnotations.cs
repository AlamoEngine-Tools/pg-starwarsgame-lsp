// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The three JetBrains code annotations this solution uses, in the namespace the inspector looks them
// up by. Carried as source rather than as a package: they are compile-time hints with no runtime
// behaviour, and [Conditional] keeps them out of the built assemblies. A type that crosses the
// wire - a protocol record the client reads, a YAML or JSON shape a deserializer fills - declares
// so with [PublicAPI], which is what tells the inspector its members are read on the other side.

using System.Diagnostics;

// The inspector reads these attributes' properties and names; nothing in the solution does, and the
// names are the ones it recognises, so neither is ours to change.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable InconsistentNaming
// ReSharper disable once CheckNamespace - the inspector recognises these attributes by this name only.
namespace JetBrains.Annotations;

/// <summary>
///     The members of the marked type are read by code outside this solution: the VS Code client, a
///     serializer, a test rig. The inspector then stops reporting them as unused.
/// </summary>
[MeansImplicitUse(ImplicitUseTargetFlags.WithMembers)]
[AttributeUsage(AttributeTargets.All, Inherited = false)]
[Conditional("JETBRAINS_ANNOTATIONS")]
public sealed class PublicAPIAttribute : Attribute
{
    public PublicAPIAttribute()
    {
    }

    public PublicAPIAttribute(string comment)
    {
        Comment = comment;
    }

    public string? Comment { get; }
}

/// <summary>Marks an attribute whose presence means the decorated symbol is used implicitly.</summary>
[AttributeUsage(AttributeTargets.Class)]
[Conditional("JETBRAINS_ANNOTATIONS")]
public sealed class MeansImplicitUseAttribute : Attribute
{
    public MeansImplicitUseAttribute()
        : this(ImplicitUseKindFlags.Default, ImplicitUseTargetFlags.Default)
    {
    }

    public MeansImplicitUseAttribute(ImplicitUseKindFlags useKindFlags)
        : this(useKindFlags, ImplicitUseTargetFlags.Default)
    {
    }

    public MeansImplicitUseAttribute(ImplicitUseTargetFlags targetFlags)
        : this(ImplicitUseKindFlags.Default, targetFlags)
    {
    }

    public MeansImplicitUseAttribute(ImplicitUseKindFlags useKindFlags, ImplicitUseTargetFlags targetFlags)
    {
        UseKindFlags = useKindFlags;
        TargetFlags = targetFlags;
    }

    public ImplicitUseKindFlags UseKindFlags { get; }

    public ImplicitUseTargetFlags TargetFlags { get; }
}

[Flags]
public enum ImplicitUseKindFlags
{
    Default = Access | Assign | InstantiatedWithFixedConstructorSignature,
    Access = 1,
    Assign = 2,
    InstantiatedWithFixedConstructorSignature = 4,
    InstantiatedNoFixedConstructorSignature = 8
}

[Flags]
public enum ImplicitUseTargetFlags
{
    Default = Itself,
    Itself = 1,
    Members = 2,
    WithInheritors = 4,
    WithMembers = Itself | Members
}
