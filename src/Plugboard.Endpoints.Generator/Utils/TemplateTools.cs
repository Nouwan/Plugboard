using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.Text;
using Scriban;

namespace Plugboard.Endpoints.Generator.Utils;

/// <summary>
/// Renders the embedded Scriban templates. Templates are located by name suffix, so the manifest resource name
/// may carry any RootNamespace/folder prefix. Parsed templates are cached per name.
/// </summary>
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
        Assembly assembly = typeof(TemplateTools).Assembly;
        var suffix = $".Templates.{templateName}.sbn-cs";

        string resourceName = assembly.GetManifestResourceNames()
                .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"No embedded resource ending in '{suffix}'. Ensure the template is included as an EmbeddedResource.");

        using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
