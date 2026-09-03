namespace BillWatch.API.Services.Identity;

public sealed class IdentityEmailOptions
{
    public const string SectionName =
        "IdentityEmail";

    public bool Enabled { get; set; }

    public string ApiKey
    {
        get;
        set;
    } = string.Empty;

    public string FromAddress
    {
        get;
        set;
    } = "security@billbeacon.net";

    public string FromName
    {
        get;
        set;
    } = "BillWatch";

    public string PublicWebBaseUrl
    {
        get;
        set;
    } = "https://billbeacon.net";
}
