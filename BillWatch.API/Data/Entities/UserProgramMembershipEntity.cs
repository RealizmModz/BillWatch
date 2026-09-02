namespace BillWatch.API.Data.Entities;

public enum UserProgramType
{
    BetaTester,
    InternalTester
}

public sealed class UserProgramMembershipEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid UserId { get; set; }

    public UserProgramType Program { get; set; } =
        UserProgramType.BetaTester;

    public DateTimeOffset StartsAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset? EndsAtUtc { get; set; }

    public bool IsActive { get; set; } =
        true;

    public Guid? GrantedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } =
        null!;

    public ApplicationUser? GrantedByUser { get; set; }
}
