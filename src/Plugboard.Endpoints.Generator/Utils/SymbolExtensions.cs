using Microsoft.CodeAnalysis;

namespace Plugboard.Endpoints.Generator.Utils;

internal static class SymbolExtensions
{
    extension(ISymbol symbol)
    {
        public Location Location => symbol.Locations[0];

        public AttributeData? FindAttribute(INamedTypeSymbol attributeType) => symbol.GetAttributes()
            .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));
    }

    extension(INamedTypeSymbol type)
    {
        public string FullyQualifiedName => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    extension(IMethodSymbol method)
    {
        public string FullyQualifiedName => $"{method.ContainingType.FullyQualifiedName}.{method.Name}";
    }
}