using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Infrastructure.Services;

if (args.Length < 1)
{
    Console.WriteLine("Usage: dotnet run --project tools/OcrBenchmark -- <pdfPath>");
    return;
}

string pdfPath = Path.GetFullPath(args[0]);
if (!File.Exists(pdfPath))
{
    Console.WriteLine($"PDF not found: {pdfPath}");
    return;
}

string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string tessPath = Path.Combine(repoRoot, "src", "PIT.Boletas.Worker", "ocr", "tessdata");

var profiles = new[]
{
    new LocalOcrOptions
    {
        Enabled = true,
        Language = "spa",
        TessDataPath = tessPath,
        RenderWidth = 2200,
        RenderHeight = 3000,
        MinTextLength = 20,
        EngineMode = "Default",
        PageSegMode = "Auto",
        UserDefinedDpi = 300,
        EnableImagePreprocessing = false,
        ContrastBoost = 1.0,
        BinarizationThreshold = 160
    },
    new LocalOcrOptions
    {
        Enabled = true,
        Language = "spa",
        TessDataPath = tessPath,
        RenderWidth = 2480,
        RenderHeight = 3508,
        MinTextLength = 20,
        EngineMode = "LstmOnly",
        PageSegMode = "Auto",
        UserDefinedDpi = 300,
        EnableImagePreprocessing = true,
        ContrastBoost = 1.35,
        BinarizationThreshold = 160
    },
    new LocalOcrOptions
    {
        Enabled = true,
        Language = "spa",
        TessDataPath = tessPath,
        RenderWidth = 3000,
        RenderHeight = 4200,
        MinTextLength = 20,
        EngineMode = "LstmOnly",
        PageSegMode = "SingleBlock",
        UserDefinedDpi = 300,
        EnableImagePreprocessing = true,
        ContrastBoost = 1.5,
        BinarizationThreshold = 150
    }
};

string[] names = ["baseline", "precision_current", "aggressive"];

for (int i = 0; i < profiles.Length; i++)
{
    var service = new LocalTesseractOcrService(
        NullLogger<LocalTesseractOcrService>.Instance,
        Options.Create(profiles[i]));

    // Warm-up run to reduce JIT/first-load noise.
    await service.ExtractTextFromPdfAsync(pdfPath, CancellationToken.None);

    Process proc = Process.GetCurrentProcess();
    TimeSpan cpuBefore = proc.TotalProcessorTime;
    long memoryBefore = GC.GetTotalMemory(forceFullCollection: true);

    Stopwatch sw = Stopwatch.StartNew();
    string text = await service.ExtractTextFromPdfAsync(pdfPath, CancellationToken.None);
    sw.Stop();

    proc.Refresh();
    TimeSpan cpuAfter = proc.TotalProcessorTime;
    long memoryAfter = GC.GetTotalMemory(forceFullCollection: true);

    double elapsedMs = sw.Elapsed.TotalMilliseconds;
    double cpuMs = (cpuAfter - cpuBefore).TotalMilliseconds;
    double cpuPctOfOneCore = elapsedMs > 0 ? (cpuMs / elapsedMs) * 100.0 : 0;

    Console.WriteLine($"profile={names[i]}");
    Console.WriteLine($"elapsed_ms={elapsedMs:F0}");
    Console.WriteLine($"cpu_ms={cpuMs:F0}");
    Console.WriteLine($"cpu_pct_1core={cpuPctOfOneCore:F1}");
    Console.WriteLine($"text_chars={text.Length}");
    Console.WriteLine($"managed_mem_delta_kb={(memoryAfter - memoryBefore) / 1024.0:F1}");
    Console.WriteLine("---");
}
