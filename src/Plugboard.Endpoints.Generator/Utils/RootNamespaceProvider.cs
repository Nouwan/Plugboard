using Microsoft.CodeAnalysis;

namespace Plugboard.Endpoints.Generator.Utils;

internal static class RootNamespaceProvider
{
    extension(IncrementalGeneratorInitializationContext context)
    {
        /// <summary>
        /// The consuming project's RootNamespace, compiler-visible by SDK default. No fallback: a missing value
        /// emits an empty namespace and fails the consuming build immediately.
        /// </summary>
        public IncrementalValueProvider<string> GetRootNamespace() =>
            context.AnalyzerConfigOptionsProvider
                .Select(static (options, _) =>
                    options.GlobalOptions.TryGetValue("build_property.RootNamespace", out string? value)
                        ? value
                        : throw new InvalidOperationException("Could not find root namespace"));
    }
}
