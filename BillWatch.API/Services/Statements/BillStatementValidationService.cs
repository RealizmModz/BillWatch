namespace BillWatch.API.Services.Statements;

public sealed class BillStatementValidationService
{
    private const int MaximumExpectedPeriodDays =
        400;

    private const int MaximumFutureDateDays =
        60;

    public BillStatementValidationResult Validate(
        BillStatementValidationInput input,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        var issues =
            new List<BillStatementValidationIssue>();

        if (!input.TotalAmount.HasValue)
        {
            AddError(
                issues,
                "missing_total",
                "A statement total is required.");
        }
        else if (input.TotalAmount.Value <
                 0m)
        {
            AddError(
                issues,
                "negative_total",
                "The statement total cannot be negative.");
        }

        if (!input.PeriodStart.HasValue)
        {
            AddError(
                issues,
                "missing_period_start",
                "A billing-period start date is required.");
        }

        if (!input.PeriodEnd.HasValue)
        {
            AddError(
                issues,
                "missing_period_end",
                "A billing-period end date is required.");
        }

        if (input.PeriodStart.HasValue &&
            input.PeriodEnd.HasValue)
        {
            if (input.PeriodEnd.Value <
                input.PeriodStart.Value)
            {
                AddError(
                    issues,
                    "reversed_period",
                    "The billing period ends before it starts.");
            }
            else
            {
                var periodDays =
                    input.PeriodEnd.Value
                        .DayNumber -
                    input.PeriodStart.Value
                        .DayNumber +
                    1;

                if (periodDays >
                    MaximumExpectedPeriodDays)
                {
                    AddWarning(
                        issues,
                        "unusually_long_period",
                        "The billing period is unusually long.");
                }
            }
        }

        ValidateCurrency(
            input.CurrencyCode,
            issues);

        if (input.StatementDate.HasValue &&
            input.DueDate.HasValue &&
            input.DueDate.Value <
            input.StatementDate.Value)
        {
            AddWarning(
                issues,
                "due_before_statement",
                "The due date is earlier than the statement date.");
        }

        var latestExpectedDate =
            today.AddDays(
                MaximumFutureDateDays);

        CheckFutureDate(
            input.PeriodStart,
            latestExpectedDate,
            "period_start_future",
            issues);

        CheckFutureDate(
            input.PeriodEnd,
            latestExpectedDate,
            "period_end_future",
            issues);

        CheckFutureDate(
            input.StatementDate,
            latestExpectedDate,
            "statement_date_future",
            issues);

        CheckFutureDate(
            input.DueDate,
            latestExpectedDate,
            "due_date_future",
            issues);

        return new BillStatementValidationResult(
            issues);
    }

    private static void ValidateCurrency(
        string? currencyCode,
        ICollection<BillStatementValidationIssue>
            issues)
    {
        if (string.IsNullOrWhiteSpace(
                currencyCode))
        {
            AddError(
                issues,
                "missing_currency",
                "A currency code is required.");

            return;
        }

        if (currencyCode.Length !=
                3 ||
            currencyCode.Any(
                character =>
                    character is <
                        'A' or >
                        'Z'))
        {
            AddError(
                issues,
                "invalid_currency",
                "The currency code must contain exactly three uppercase letters.");
        }
    }

    private static void CheckFutureDate(
        DateOnly? value,
        DateOnly latestExpectedDate,
        string code,
        ICollection<BillStatementValidationIssue>
            issues)
    {
        if (!value.HasValue ||
            value.Value <=
            latestExpectedDate)
        {
            return;
        }

        AddWarning(
            issues,
            code,
            "The extracted date is unusually far in the future.");
    }

    private static void AddError(
        ICollection<BillStatementValidationIssue>
            issues,
        string code,
        string message)
    {
        issues.Add(
            new BillStatementValidationIssue(
                code,
                message,
                BillStatementValidationSeverity.Error));
    }

    private static void AddWarning(
        ICollection<BillStatementValidationIssue>
            issues,
        string code,
        string message)
    {
        issues.Add(
            new BillStatementValidationIssue(
                code,
                message,
                BillStatementValidationSeverity.Warning));
    }
}

public sealed record BillStatementValidationInput(
    decimal? TotalAmount,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    DateOnly? StatementDate,
    DateOnly? DueDate,
    string? CurrencyCode);

public sealed class BillStatementValidationResult
{
    public BillStatementValidationResult(
        IReadOnlyCollection<
            BillStatementValidationIssue> issues)
    {
        Issues =
            issues.ToArray();
    }

    public IReadOnlyList<
        BillStatementValidationIssue> Issues
    {
        get;
    }

    public bool IsValid =>
        Issues.All(
            issue =>
                issue.Severity !=
                BillStatementValidationSeverity.Error);

    public bool RequiresReview =>
        Issues.Any(
            issue =>
                issue.Severity ==
                BillStatementValidationSeverity.Warning);
}

public sealed record BillStatementValidationIssue(
    string Code,
    string Message,
    BillStatementValidationSeverity Severity);

public enum BillStatementValidationSeverity
{
    Warning = 0,
    Error = 1
}