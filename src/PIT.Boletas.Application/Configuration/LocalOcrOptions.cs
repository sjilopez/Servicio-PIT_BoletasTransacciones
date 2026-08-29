namespace PIT.Boletas.Application.Configuration;

public sealed class LocalOcrOptions
{
    public const string SectionName = "LocalOcr";

    public bool Enabled { get; set; } = true;

    public string Engine { get; set; } = "Tesseract";

    public string PaddleDetModelPath { get; set; } = "ocr/paddle/det";

    public string PaddleClsModelPath { get; set; } = "ocr/paddle/cls";

    public string PaddleRecModelPath { get; set; } = "ocr/paddle/rec";

    public string PaddleDictionaryPath { get; set; } = "ocr/paddle/ppocr_keys.txt";

    public int PaddleMaxSideLength { get; set; } = 1280;

    public double PaddleDetDbBoxThreshold { get; set; } = 0.5;

    public bool PaddleReadHeader { get; set; } = true;

    public int PaddleHeaderCropX { get; set; } = 0;

    public int PaddleHeaderCropY { get; set; } = 0;

    public double PaddleHeaderCropRatio { get; set; } = 0.28;

    public bool PaddleHeaderPreprocessing { get; set; } = true;

    public int PaddleHeaderScale { get; set; } = 2;

    public double PaddleHeaderContrast { get; set; } = 1.35;

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
