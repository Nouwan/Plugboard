using System.Collections.Concurrent;
using System.Text;
using Microsoft.CodeAnalysis.Text;
using Scriban;

namespace Plugboard.Endpoints.Generator.Utils;

internal static class TemplateTools
{
    private static readonly ConcurrentDictionary<string, Template> TemplateCache = new(StringComparer.Ordinal);

    internal static SourceText RenderTemplate(string templateName, object model)
    {
        Template template = TemplateCache.GetOrAdd(templateName, Load);
        return SourceText.From(template.Render(model), Encoding.UTF8);
    }

    private static Template Load(string templateName)
    {
        var template = Template.Parse(ReadEmbeddedTemplate(templateName));

        if (template.HasErrors)
        {
            throw new InvalidOperationException(
                $"Scriban template '{templateName}' has parse errors: {string.Join("; ", template.Messages)}");
        }

        return template;
    }

    private static string ReadEmbeddedTemplate(string templateName)
    {
        var resourceName = $"{templateName}.sbn-cs";

        using Stream stream = typeof(TemplateTools).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"No embedded resource '{resourceName}'. Ensure the template is included as an EmbeddedResource with that LogicalName.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
