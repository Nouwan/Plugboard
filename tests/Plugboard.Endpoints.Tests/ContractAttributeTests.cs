using System.Reflection;
using NUnit.Framework;

namespace Plugboard.Endpoints.Tests;

[TestFixture]
internal sealed class ContractAttributeTests
{
    /// <summary>
    /// Rider only treats contract members as used when the annotation survives into the compiled assembly;
    /// the NuGet JetBrains.Annotations attributes are [Conditional] and would be silently stripped.
    /// </summary>
    [Test]
    public void ContractAttribute_ShouldCarryMeansImplicitUseWithMembers()
    {
        CustomAttributeData? annotation = typeof(ContractAttribute).GetCustomAttributesData()
            .SingleOrDefault(static data => data.AttributeType.FullName == "JetBrains.Annotations.MeansImplicitUseAttribute");

        Assert.That(annotation, Is.Not.Null);
        Assert.That(annotation!.ConstructorArguments.Select(static argument => Convert.ToInt32(argument.Value, null)),
            Is.EqualTo((int[])[7, 3])); // ImplicitUseKindFlags.Default, ImplicitUseTargetFlags.WithMembers
    }
}
