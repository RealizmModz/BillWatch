using BillWatch.API.Services.Statements;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiPrivateCorpusPathPolicyTests
{
    [Fact]
    public void FixedCorpusFileNames_ResolveWithinTheExplicitRoot()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "BillWatchAiCorpusRoot");

        var statementPath =
            BillStatementAiPrivateCorpusPathPolicy
                .ResolveStatementTextPath(
                    root,
                    "acme-fiber_001");

        var groundTruthPath =
            BillStatementAiPrivateCorpusPathPolicy
                .ResolveGroundTruthPath(
                    root,
                    "acme-fiber_001");

        Assert.Equal(
            Path.Combine(
                Path.GetFullPath(
                    root),
                "acme-fiber_001",
                "statement.txt"),
            statementPath);

        Assert.Equal(
            Path.Combine(
                Path.GetFullPath(
                    root),
                "acme-fiber_001",
                "ground-truth.json"),
            groundTruthPath);
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("case/other")]
    [InlineData("case\\other")]
    [InlineData(".")]
    [InlineData("case name")]
    public void UnsafeCaseIds_AreRejected(string caseId)
    {
        Assert.Throws<ArgumentException>(
            () =>
                BillStatementAiPrivateCorpusPathPolicy
                    .ResolveStatementTextPath(
                        Path.GetTempPath(),
                        caseId));
    }

    [Fact]
    public void RelativeCorpusRoot_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () =>
                BillStatementAiPrivateCorpusPathPolicy
                    .ResolveStatementTextPath(
                        "private-corpus",
                        "case-001"));
    }
}
