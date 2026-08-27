using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Application.Abstractions;
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

builder.Configuration
	.AddJsonFile(@"C:\ProgramData\PIT-BoletasTransaccionales\Config\appsettings.local.json", optional: true, reloadOnChange: true);

builder.Configuration.AddInMemoryCollection(
	WindowsCredentialStore.LoadConfigurationOverrides()
		.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));

builder.Services.Configure<PipelineFoldersOptions>(builder.Configuration.GetSection(PipelineFoldersOptions.SectionName));
builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection(IngestionOptions.SectionName));
builder.Services.Configure<LocalOcrOptions>(builder.Configuration.GetSection(LocalOcrOptions.SectionName));
builder.Services.Configure<AlertingOptions>(builder.Configuration.GetSection(AlertingOptions.SectionName));
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection(MonitoringOptions.SectionName));
builder.Services.Configure<CompressionOptions>(builder.Configuration.GetSection(CompressionOptions.SectionName));
builder.Services.AddWindowsService(options =>
{
	options.ServiceName = "PIT_BoletasTransacciones";
});

builder.Services.AddFolderBootstrapServices();
builder.Services.AddHostedService<FolderBootstrapHostedService>();
builder.Services.AddHostedService<PipelineOrchestratorWorker>();
builder.Services.AddHostedService<ServiceHeartbeatHostedService>();

var host = builder.Build();

if (TryGetArgumentValue(args, "--check-ocr", out string? ocrPath))
{
	if (!File.Exists(ocrPath))
	{
		throw new FileNotFoundException("No se encontro el PDF para comprobar OCR.", ocrPath);
	}

	ILocalOcrService ocrService = host.Services.GetRequiredService<ILocalOcrService>();
	string text = await ocrService.ExtractTextFromPdfAsync(ocrPath, CancellationToken.None);
	Console.WriteLine($"OCR OK. Caracteres: {text.Length}");
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
