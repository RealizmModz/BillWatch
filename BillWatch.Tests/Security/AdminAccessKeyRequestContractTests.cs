using System.Text.Json;
using BillWatch.Web.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class AdminAccessKeyRequestContractTests
{
    [Fact]
    public void LifetimeLabel_IsIncludedInForwardedJsonContract()
    {
        var request = new AdminCreateAccessKeyRequest(
            "Complimentary",
            "Standard",
            null,
            true,
            1,
            null,
            "Founding beta tester");

        var json = JsonSerializer.Serialize(
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            "Founding beta tester",
            document.RootElement.GetProperty("label").GetString());
    }
}
