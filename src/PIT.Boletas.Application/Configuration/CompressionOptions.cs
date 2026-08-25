namespace PIT.Boletas.Application.Configuration;

public sealed class CompressionOptions
{
    public const string SectionName = "Compression";

    public bool Enabled { get; set; } = true;

    public int TargetDpi { get; set; } = 150;

    public bool Grayscale { get; set; } = false;

    public int JpegQuality { get; set; } = 70;
}
