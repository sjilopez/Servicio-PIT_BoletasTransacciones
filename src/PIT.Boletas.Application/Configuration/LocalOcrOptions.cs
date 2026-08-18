namespace PIT.Boletas.Application.Configuration;

public sealed class LocalOcrOptions
{
    public const string SectionName = "LocalOcr";

    public bool Enabled { get; set; } = true;

    public string Language { get; set; } = "spa";

    public string TessDataPath { get; set; } = "ocr/tessdata";

    public int RenderWidth { get; set; } = 2200;

    public int RenderHeight { get; set; } = 3000;

    public int MinTextLength { get; set; } = 20;
}
