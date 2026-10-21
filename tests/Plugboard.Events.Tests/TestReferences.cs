using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Plugboard.Events.Tests;

internal static class TestReferences
{
    /// <summary>.NET 10 reference assemblies; the DI abstractions and Plugboard.Events come from the test project's own references.</summary>
    public static readonly ReferenceAssemblies Net100 = ReferenceAssemblies.Net.Net100;

    public static void AddPlugboardEvents(this SolutionState state)
    {
        state.AdditionalReferences.Add(typeof(EventAttribute).Assembly);
        state.AdditionalReferences.Add(typeof(IServiceCollection).Assembly);
    }
}
