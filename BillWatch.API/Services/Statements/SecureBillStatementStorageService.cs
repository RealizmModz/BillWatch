using Microsoft.Extensions.Options;

namespace BillWatch.API.Services.Statements;

public sealed class SecureBillStatementStorageService
{
    private const int SignatureBufferLength = 8;
    private const int FileBufferSize = 81920;

    private readonly string _rootPath;
    private readonly long _maxFileSizeBytes;

    public SecureBillStatementStorageService(
        IOptions<BillStatementStorageOptions> options,
        IWebHostEnvironment environment)
    {
        var configuredOptions =
            options.Value;

        _maxFileSizeBytes =
            configuredOptions.MaxFileSizeBytes;

        if (_maxFileSizeBytes <= 0)
        {
            throw new InvalidOperationException(
                "Bill statement maximum file size must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(
                configuredOptions.RootPath))
        {
            _rootPath =
                Path.GetFullPath(
                    configuredOptions.RootPath);
        }
        else if (environment.IsDevelopment())
        {
            _rootPath =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "BillWatch",
                    "StatementStorage");
        }
        else
        {
            throw new InvalidOperationException(
                "BillStatementStorage:RootPath must be configured outside development.");
        }

        Directory.CreateDirectory(
            _rootPath);
    }

    public async Task<StoredBillStatementFile>
        StoreAsync(
            Guid userId,
            Stream source,
            string originalFileName,
            CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(
            source);

        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The uploaded file stream is not readable.",
                nameof(source));
        }

        if (string.IsNullOrWhiteSpace(
                originalFileName))
        {
            throw new BillStatementFileValidationException(
                "A file name is required.");
        }

        var suppliedExtension =
            Path.GetExtension(
                    originalFileName)
                .ToLowerInvariant();

        if (suppliedExtension is not
            ".pdf" and not
            ".jpg" and not
            ".jpeg" and not
            ".png")
        {
            throw new BillStatementFileValidationException(
                "Only PDF, JPG, JPEG, and PNG bill statements are supported.");
        }

        var userDirectory =
            GetUserDirectory(
                userId);

        Directory.CreateDirectory(
            userDirectory);

        var temporaryPath =
            Path.Combine(
                userDirectory,
                $".upload-{Guid.NewGuid():N}.tmp");

        try
        {
            var totalBytes =
                await CopyWithLimitAsync(
                    source,
                    temporaryPath,
                    cancellationToken);

            var detectedType =
                await DetectFileTypeAsync(
                    temporaryPath,
                    cancellationToken);

            ValidateExtensionMatchesType(
                suppliedExtension,
                detectedType);

            var storedFileName =
                $"{Guid.NewGuid():N}{detectedType.Extension}";

            var finalPath =
                Path.Combine(
                    userDirectory,
                    storedFileName);

            File.Move(
                temporaryPath,
                finalPath);

            return new StoredBillStatementFile(
                StorageKey:
                    BuildStorageKey(
                        userId,
                        storedFileName),

                MediaType:
                    detectedType.MediaType,

                FileExtension:
                    detectedType.Extension,

                SizeBytes:
                    totalBytes);
        }
        catch
        {
            TryDelete(
                temporaryPath);

            throw;
        }
    }

    public Stream OpenRead(
        Guid userId,
        string storageKey)
    {
        var physicalPath =
            GetOwnedPhysicalPath(
                userId,
                storageKey);

        if (!File.Exists(
                physicalPath))
        {
            throw new FileNotFoundException(
                "The stored bill statement file could not be found.");
        }

        return new FileStream(
            physicalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize:
                FileBufferSize,
            options:
                FileOptions.SequentialScan);
    }

    public void Delete(
        string storageKey)
    {
        var physicalPath =
            GetPhysicalPath(
                storageKey);

        TryDelete(
            physicalPath);
    }

    private string GetOwnedPhysicalPath(
        Guid userId,
        string storageKey)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        var physicalPath =
            GetPhysicalPath(
                storageKey);

        var userDirectory =
            GetUserDirectory(
                userId);

        var userDirectoryWithSeparator =
            userDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!physicalPath.StartsWith(
                userDirectoryWithSeparator,
                GetPathComparison()))
        {
            throw new InvalidOperationException(
                "The stored bill statement does not belong to this user.");
        }

        return physicalPath;
    }

    private string GetPhysicalPath(
        string storageKey)
    {
        if (string.IsNullOrWhiteSpace(
                storageKey))
        {
            throw new ArgumentException(
                "Storage key is required.",
                nameof(storageKey));
        }

        var normalizedKey =
            storageKey.Replace(
                '/',
                Path.DirectorySeparatorChar);

        var candidatePath =
            Path.GetFullPath(
                Path.Combine(
                    _rootPath,
                    normalizedKey));

        var rootWithSeparator =
            _rootPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!candidatePath.StartsWith(
                rootWithSeparator,
                GetPathComparison()))
        {
            throw new InvalidOperationException(
                "The storage key resolves outside the bill statement storage directory.");
        }

        return candidatePath;
    }

    private string GetUserDirectory(
        Guid userId)
    {
        var path =
            Path.GetFullPath(
                Path.Combine(
                    _rootPath,
                    userId.ToString("N")));

        var rootWithSeparator =
            _rootPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!path.StartsWith(
                rootWithSeparator,
                GetPathComparison()))
        {
            throw new InvalidOperationException(
                "The user storage directory resolves outside the configured root.");
        }

        return path;
    }

    private async Task<long>
        CopyWithLimitAsync(
            Stream source,
            string destinationPath,
            CancellationToken cancellationToken)
    {
        await using var destination =
            new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: FileBufferSize,
                useAsync: true);

        var buffer =
            new byte[FileBufferSize];

        long totalBytes =
            0;

        while (true)
        {
            var bytesRead =
                await source.ReadAsync(
                    buffer.AsMemory(
                        0,
                        buffer.Length),
                    cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            totalBytes +=
                bytesRead;

            if (totalBytes >
                _maxFileSizeBytes)
            {
                throw new BillStatementFileValidationException(
                    $"The uploaded bill exceeds the {_maxFileSizeBytes / 1024 / 1024} MB file-size limit.");
            }

            await destination.WriteAsync(
                buffer.AsMemory(
                    0,
                    bytesRead),
                cancellationToken);
        }

        if (totalBytes == 0)
        {
            throw new BillStatementFileValidationException(
                "The uploaded bill is empty.");
        }

        return totalBytes;
    }

    private static async Task<DetectedBillFileType>
        DetectFileTypeAsync(
            string path,
            CancellationToken cancellationToken)
    {
        var signature =
            new byte[SignatureBufferLength];

        await using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: SignatureBufferLength,
                useAsync: true);

        var bytesRead =
            await stream.ReadAsync(
                signature.AsMemory(
                    0,
                    signature.Length),
                cancellationToken);

        if (bytesRead >= 5 &&
            signature[0] == 0x25 &&
            signature[1] == 0x50 &&
            signature[2] == 0x44 &&
            signature[3] == 0x46 &&
            signature[4] == 0x2D)
        {
            return new DetectedBillFileType(
                ".pdf",
                "application/pdf");
        }

        if (bytesRead >= 8 &&
            signature[0] == 0x89 &&
            signature[1] == 0x50 &&
            signature[2] == 0x4E &&
            signature[3] == 0x47 &&
            signature[4] == 0x0D &&
            signature[5] == 0x0A &&
            signature[6] == 0x1A &&
            signature[7] == 0x0A)
        {
            return new DetectedBillFileType(
                ".png",
                "image/png");
        }

        if (bytesRead >= 3 &&
            signature[0] == 0xFF &&
            signature[1] == 0xD8 &&
            signature[2] == 0xFF)
        {
            return new DetectedBillFileType(
                ".jpg",
                "image/jpeg");
        }

        throw new BillStatementFileValidationException(
            "The uploaded file is not a valid supported PDF, JPG, or PNG.");
    }

    private static void
        ValidateExtensionMatchesType(
            string suppliedExtension,
            DetectedBillFileType detectedType)
    {
        var extensionMatches =
            detectedType.Extension switch
            {
                ".jpg" =>
                    suppliedExtension is
                        ".jpg" or
                        ".jpeg",

                _ =>
                    suppliedExtension ==
                    detectedType.Extension
            };

        if (!extensionMatches)
        {
            throw new BillStatementFileValidationException(
                "The uploaded file extension does not match its actual file type.");
        }
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static string BuildStorageKey(
        Guid userId,
        string storedFileName)
    {
        return
            $"{userId:N}/{storedFileName}";
    }

    private static void TryDelete(
        string path)
    {
        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
            // Cleanup failure must not hide the original upload error.
        }
    }

    private sealed record DetectedBillFileType(
        string Extension,
        string MediaType);
}

public sealed record StoredBillStatementFile(
    string StorageKey,
    string MediaType,
    string FileExtension,
    long SizeBytes);

public sealed class BillStatementFileValidationException
    : Exception
{
    public BillStatementFileValidationException(
        string message)
        : base(message)
    {
    }
}