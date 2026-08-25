using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Infrastructure.Security;
using PIT.Boletas.Infrastructure.DependencyInjection;
using PIT.Boletas.Worker.HostedServices;

if (!OperatingSystem.IsWindows())
{
	throw new PlatformNotSupportedException("PIT_BoletasTransacciones requires Windows.");
}

if (args.Contains("--provision-credentials", StringComparer.OrdinalIgnoreCase))
{
	WindowsCredentialStore.ProvisionInteractive(Console.Out, Console.Error);
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
host.Run();
