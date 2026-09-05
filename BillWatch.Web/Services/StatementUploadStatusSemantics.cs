namespace BillWatch.Web.Services;

public static class StatementUploadStatusSemantics
{
    public static bool IsTerminal(
        string? status)
    {
        return IsStatus(status, "Processed") ||
               IsStatus(status, "Failed") ||
               IsStatus(status, "NeedsOcr") ||
               IsStatus(status, "ReadyForParsing");
    }

    public static bool IsProcessed(
        string? status)
    {
        return IsStatus(
            status,
            "Processed");
    }

    private static bool IsStatus(
        string? actual,
        string expected)
    {
        return string.Equals(
            actual,
            expected,
            StringComparison.OrdinalIgnoreCase);
    }
}
