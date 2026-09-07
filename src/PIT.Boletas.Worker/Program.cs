using System.Text.Json;
using FuzzySharp;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Application.Abstractions;
using PIT.Boletas.Application.Models;
using PIT.Boletas.Infrastructure.Security;
using PIT.Boletas.Infrastructure.DependencyInjection;
using PIT.Boletas.Worker.HostedServices;
using PIT.Boletas.Infrastructure.Services;

if (!OperatingSystem.IsWindows())
{
	throw new PlatformNotSupportedException("PIT_BoletasTransacciones requires Windows.");
}

if (args.Contains("--provision-credentials", StringComparer.OrdinalIgnoreCase))
{
	WindowsCredentialStore.ProvisionInteractive(Console.Out, Console.Error);
	return;
}

if (TryGetArgumentValue(args, "--create-provisioning-file", out string? createPath))
{
	WindowsCredentialStore.CreateEncryptedProvisioningFile(createPath!);
	return;
}

if (TryGetArgumentValue(args, "--provision-encrypted", out string? encryptedPath))
{
	WindowsCredentialStore.ProvisionEncryptedFile(encryptedPath!);
	return;
}

if (args.Contains("--check-credentials", StringComparer.OrdinalIgnoreCase))
{
	IReadOnlyCollection<string> configuredKeys = WindowsCredentialStore.GetConfiguredKeys();
	Console.WriteLine($"Credenciales configuradas: {configuredKeys.Count}");
	foreach (string key in configuredKeys)
	{
		Console.WriteLine($"OK {key}");
	}

	return;
}

WindowsCredentialStore.MigrateLegacySettings(
	@"C:\Scans\Tools\settings.local.json",
	@"C:\ProgramData\PIT-BoletasTransaccionales\Config\appsettings.local.json");

var builder = Host.CreateApplicationBuilder(args);
bool isOcrCheck = args.Contains("--check-ocr", StringComparer.OrdinalIgnoreCase);
bool isValidationCheck = args.Contains("--check-validation", StringComparer.OrdinalIgnoreCase);

builder.Configuration
	.AddJsonFile(@"C:\ProgramData\PIT-BoletasTransaccionales\Config\appsettings.local.json", optional: true, reloadOnChange: true);

if (!isOcrCheck && !isValidationCheck)
{
	builder.Configuration.AddInMemoryCollection(
		WindowsCredentialStore.LoadConfigurationOverrides()
			.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));
}

builder.Services.Configure<PipelineFoldersOptions>(builder.Configuration.GetSection(PipelineFoldersOptions.SectionName));
builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection(IngestionOptions.SectionName));
builder.Services.Configure<LocalOcrOptions>(builder.Configuration.GetSection(LocalOcrOptions.SectionName));
builder.Services.Configure<AlertingOptions>(builder.Configuration.GetSection(AlertingOptions.SectionName));
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection(MonitoringOptions.SectionName));
builder.Services.Configure<CompressionOptions>(builder.Configuration.GetSection(CompressionOptions.SectionName));
builder.Services.AddWindowsService(options =>
{
	options.ServiceName = "PIT_BoletasTransacciones_v2.00";
});

builder.Services.AddFolderBootstrapServices();
builder.Services.AddHostedService<FolderBootstrapHostedService>();
builder.Services.AddHostedService<PipelineOrchestratorWorker>();
builder.Services.AddHostedService<ServiceHeartbeatHostedService>();

var host = builder.Build();

if (args.Contains("--check-active-user", StringComparer.OrdinalIgnoreCase))
{
	Console.WriteLine($"Usuario activo detectado: {StageOneIngestionService.ResolveInteractiveUser()}");
	return;
}

if (TryGetArgumentValue(args, "--check-ocr", out string? ocrPath))
{
	if (!File.Exists(ocrPath))
	{
		throw new FileNotFoundException("No se encontro el PDF para comprobar OCR.", ocrPath);
	}

	ILocalOcrService ocrService = host.Services.GetRequiredService<ILocalOcrService>();
	string text = await ocrService.ExtractTextFromPdfAsync(ocrPath, CancellationToken.None);
	Console.WriteLine($"OCR OK. Caracteres: {text.Length}");
	if (args.Contains("--show-ocr-text", StringComparer.OrdinalIgnoreCase))
	{
		Console.WriteLine("----- INICIO TEXTO OCR -----");
		Console.WriteLine(text);
		Console.WriteLine("----- FIN TEXTO OCR -----");
	}

	return;
}

if (TryGetArgumentValue(args, "--check-validation", out string? validationPath))
{
	if (!File.Exists(validationPath))
	{
		throw new FileNotFoundException("No se encontro el PDF para comprobar validacion.", validationPath);
	}

	ILocalOcrService ocrService = host.Services.GetRequiredService<ILocalOcrService>();
	int pageCount = await ocrService.GetPageCountAsync(validationPath, CancellationToken.None);
	string text = await ocrService.ExtractTextFromPdfAsync(validationPath, CancellationToken.None);
	IConfiguration validationConfiguration = host.Services.GetRequiredService<IConfiguration>();
	int fuzzyMatch = Math.Clamp(validationConfiguration.GetValue("Validation:FuzzyMatch", 85), 1, 100);
	int headerFuzzyMatch = Math.Clamp(validationConfiguration.GetValue("Validation:HeaderFuzzyMatch", 72), 1, 100);
	int minimumMatches = Math.Max(1, validationConfiguration.GetValue("Validation:MinimumMatches", 3));
	string requiredHeader = validationConfiguration.GetValue<string>("Validation:RequiredHeader") ?? "BOLETA DE TRANSACCIONES";
	string configPath = validationConfiguration.GetValue<string>("PipelineFolders:ProgramDataConfigPath")
	                    ?? @"C:\ProgramData\PIT-BoletasTransaccionales\Config";
	string templatePath = Path.Combine(configPath, "PlantillasDocumentales.json");
	TemplateSettings templates = LoadTemplateSettings(templatePath);
	DocumentTemplate template = templates.Templates.FirstOrDefault()
	                            ?? new DocumentTemplate { Name = "BoletaTransaccional" };
	string normalizedText = Normalize(validationText: text);
	string normalizedHeader = Normalize(requiredHeader);
	int headerScore = Fuzz.PartialRatio(normalizedHeader, normalizedText);
	bool headerMatched = normalizedText.Contains(normalizedHeader, StringComparison.OrdinalIgnoreCase)
	                    || headerScore >= headerFuzzyMatch;
	List<string> indicators = [.. template.Phrases, .. template.Keywords];

	int matched = 0;
	foreach (string indicator in indicators)
	{
		int score = Fuzz.PartialRatio(Normalize(indicator), normalizedText);
		bool indicatorMatched = score >= fuzzyMatch;
		if (indicatorMatched)
		{
			matched++;
		}

	}

	bool isValid = headerMatched && (indicators.Count == 0 || matched >= minimumMatches);
	bool minimumMatchesReached = indicators.Count == 0 || matched >= minimumMatches;
	Console.WriteLine();
	Console.WriteLine("===== RESUMEN DE MATCHES DE LA PLANTILLA =====");
	Console.WriteLine($"Hojas del archivo: {pageCount}");
	Console.WriteLine($"Indicadores de la plantilla: {indicators.Count}");
	Console.WriteLine($"Matches encontrados: {matched}");
	Console.WriteLine($"Matches minimos requeridos: {minimumMatches}");
	Console.WriteLine($"Cumple minimo de matches: {(minimumMatchesReached ? "SI" : "NO")}");
	Console.WriteLine($"Resultado completo Stage 2: {(isValid ? "VALIDO -> 4_OCR_EXTERNO" : "NO VALIDO -> 7_COPY_AZURE_FILE")}");
	return;
}

if (args.Contains("--check-external-ocr", StringComparer.OrdinalIgnoreCase))
{
	string? externalOcrPath = TryGetArgumentValue(args, "--check-external-ocr", out string? requestedPath)
		? requestedPath
		: FindLatestPdf(@"C:\Scans");

	if (string.IsNullOrWhiteSpace(externalOcrPath) || !File.Exists(externalOcrPath))
	{
		throw new FileNotFoundException("No se encontro ningun PDF para comprobar el OCR externo.", externalOcrPath);
	}

	StageThreeOcrService ocrService = host.Services.GetRequiredService<StageThreeOcrService>();
	(bool success, int? statusCode, string error, string payload) = await ocrService.CheckExternalOcrAsync(externalOcrPath, CancellationToken.None);
	Console.WriteLine($"PDF probado: {externalOcrPath}");
	Console.WriteLine($"OCR externo: {(success ? "OK" : "FALLO")}; HTTP {(statusCode?.ToString() ?? "sin respuesta")}");
	if (!string.IsNullOrWhiteSpace(error))
	{
		Console.WriteLine($"Detalle: {error}");
	}
	else
	{
		Console.WriteLine($"Respuesta: {payload.Length} caracteres");
	}

	return;
}

host.Run();

static bool TryGetArgumentValue(string[] args, string argumentName, out string? value)
{
	int index = Array.FindIndex(args, argument => string.Equals(argument, argumentName, StringComparison.OrdinalIgnoreCase));
	if (index >= 0 && index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1]))
	{
		value = args[index + 1];
		return true;
	}

	value = null;
	return false;
}

static string? FindLatestPdf(string rootPath)
{
	if (!Directory.Exists(rootPath))
	{
		return null;
	}

	return Directory.GetFiles(rootPath, "*.pdf", SearchOption.AllDirectories)
		.OrderByDescending(File.GetLastWriteTimeUtc)
		.FirstOrDefault();
}

static TemplateSettings LoadTemplateSettings(string path)
{
	if (!File.Exists(path))
	{
		return new TemplateSettings();
	}

	try
	{
		return JsonSerializer.Deserialize<TemplateSettings>(File.ReadAllText(path), new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		}) ?? new TemplateSettings();
	}
	catch
	{
		return new TemplateSettings();
	}
}

static string Normalize(string validationText)
{
	return validationText
		.ToUpperInvariant()
		.Replace("\r", " ")
		.Replace("\n", " ")
		.Replace("  ", " ")
		.Trim();
}
