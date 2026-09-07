namespace BillWatch.API.Services.Plaid;

public static class PlaidConnectionAttentionClassifier
{
    private const string ItemErrorType =
        "ITEM_ERROR";

    private static readonly IReadOnlySet<string> UserActionRequiredCodes =
        new HashSet<string>(
            [
                "ITEM_LOGIN_REQUIRED",
                "ACCESS_NOT_GRANTED",
                "ITEM_LOCKED",
                "PASSWORD_RESET_REQUIRED",
                "USER_SETUP_REQUIRED"
            ],
            StringComparer.OrdinalIgnoreCase);

    public static bool RequiresUserAttention(
        PlaidApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return string.Equals(
                   exception.ErrorType,
                   ItemErrorType,
                   StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(exception.ErrorCode) &&
               UserActionRequiredCodes.Contains(exception.ErrorCode);
    }
}
