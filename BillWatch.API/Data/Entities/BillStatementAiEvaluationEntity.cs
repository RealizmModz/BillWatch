namespace BillWatch.API.Data.Entities;

public enum BillStatementAiEvaluationStatus
{
    Pending = 0,
    InProgress = 1,
    AcceptedForShadowReview = 2,
    Rejected = 3,
    ProviderFailed = 4,
    SkippedDeterministicComplete = 5,
    Canceled = 6
}

/*
 * Durable AI attempt metadata only.
 *
 * Never add raw statement text, prompts, model responses, extracted account
 * data, evidence excerpts, or provider error bodies to this entity.
 */
public sealed class BillStatementAiEvaluationEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid BillStatementUploadId { get; set; }

    public string Provider { get; set; } =
        string.Empty;

    public string Model { get; set; } =
        string.Empty;

    public string PromptVersion { get; set; } =
        string.Empty;

    public BillStatementAiEvaluationStatus Status { get; set; } =
        BillStatementAiEvaluationStatus.Pending;

    public int AttemptCount { get; set; }

    public bool CandidateReadyForValidation { get; set; }

    public DateTimeOffset? LastAttemptedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } =
        null!;

    public BillStatementUploadEntity BillStatementUpload { get; set; } =
        null!;
}
