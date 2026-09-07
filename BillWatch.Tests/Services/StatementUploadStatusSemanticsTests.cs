using BillWatch.Web.Services;

namespace BillWatch.Tests.Services;

public sealed class StatementUploadStatusSemanticsTests
{
    [Theory]
    [InlineData("Processed")]
    [InlineData("processed")]
    [InlineData("Failed")]
    [InlineData("failed")]
    [InlineData("NeedsOcr")]
    [InlineData("needsocr")]
    [InlineData("ReadyForParsing")]
    [InlineData("readyforparsing")]
    public void StableStatementStatuses_AreTerminal(
        string status)
    {
        Assert.True(
            StatementUploadStatusSemantics.IsTerminal(
                status));
    }

    [Theory]
    [InlineData("Uploaded")]
    [InlineData("Processing")]
    [InlineData("")]
    [InlineData(null)]
    public void ActiveOrUnknownStatuses_AreNotTerminal(
        string? status)
    {
        Assert.False(
            StatementUploadStatusSemantics.IsTerminal(
                status));
    }

    [Fact]
    public void OnlyProcessedStatus_RefreshesTrustedBillHistory()
    {
        Assert.True(
            StatementUploadStatusSemantics.IsProcessed(
                "Processed"));

        Assert.True(
            StatementUploadStatusSemantics.IsProcessed(
                "processed"));

        Assert.False(
            StatementUploadStatusSemantics.IsProcessed(
                "ReadyForParsing"));

        Assert.False(
            StatementUploadStatusSemantics.IsProcessed(
                "NeedsOcr"));

        Assert.False(
            StatementUploadStatusSemantics.IsProcessed(
                "Failed"));
    }
}
