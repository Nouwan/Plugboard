using Microsoft.CodeAnalysis;

namespace Plugboard.Generator.Utils;

internal static class ITypeSymbolExtensions
{
    extension(INamedTypeSymbol type)
    {
        public Location Location => type.Locations[0];

        public string FullyQualifiedName => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
