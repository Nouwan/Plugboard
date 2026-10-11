using Microsoft.CodeAnalysis.Testing;

namespace Plugboard.Endpoints.Tests;

internal static class TestReferences
{
    /// <summary>.NET 10 plus the ASP.NET Core shared framework, so test sources and generated code compile against the real types.</summary>
    public static readonly ReferenceAssemblies Net100AspNetCore = ReferenceAssemblies.Net.Net100
        .AddPackages([new PackageIdentity("Microsoft.AspNetCore.App.Ref", "10.0.0")]);
}
