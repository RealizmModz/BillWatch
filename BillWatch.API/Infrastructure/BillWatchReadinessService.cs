using BillWatch.API.Data;
using BillWatch.API.Services.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BillWatch.API.Infrastructure;

public sealed class BillWatchReadinessService
{
    private readonly BillWatchDbContext _dbContext;
    private readonly BillStatementStorageOptions _storageOptions;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public BillWatchReadinessService(
        BillWatchDbContext dbContext,
        IOptions<BillStatementStorageOptions> storageOptions,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _dbContext =
            dbContext;

        _storageOptions =
            storageOptions.Value;

        _configuration =
            configuration;

        _environment =
            environment;
    }

    public async Task<bool> IsReadyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await _dbContext.Database.CanConnectAsync(
                    cancellationToken))
            {
                return false;
            }

            if (_dbContext.Database.IsRelational() &&
                (await _dbContext.Database.GetPendingMigrationsAsync(
                    cancellationToken)).Any())
            {
                return false;
            }

            var statementStoragePath =
                ResolveStatementStoragePath();

            if (!await CanWriteDirectoryAsync(
                    statementStoragePath,
                    cancellationToken))
            {
                return false;
            }

            var dataProtectionPath =
                _configuration[
                    "DataProtection:KeysPath"];

            if (_environment.IsDevelopment() &&
                string.IsNullOrWhiteSpace(
                    dataProtectionPath))
            {
                return true;
            }

            return await CanWriteDirectoryAsync(
                Path.GetFullPath(
                    dataProtectionPath!),
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private string ResolveStatementStoragePath()
    {
        if (!string.IsNullOrWhiteSpace(
                _storageOptions.RootPath))
        {
            return Path.GetFullPath(
                _storageOptions.RootPath);
        }

        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "BillWatch",
            "StatementStorage");
    }

    private static async Task<bool> CanWriteDirectoryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            path);

        var probePath =
            Path.Combine(
                path,
                $".readiness-{Guid.NewGuid():N}.tmp");

        try
        {
            await using var probe =
                new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize:
                        1,
                    options:
                        FileOptions.Asynchronous |
                        FileOptions.DeleteOnClose);

            await probe.FlushAsync(
                cancellationToken);

            return true;
        }
        finally
        {
            try
            {
                File.Delete(
                    probePath);
            }
            catch
            {
                // The readiness result must not expose a physical path.
            }
        }
    }
}
