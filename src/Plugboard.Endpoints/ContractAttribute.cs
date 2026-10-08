using JetBrains.Annotations;

namespace Plugboard;

/// <summary>
/// Marks a type that is part of the contract to the outside world.
/// Members of a contract are treated as used by rider vs (code) and some .net analyzers.
/// </summary>
[MeansImplicitUse(ImplicitUseKindFlags.Default, ImplicitUseTargetFlags.WithMembers)]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, Inherited = false)]
public sealed class ContractAttribute : Attribute;
