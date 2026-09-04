namespace BillWatch.Web.Services;

public static class StatementUploadStatusSemantics
{
    public static bool IsTerminal(
        string? status)
    {
        return status is
            "Processed" or
            "Failed" or
            "NeedsOcr" or
            "ReadyForParsing";
    }

    public static bool IsProcessed(
        string? status)
    {
        return string.Equals(
            status,
            "Processed",
            StringComparison.Ordinal);
    }
}
