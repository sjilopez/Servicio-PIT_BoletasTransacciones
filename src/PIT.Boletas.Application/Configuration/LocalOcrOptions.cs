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

    public string EngineMode { get; set; } = "LstmOnly";

    public string PageSegMode { get; set; } = "Auto";

    public int UserDefinedDpi { get; set; } = 300;

    public bool EnableImagePreprocessing { get; set; } = true;

    public double ContrastBoost { get; set; } = 1.35;

    public int BinarizationThreshold { get; set; } = 160;
}
