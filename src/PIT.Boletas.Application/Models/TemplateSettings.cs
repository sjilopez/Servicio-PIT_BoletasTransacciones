namespace PIT.Boletas.Application.Models;

public sealed class TemplateSettings
{
    public List<DocumentTemplate> Templates { get; set; } = [];
}

public sealed class DocumentTemplate
{
    public string Name { get; set; } = string.Empty;

    public List<string> Phrases { get; set; } = [];
}
