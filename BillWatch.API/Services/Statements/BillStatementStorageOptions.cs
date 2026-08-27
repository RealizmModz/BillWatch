namespace BillWatch.API.Services.Statements;

public sealed class BillStatementStorageOptions
{
    public const string SectionName =
        "BillStatementStorage";

    public string RootPath { get; set; } =
        string.Empty;

    public long MaxFileSizeBytes { get; set; } =
        15 * 1024 * 1024;
}