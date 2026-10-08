// Internal copy of JetBrains' MeansImplicitUseAttribute and the enums it takes (https://github.com/JetBrains/JetBrains.Annotations).
// The JetBrains.Annotations NuGet package declares it [Conditional("JETBRAINS_ANNOTATIONS")], so the compiler drops it
// unless that symbol is defined. This copy has no [Conditional], so the attribute stays in the compiled assembly
// without adding a package dependency. Rider/ReSharper recognise it by full name.
// ReSharper disable All

#pragma warning disable S2344
#pragma warning disable IDE0130

namespace JetBrains.Annotations;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.GenericParameter | AttributeTargets.Parameter)]
internal sealed class MeansImplicitUseAttribute(ImplicitUseKindFlags useKindFlags, ImplicitUseTargetFlags targetFlags)
    : Attribute
{
    public ImplicitUseKindFlags UseKindFlags { get; } = useKindFlags;

    public ImplicitUseTargetFlags TargetFlags { get; } = targetFlags;
}

[Flags]
internal enum ImplicitUseKindFlags
{
    Default = Access | Assign | InstantiatedWithFixedConstructorSignature,
    Access = 1,
    Assign = 2,
    InstantiatedWithFixedConstructorSignature = 4,
    InstantiatedNoFixedConstructorSignature = 8,
}

[Flags]
internal enum ImplicitUseTargetFlags
{
    Itself = 1,
    Members = 2,
    WithInheritors = 4,
    WithMembers = Itself | Members,
}
