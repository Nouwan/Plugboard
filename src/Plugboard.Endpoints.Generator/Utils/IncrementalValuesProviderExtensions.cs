using Microsoft.CodeAnalysis;

namespace Plugboard.Endpoints.Generator.Utils;

internal static class IncrementalValuesProviderExtensions
{
    extension<T>(IncrementalValuesProvider<T?> source) where T : class
    {
        public IncrementalValuesProvider<T> WhereNotNull() =>
            source.Where(static item => item is not null).Select(static (item, _) => item!);
    }
}
