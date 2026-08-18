namespace PIT.Boletas.Infrastructure.Utils;

public static class PipelinePathResolver
{
    public static string StagePath(string basePath, string stageFolder)
    {
        return Path.Combine(basePath, stageFolder);
    }

    public static string BuildNonCollidingFilePath(string directoryPath, string fileName)
    {
        string candidate = Path.Combine(directoryPath, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        string name = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        int index = 1;

        while (true)
        {
            string retryCandidate = Path.Combine(directoryPath, $"{name}_{index:00}{extension}");
            if (!File.Exists(retryCandidate))
            {
                return retryCandidate;
            }

            index++;
        }
    }
}
