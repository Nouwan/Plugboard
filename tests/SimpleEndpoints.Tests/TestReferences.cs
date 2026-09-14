using Microsoft.CodeAnalysis.Testing;

namespace SimpleEndpoints.Tests;

internal static class TestReferences
{
    /// <summary>.NET 8 plus the ASP.NET Core shared framework, so test sources and generated code compile against the real types.</summary>
    public static readonly ReferenceAssemblies Net80AspNetCore = ReferenceAssemblies.Net.Net80
        .AddPackages([new PackageIdentity("Microsoft.AspNetCore.App.Ref", "8.0.0")]);
}
