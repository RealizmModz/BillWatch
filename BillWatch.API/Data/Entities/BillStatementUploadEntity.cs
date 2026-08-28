namespace BillWatch.API.Data.Entities;

public enum BillStatementUploadStatus
{
    Uploaded = 0,
    Processing = 1,
    Processed = 2,
    Failed = 3,
    NeedsOcr = 4,
    ReadyForParsing = 5
}

public sealed class BillStatementUploadEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid BillStreamId { get; set; }

    public Guid? BillStatementId { get; set; }

    public string StorageKey { get; set; } =
        string.Empty;

    public string MediaType { get; set; } =
        string.Empty;

    public string FileExtension { get; set; } =
        string.Empty;

    public long SizeBytes { get; set; }

    public BillStatementUploadStatus Status { get; set; } =
        BillStatementUploadStatus.Uploaded;

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } =
        null!;

    public BillStreamEntity BillStream { get; set; } =
        null!;

    public BillStatementEntity? BillStatement { get; set; }
}