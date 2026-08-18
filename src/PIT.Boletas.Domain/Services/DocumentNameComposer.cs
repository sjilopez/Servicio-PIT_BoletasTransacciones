namespace PIT.Boletas.Domain.Services;

public static class DocumentNameComposer
{
    public static string ResolveAgency(string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return "UNKN";
        }

        string normalized = hostName.Trim().ToUpperInvariant();

        if (normalized.StartsWith("SJAGM", StringComparison.Ordinal))
        {
            return "SJAGM";
        }

        return normalized.Length >= 4 ? normalized[..4] : normalized.PadRight(4, 'X');
    }

    public static string BuildBaseFileName(string agency, string user, DateTime creationTime)
    {
        return $"{agency}_{user}_{creationTime:yyyyMMdd}_{creationTime:HHmmss}_{creationTime:fff}";
    }
}
