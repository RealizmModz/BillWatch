namespace BillWatch.Core.Models;

public sealed record BillStatement
{
    public string ProviderName { get; }

    public DateOnly BillingPeriodStart { get; }

    public DateOnly BillingPeriodEnd { get; }

    public BillAmount TotalAmount { get; }

    public IReadOnlyList<BillLineItem> LineItems { get; }

    public BillStatement(
        string providerName,
        DateOnly billingPeriodStart,
        DateOnly billingPeriodEnd,
        BillAmount totalAmount,
        IEnumerable<BillLineItem> lineItems)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException(
                "A bill statement must have a provider name.",
                nameof(providerName));
        }

        if (billingPeriodEnd < billingPeriodStart)
        {
            throw new ArgumentException(
                "Billing period end cannot be before the start.");
        }

        ArgumentNullException.ThrowIfNull(totalAmount);
        ArgumentNullException.ThrowIfNull(lineItems);

        ProviderName = providerName.Trim();
        BillingPeriodStart = billingPeriodStart;
        BillingPeriodEnd = billingPeriodEnd;
        TotalAmount = totalAmount;
        LineItems = lineItems.ToList().AsReadOnly();
    }
}