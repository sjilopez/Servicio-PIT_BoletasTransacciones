namespace PIT.Boletas.Observability;

public static class ErrorCodes
{
    public const string FolderPermissionError = "FS001";
    public const string FileLockedOrIncomplete = "FS002";
    public const string OcrApiTimeout = "OCRE001";
    public const string MySqlConnectionFailure = "DB001";
    public const string ServiceStartupFailure = "SVC001";
    public const string ServiceRepeatedRestart = "SVC002";
}
