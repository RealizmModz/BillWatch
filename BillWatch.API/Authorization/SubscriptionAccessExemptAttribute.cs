namespace BillWatch.API.Authorization;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = true)]
public sealed class SubscriptionAccessExemptAttribute
    : Attribute;
