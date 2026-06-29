using Microsoft.EntityFrameworkCore;

var connectionString = args.ElementAtOrDefault(0)
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=SifenInvoicingDev;Trusted_Connection=True;TrustServerCertificate=True";

var options = new DbContextOptionsBuilder<DbContext>()
    .UseSqlServer(connectionString)
    .Options;

await using var dbContext = new DbContext(options);

var sql = """
IF COL_LENGTH('Documents', 'CorrelationId') IS NULL
    ALTER TABLE Documents ADD CorrelationId nvarchar(80) NULL;

IF COL_LENGTH('Documents', 'InternalStatus') IS NULL
    ALTER TABLE Documents ADD InternalStatus nvarchar(40) NOT NULL CONSTRAINT DF_Documents_InternalStatus DEFAULT('DRAFT') WITH VALUES;

IF COL_LENGTH('Documents', 'RetryCount') IS NULL
    ALTER TABLE Documents ADD RetryCount int NOT NULL CONSTRAINT DF_Documents_RetryCount DEFAULT(0) WITH VALUES;

IF COL_LENGTH('Documents', 'IsRetryable') IS NULL
    ALTER TABLE Documents ADD IsRetryable bit NOT NULL CONSTRAINT DF_Documents_IsRetryable DEFAULT(0) WITH VALUES;

IF COL_LENGTH('Documents', 'LastErrorCode') IS NULL
    ALTER TABLE Documents ADD LastErrorCode nvarchar(80) NULL;

IF COL_LENGTH('Documents', 'LastErrorMessage') IS NULL
    ALTER TABLE Documents ADD LastErrorMessage nvarchar(500) NULL;

IF OBJECT_ID('FeInvoiceEvents', 'U') IS NULL
BEGIN
    CREATE TABLE FeInvoiceEvents (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_FeInvoiceEvents PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        InvoiceId uniqueidentifier NOT NULL,
        CorrelationId nvarchar(80) NOT NULL,
        PreviousStatus nvarchar(40) NULL,
        NewStatus nvarchar(40) NOT NULL,
        EventType nvarchar(60) NOT NULL,
        Message nvarchar(500) NOT NULL,
        TechnicalDetail nvarchar(max) NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_FeInvoiceEvents_CreatedAt DEFAULT(sysutcdatetime()),
        UpdatedAt datetimeoffset NULL
    );

    CREATE INDEX IX_FeInvoiceEvents_TenantId_InvoiceId_CreatedAt
        ON FeInvoiceEvents (TenantId, InvoiceId, CreatedAt);
END;

IF OBJECT_ID('FeTenantLogs', 'U') IS NULL
BEGIN
    CREATE TABLE FeTenantLogs (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_FeTenantLogs PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        InvoiceId uniqueidentifier NULL,
        CorrelationId nvarchar(80) NULL,
        Level nvarchar(20) NOT NULL,
        Source nvarchar(80) NOT NULL,
        Message nvarchar(500) NOT NULL,
        TechnicalDetail nvarchar(max) NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_FeTenantLogs_CreatedAt DEFAULT(sysutcdatetime()),
        UpdatedAt datetimeoffset NULL
    );

    CREATE INDEX IX_FeTenantLogs_TenantId_CreatedAt
        ON FeTenantLogs (TenantId, CreatedAt);

    CREATE INDEX IX_FeTenantLogs_TenantId_InvoiceId_CreatedAt
        ON FeTenantLogs (TenantId, InvoiceId, CreatedAt);
END;
""";

await dbContext.Database.ExecuteSqlRawAsync(sql);
Console.WriteLine("Local SIFEN schema synchronized.");
