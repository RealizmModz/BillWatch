using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Data;

public sealed class BillWatchDbContext
    : IdentityDbContext<
        ApplicationUser,
        IdentityRole<Guid>,
        Guid>
{
    public BillWatchDbContext(
        DbContextOptions<BillWatchDbContext> options)
        : base(options)
    {
    }

    public DbSet<BillStreamEntity> BillStreams =>
        Set<BillStreamEntity>();

    public DbSet<BankConnectionEntity> BankConnections =>
        Set<BankConnectionEntity>();

    public DbSet<BankAccountEntity> BankAccounts =>
        Set<BankAccountEntity>();

    public DbSet<BankTransactionEntity> BankTransactions =>
        Set<BankTransactionEntity>();

    public DbSet<BillStatementEntity> BillStatements =>
        Set<BillStatementEntity>();

    public DbSet<BillLineItemEntity> BillLineItems =>
        Set<BillLineItemEntity>();

    public DbSet<BillChangeEntity> BillChanges =>
        Set<BillChangeEntity>();

    public DbSet<BillAlertEntity> BillAlerts =>
        Set<BillAlertEntity>();

    public DbSet<BillStatementUploadEntity> BillStatementUploads =>
        Set<BillStatementUploadEntity>();

    public DbSet<BillStatementAiEvaluationEntity> BillStatementAiEvaluations =>
        Set<BillStatementAiEvaluationEntity>();

    public DbSet<PlaidLinkSessionEntity> PlaidLinkSessions =>
        Set<PlaidLinkSessionEntity>();

    protected override void OnModelCreating(
        ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureApplicationUser(builder);
        ConfigureBillStream(builder);
        ConfigureBankConnection(builder);
        ConfigureBankAccount(builder);
        ConfigureBankTransaction(builder);
        ConfigureBillStatement(builder);
        ConfigureBillLineItem(builder);
        ConfigureBillChange(builder);
        ConfigureBillAlert(builder);
        ConfigureBillStatementUpload(builder);
        ConfigureBillStatementAiEvaluation(builder);
        ConfigurePlaidLinkSession(builder);
    }

    private static void ConfigureApplicationUser(
        ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(
            entity =>
            {
                entity.Property(user => user.CreatedAtUtc)
                    .IsRequired();

                entity.Property(user => user.IsActive)
                    .IsRequired();
            });
    }

    private static void ConfigureBillStream(
        ModelBuilder builder)
    {
        builder.Entity<BillStreamEntity>(
            entity =>
            {
                entity.ToTable("BillStreams");

                entity.HasKey(stream => stream.Id);

                entity.HasAlternateKey(stream => new
                {
                    stream.Id,
                    stream.UserId
                });

                entity.Property(stream => stream.ProviderName)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(stream => stream.Category)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(stream => stream.IsActive)
                    .IsRequired();

                entity.Property(stream => stream.CreatedAtUtc)
                    .IsRequired();

                entity.Property(stream => stream.UpdatedAtUtc)
                    .IsRequired();

                entity.HasIndex(stream => stream.UserId);

                entity.HasIndex(stream => new
                {
                    stream.UserId,
                    stream.ProviderName
                });

                entity.HasOne(stream => stream.User)
                    .WithMany()
                    .HasForeignKey(stream => stream.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
    }

    private static void ConfigureBankConnection(
        ModelBuilder builder)
    {
        builder.Entity<BankConnectionEntity>(
            entity =>
            {
                entity.ToTable("BankConnections");

                entity.HasKey(connection => connection.Id);

                entity.HasAlternateKey(connection => new
                {
                    connection.Id,
                    connection.UserId
                });

                entity.Property(connection => connection.InstitutionName)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(connection => connection.PlaidInstitutionId)
                    .HasMaxLength(100);

                entity.Property(connection => connection.PlaidItemId)
                    .HasMaxLength(200);

                entity.Property(connection => connection.Status)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(connection => connection.CreatedAtUtc)
                    .IsRequired();

                entity.Property(connection => connection.UpdatedAtUtc)
                    .IsRequired();

                entity.HasIndex(connection => connection.UserId);

                entity.HasIndex(connection => new
                {
                    connection.UserId,
                    connection.PlaidItemId
                })
                .IsUnique();

                entity.HasOne(connection => connection.User)
                    .WithMany()
                    .HasForeignKey(connection => connection.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
    }

    private static void ConfigureBankAccount(
        ModelBuilder builder)
    {
        builder.Entity<BankAccountEntity>(
            entity =>
            {
                entity.ToTable("BankAccounts");

                entity.HasKey(account => account.Id);

                entity.HasAlternateKey(account => new
                {
                    account.Id,
                    account.UserId
                });

                entity.Property(account => account.PlaidAccountId)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(account => account.Name)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(account => account.OfficialName)
                    .HasMaxLength(300);

                entity.Property(account => account.Mask)
                    .HasMaxLength(10);

                entity.Property(account => account.AccountType)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(account => account.AccountSubtype)
                    .HasMaxLength(100);

                entity.Property(account => account.IsActive)
                    .IsRequired();

                entity.Property(account => account.CreatedAtUtc)
                    .IsRequired();

                entity.Property(account => account.UpdatedAtUtc)
                    .IsRequired();

                entity.HasIndex(account => account.UserId);

                entity.HasIndex(account => account.BankConnectionId);

                entity.HasIndex(account => new
                {
                    account.UserId,
                    account.PlaidAccountId
                })
                .IsUnique();

                entity.HasOne(account => account.User)
                    .WithMany()
                    .HasForeignKey(account => account.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(account => account.BankConnection)
                    .WithMany()
                    .HasForeignKey(account => new
                    {
                        account.BankConnectionId,
                        account.UserId
                    })
                    .HasPrincipalKey(connection => new
                    {
                        connection.Id,
                        connection.UserId
                    })
                    .OnDelete(DeleteBehavior.Cascade);
            });
    }

    private static void ConfigureBankTransaction(
        ModelBuilder builder)
    {
        builder.Entity<BankTransactionEntity>(
            entity =>
            {
                entity.ToTable("BankTransactions");

                entity.HasKey(transaction => transaction.Id);

                entity.Property(transaction => transaction.PlaidTransactionId)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(transaction => transaction.Name)
                    .HasMaxLength(300)
                    .IsRequired();

                entity.Property(transaction => transaction.MerchantName)
                    .HasMaxLength(300);

                entity.Property(transaction => transaction.Amount)
                    .HasPrecision(18, 2)
                    .IsRequired();

                entity.Property(transaction => transaction.IsoCurrencyCode)
                    .HasMaxLength(3);

                entity.Property(transaction => transaction.PostedDate)
                    .IsRequired();

                entity.Property(transaction => transaction.IsPending)
                    .IsRequired();

                entity.Property(transaction => transaction.IsRemoved)
                    .IsRequired();

                entity.Property(transaction => transaction.CategoryPrimary)
                    .HasMaxLength(100);

                entity.Property(transaction => transaction.CategoryDetailed)
                    .HasMaxLength(200);

                entity.Property(transaction => transaction.CreatedAtUtc)
                    .IsRequired();

                entity.Property(transaction => transaction.UpdatedAtUtc)
                    .IsRequired();

                entity.HasIndex(transaction => transaction.UserId);

                entity.HasIndex(transaction => transaction.BankAccountId);

                entity.HasIndex(transaction => transaction.BillStreamId);

                entity.HasIndex(transaction => transaction.PostedDate);

                entity.HasIndex(transaction => new
                {
                    transaction.UserId,
                    transaction.PlaidTransactionId
                })
                .IsUnique();

                entity.HasOne(transaction => transaction.User)
                    .WithMany()
                    .HasForeignKey(transaction => transaction.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(transaction => transaction.BankAccount)
                    .WithMany()
                    .HasForeignKey(transaction => new
                    {
                        transaction.BankAccountId,
                        transaction.UserId
                    })
                    .HasPrincipalKey(account => new
                    {
                        account.Id,
                        account.UserId
                    })
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(transaction => transaction.BillStream)
                    .WithMany()
                    .HasForeignKey(transaction => new
                    {
                        transaction.BillStreamId,
                        transaction.UserId
                    })
                    .HasPrincipalKey(stream => new
                    {
                        stream.Id,
                        stream.UserId
                    })
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    private static void ConfigureBillStatement(
        ModelBuilder builder)
    {
        builder.Entity<BillStatementEntity>(
            entity =>
            {
                entity.ToTable("BillStatements");

                entity.HasKey(statement => statement.Id);

                entity.HasAlternateKey(statement => new
                {
                    statement.Id,
                    statement.UserId
                });

                entity.Property(statement => statement.PeriodStart)
                    .IsRequired();

                entity.Property(statement => statement.PeriodEnd)
                    .IsRequired();

                entity.Property(statement => statement.TotalAmount)
                    .HasPrecision(18, 2)
                    .IsRequired();

                entity.Property(statement => statement.CurrencyCode)
                    .HasMaxLength(3)
                    .IsRequired();

                entity.Property(statement => statement.ProviderStatementId)
                    .HasMaxLength(200);

                entity.Property(statement => statement.RetrievedAtUtc)
                    .IsRequired();

                entity.Property(statement => statement.CreatedAtUtc)
                    .IsRequired();

                entity.Property(statement => statement.UpdatedAtUtc)
                    .IsRequired();

                entity.HasIndex(statement => statement.UserId);

                entity.HasIndex(statement => statement.BillStreamId);

                entity.HasIndex(statement => new
                {
                    statement.UserId,
                    statement.BillStreamId,
                    statement.PeriodStart,
                    statement.PeriodEnd
                });

                entity.HasOne(statement => statement.User)
                    .WithMany()
                    .HasForeignKey(statement => statement.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(statement => statement.BillStream)
                    .WithMany()
                    .HasForeignKey(statement => new
                    {
                        statement.BillStreamId,
                        statement.UserId
                    })
                    .HasPrincipalKey(stream => new
                    {
                        stream.Id,
                        stream.UserId
                    })
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    private static void ConfigureBillLineItem(
        ModelBuilder builder)
    {
        builder.Entity<BillLineItemEntity>(
            entity =>
            {
                entity.ToTable("BillLineItems");

                entity.HasKey(item => item.Id);

                entity.Property(item => item.Description)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(item => item.Amount)
                    .HasPrecision(18, 2)
                    .IsRequired();

                entity.Property(item => item.Category)
                    .HasMaxLength(100);

                entity.Property(item => item.SortOrder)
                    .IsRequired();

                entity.Property(item => item.CreatedAtUtc)
                    .IsRequired();

                entity.Property(item => item.UpdatedAtUtc)
                    .IsRequired();

                entity.HasIndex(item => item.UserId);

                entity.HasIndex(item => item.BillStatementId);

                entity.HasIndex(item => new
                {
                    item.BillStatementId,
                    item.SortOrder
                });

                entity.HasOne(item => item.User)
                    .WithMany()
                    .HasForeignKey(item => item.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(item => item.BillStatement)
                    .WithMany()
                    .HasForeignKey(item => new
                    {
                        item.BillStatementId,
                        item.UserId
                    })
                    .HasPrincipalKey(statement => new
                    {
                        statement.Id,
                        statement.UserId
                    })
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    private static void ConfigureBillChange(
        ModelBuilder builder)
    {
        builder.Entity<BillChangeEntity>(
            entity =>
            {
                entity.ToTable("BillChanges");

                entity.HasKey(change => change.Id);

                entity.HasAlternateKey(change => new
                {
                    change.Id,
                    change.UserId
                });

                entity.Property(change => change.ChangeType)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(change => change.Confidence)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(change => change.Description)
                    .HasMaxLength(1000)
                    .IsRequired();

                entity.Property(change => change.PreviousAmount)
                    .HasPrecision(18, 2)
                    .IsRequired();

                entity.Property(change => change.CurrentAmount)
                    .HasPrecision(18, 2)
                    .IsRequired();

                entity.Property(change => change.AmountDifference)
                    .HasPrecision(18, 2)
                    .IsRequired();

                entity.Property(change => change.AnnualizedImpact)
                    .HasPrecision(18, 2)
                    .IsRequired();

                entity.Property(change => change.IsAcknowledged)
                    .IsRequired();

                entity.Property(change => change.DetectedAtUtc)
                    .IsRequired();

                entity.Property(change => change.CreatedAtUtc)
                    .IsRequired();

                entity.Property(change => change.UpdatedAtUtc)
                    .IsRequired();

                entity.HasIndex(change => change.UserId);

                entity.HasIndex(change => change.BillStreamId);

                entity.HasIndex(change => change.CurrentStatementId);

                entity.HasIndex(change => change.PreviousStatementId);

                entity.HasIndex(change => new
                {
                    change.UserId,
                    change.IsAcknowledged,
                    change.DetectedAtUtc
                });

                entity.HasOne(change => change.User)
                    .WithMany()
                    .HasForeignKey(change => change.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(change => change.BillStream)
                    .WithMany()
                    .HasForeignKey(change => new
                    {
                        change.BillStreamId,
                        change.UserId
                    })
                    .HasPrincipalKey(stream => new
                    {
                        stream.Id,
                        stream.UserId
                    })
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(change => change.CurrentStatement)
                    .WithMany()
                    .HasForeignKey(change => new
                    {
                        change.CurrentStatementId,
                        change.UserId
                    })
                    .HasPrincipalKey(statement => new
                    {
                        statement.Id,
                        statement.UserId
                    })
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(change => change.PreviousStatement)
                    .WithMany()
                    .HasForeignKey(change => new
                    {
                        change.PreviousStatementId,
                        change.UserId
                    })
                    .HasPrincipalKey(statement => new
                    {
                        statement.Id,
                        statement.UserId
                    })
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    private static void ConfigureBillAlert(
        ModelBuilder builder)
    {
        builder.Entity<BillAlertEntity>(
            entity =>
            {
                entity.ToTable("BillAlerts");

                entity.HasKey(alert => alert.Id);

                entity.Property(alert => alert.AlertType)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(alert => alert.Severity)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(alert => alert.Title)
                    .HasMaxLength(300)
                    .IsRequired();

                entity.Property(alert => alert.Message)
                    .HasMaxLength(2000)
                    .IsRequired();

                entity.Property(alert => alert.IsRead)
                    .IsRequired();

                entity.Property(alert => alert.IsDismissed)
                    .IsRequired();

                entity.Property(alert => alert.CreatedAtUtc)
                    .IsRequired();

                entity.Property(alert => alert.UpdatedAtUtc)
                    .IsRequired();

                entity.HasIndex(alert => alert.UserId);

                entity.HasIndex(alert => alert.BillStreamId);

                entity.HasIndex(alert => alert.BillChangeId);

                entity.HasIndex(alert => new
                {
                    alert.UserId,
                    alert.IsDismissed,
                    alert.IsRead,
                    alert.CreatedAtUtc
                });

                entity.HasOne(alert => alert.User)
                    .WithMany()
                    .HasForeignKey(alert => alert.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(alert => alert.BillStream)
                    .WithMany()
                    .HasForeignKey(alert => new
                    {
                        alert.BillStreamId,
                        alert.UserId
                    })
                    .HasPrincipalKey(stream => new
                    {
                        stream.Id,
                        stream.UserId
                    })
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(alert => alert.BillChange)
                    .WithMany()
                    .HasForeignKey(alert => new
                    {
                        alert.BillChangeId,
                        alert.UserId
                    })
                    .HasPrincipalKey(change => new
                    {
                        change.Id,
                        change.UserId
                    })
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    private static void ConfigureBillStatementUpload(
        ModelBuilder builder)
    {
        builder.Entity<BillStatementUploadEntity>(
            entity =>
            {
                entity.ToTable(
                    "BillStatementUploads");

                entity.HasKey(
                    upload => upload.Id);

                entity.HasAlternateKey(
                    upload => new
                    {
                        upload.Id,
                        upload.UserId
                    });

                entity.Property(
                        upload => upload.StorageKey)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(
                        upload => upload.MediaType)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(
                        upload => upload.FileExtension)
                    .HasMaxLength(10)
                    .IsRequired();

                entity.Property(
                        upload => upload.SizeBytes)
                    .IsRequired();

                entity.Property(
                        upload => upload.Status)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(
                        upload => upload.CreatedAtUtc)
                    .IsRequired();

                entity.Property(
                        upload => upload.UpdatedAtUtc)
                    .IsRequired();

                entity.HasIndex(
                    upload => upload.UserId);

                entity.HasIndex(
                    upload => upload.BillStreamId);

                entity.HasIndex(
                    upload => upload.BillStatementId);

                entity.HasIndex(
                    upload => new
                    {
                        upload.UserId,
                        upload.Status,
                        upload.CreatedAtUtc
                    });

                entity.HasOne(
                        upload => upload.User)
                    .WithMany()
                    .HasForeignKey(
                        upload => upload.UserId)
                    .OnDelete(
                        DeleteBehavior.Cascade);

                entity.HasOne(
                        upload => upload.BillStream)
                    .WithMany()
                    .HasForeignKey(
                        upload => new
                        {
                            upload.BillStreamId,
                            upload.UserId
                        })
                    .HasPrincipalKey(
                        stream => new
                        {
                            stream.Id,
                            stream.UserId
                        })
                    .OnDelete(
                        DeleteBehavior.Restrict);

                entity.HasOne(
                        upload => upload.BillStatement)
                    .WithMany()
                    .HasForeignKey(
                        upload => new
                        {
                            upload.BillStatementId,
                            upload.UserId
                        })
                    .HasPrincipalKey(
                        statement => new
                        {
                            statement.Id,
                            statement.UserId
                        })
                    .OnDelete(
                        DeleteBehavior.Restrict);
            });
    }

    private static void ConfigurePlaidLinkSession(
        ModelBuilder builder)
    {
        builder.Entity<PlaidLinkSessionEntity>(
            entity =>
            {
                entity.ToTable("PlaidLinkSessions");

                entity.HasKey(session => session.Id);

                entity.Property(session => session.ProtectedLinkToken)
                    .HasMaxLength(4000)
                    .IsRequired();

                entity.Property(session => session.Status)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(session => session.ExpiresAtUtc)
                    .IsRequired();

                entity.Property(session => session.CreatedAtUtc)
                    .IsRequired();

                entity.Property(session => session.UpdatedAtUtc)
                    .IsRequired();

                entity.HasIndex(session => session.UserId);

                entity.HasIndex(session => new
                {
                    session.UserId,
                    session.Status,
                    session.ExpiresAtUtc
                });

                entity.HasOne(session => session.User)
                    .WithMany()
                    .HasForeignKey(session => session.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
    }

    private static void ConfigureBillStatementAiEvaluation(
        ModelBuilder builder)
    {
        builder.Entity<BillStatementAiEvaluationEntity>(
            entity =>
            {
                entity.ToTable(
                    "BillStatementAiEvaluations",
                    table =>
                        table.HasCheckConstraint(
                            "CK_BillStatementAiEvaluations_AttemptCount",
                            "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= 1"));

                entity.HasKey(
                    evaluation => evaluation.Id);

                entity.HasAlternateKey(
                    evaluation => new
                    {
                        evaluation.Id,
                        evaluation.UserId
                    });

                entity.Property(
                        evaluation => evaluation.Provider)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(
                        evaluation => evaluation.Model)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(
                        evaluation => evaluation.PromptVersion)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(
                        evaluation => evaluation.Status)
                    .HasConversion<string>()
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(
                        evaluation => evaluation.AttemptCount)
                    .IsRequired();

                entity.Property(
                        evaluation => evaluation.CandidateReadyForValidation)
                    .IsRequired();

                entity.Property(
                        evaluation => evaluation.CreatedAtUtc)
                    .IsRequired();

                entity.Property(
                        evaluation => evaluation.UpdatedAtUtc)
                    .IsRequired();

                entity.HasIndex(
                    evaluation => evaluation.UserId);

                entity.HasIndex(
                    evaluation => new
                    {
                        evaluation.UserId,
                        evaluation.Status,
                        evaluation.CreatedAtUtc
                    });

                entity.HasIndex(
                        evaluation => new
                        {
                            evaluation.UserId,
                            evaluation.BillStatementUploadId,
                            evaluation.Provider,
                            evaluation.Model,
                            evaluation.PromptVersion
                        })
                    .IsUnique();

                entity.HasOne(
                        evaluation => evaluation.User)
                    .WithMany()
                    .HasForeignKey(
                        evaluation => evaluation.UserId)
                    .OnDelete(
                        DeleteBehavior.Cascade);

                entity.HasOne(
                        evaluation => evaluation.BillStatementUpload)
                    .WithMany()
                    .HasForeignKey(
                        evaluation => new
                        {
                            evaluation.BillStatementUploadId,
                            evaluation.UserId
                        })
                    .HasPrincipalKey(
                        upload => new
                        {
                            upload.Id,
                            upload.UserId
                        })
                    .OnDelete(
                        DeleteBehavior.Cascade);
            });
    }
}
