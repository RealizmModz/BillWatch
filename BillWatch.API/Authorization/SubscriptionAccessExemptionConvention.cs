using BillWatch.API.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace BillWatch.API.Authorization;

public sealed class SubscriptionAccessExemptionConvention
    : IActionModelConvention
{
    public void Apply(ActionModel action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!SubscriptionAccessExemptionRules.IsExempt(
                action.Controller.ControllerType.AsType(),
                action.ActionMethod.Name))
        {
            return;
        }

        foreach (var selector in action.Selectors)
        {
            if (selector.EndpointMetadata.Any(
                    metadata =>
                        metadata is SubscriptionAccessExemptAttribute))
            {
                continue;
            }

            selector.EndpointMetadata.Add(
                new SubscriptionAccessExemptAttribute());
        }
    }
}

public static class SubscriptionAccessExemptionRules
{
    public static bool IsExempt(
        Type controllerType,
        string actionName)
    {
        ArgumentNullException.ThrowIfNull(controllerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);

        if (controllerType == typeof(SubscriptionController) ||
            controllerType == typeof(AdminSecurityController) ||
            controllerType == typeof(AdminSubscriptionController) ||
            controllerType == typeof(AdminUsersController))
        {
            return true;
        }

        if (controllerType == typeof(AccountController))
        {
            return actionName is
                nameof(AccountController.ExportAccountData) or
                nameof(AccountController.DeleteAccount);
        }

        if (controllerType == typeof(BankConnectionsController))
        {
            return actionName ==
                nameof(BankConnectionsController.Disconnect);
        }

        return false;
    }
}
