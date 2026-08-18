namespace PIT.Boletas.Application.Models;

public enum FolderIssueSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record FolderValidationIssue(
    string Code,
    FolderIssueSeverity Severity,
    string Message,
    string FullPath);

public sealed class FolderValidationReport
{
    private readonly List<FolderValidationIssue> _issues = [];

    public IReadOnlyCollection<FolderValidationIssue> Issues => _issues;

    public bool HasCriticalIssues => _issues.Any(x => x.Severity == FolderIssueSeverity.Critical);

    public void Add(string code, FolderIssueSeverity severity, string message, string fullPath)
    {
        _issues.Add(new FolderValidationIssue(code, severity, message, fullPath));
    }
}
