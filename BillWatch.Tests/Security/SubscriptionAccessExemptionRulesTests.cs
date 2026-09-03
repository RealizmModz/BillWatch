using BillWatch.API.Authorization;
using BillWatch.API.Controllers;

namespace BillWatch.Tests.Security;

public sealed class SubscriptionAccessExemptionRulesTests
{
    [Theory]
    [InlineData(typeof(SubscriptionController), "GetStatus")]
    [InlineData(typeof(SubscriptionController), "Redeem")]
    [InlineData(typeof(AdminSecurityController), "AnyAdminAction")]
    [InlineData(typeof(AdminSubscriptionController), "AnyAdminAction")]
    [InlineData(typeof(AdminUsersController), "AnyAdminAction")]
    [InlineData(typeof(AccountSecurityController), nameof(AccountSecurityController.Get))]
    [InlineData(typeof(AccountSecurityController), nameof(AccountSecurityController.ChangePassword))]
    [InlineData(typeof(AccountSecurityController), nameof(AccountSecurityController.ChangeEmail))]
    [InlineData(typeof(AccountSecurityController), nameof(AccountSecurityController.SetupTwoFactor))]
    [InlineData(typeof(AccountController), nameof(AccountController.ExportAccountData))]
    [InlineData(typeof(AccountController), nameof(AccountController.DeleteAccount))]
    [InlineData(typeof(BankConnectionsController), nameof(BankConnectionsController.Disconnect))]
    public void IsExempt_ReturnsTrueOnlyForRequiredEscapeHatches(
        Type controllerType,
        string actionName)
    {
        Assert.True(
            SubscriptionAccessExemptionRules.IsExempt(
                controllerType,
                actionName));
    }

    [Theory]
    [InlineData(typeof(AccountController), "FutureAccountAction")]
    [InlineData(typeof(BankConnectionsController), nameof(BankConnectionsController.GetConnections))]
    [InlineData(typeof(BankAccountsController), "AnyAction")]
    [InlineData(typeof(BankTransactionsController), "AnyAction")]
    [InlineData(typeof(BillStreamsController), "AnyAction")]
    public void IsExempt_ReturnsFalseForProtectedFinancialActions(
        Type controllerType,
        string actionName)
    {
        Assert.False(
            SubscriptionAccessExemptionRules.IsExempt(
                controllerType,
                actionName));
    }
}
