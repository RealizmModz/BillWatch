using System.Diagnostics;
using Xunit;

namespace BillWatch.Tests.Security;

public sealed class PrivateBetaEvidenceScriptTests
{
    [Theory]
    [InlineData("deploy/tests/plaid-observation-proof-tests.sh")]
    [InlineData("deploy/tests/private-beta-acceptance-evidence-tests.sh")]
    [InlineData("deploy/tests/trusted-beta-launch-evidence-tests.sh")]
    public void EvidenceShellRegressionSuite_Passes(string relativePath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = FindRepositoryRoot();
        var script = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(script), $"Missing regression script: {script}");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = $"\"{script}\"",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not start shell regression suite.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0,
            $"Shell regression suite failed ({process.ExitCode}).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "deploy")) &&
                File.Exists(Path.Combine(directory.FullName, "BillWatch.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the BillWatch repository root from the test output directory.");
    }
}
