using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PIT.Boletas.Application.Configuration;
using PIT.Boletas.Application.Models;
using PIT.Boletas.Infrastructure.Services;

namespace PIT.Boletas.UnitTests;

public sealed class StartupFolderGuardTests
{
    [Fact]
    public async Task ValidateAndEnsureAsync_CreatesMissingStageDirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), "pit-bootstrap-" + Guid.NewGuid().ToString("N"));

        try
        {
            PipelineFoldersOptions options = new()
            {
                BasePath = root,
                ProgramDataConfigPath = Path.Combine(root, "ProgramData")
            };

            LocalOcrOptions localOcrOptions = new()
            {
                Enabled = false
            };

            StartupFolderGuard guard = new(
                Options.Create(options),
                Options.Create(localOcrOptions),
                NullLogger<StartupFolderGuard>.Instance);

            FolderValidationReport report = await guard.ValidateAndEnsureAsync(CancellationToken.None);

            Assert.False(report.HasCriticalIssues);

            foreach (string folder in options.StageFolders)
            {
                Assert.True(Directory.Exists(Path.Combine(root, folder)));
            }

            Assert.True(File.Exists(Path.Combine(options.ProgramDataConfigPath, options.LocalSettingsFileName)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
