IF OBJECT_ID(N'dbo.FeInvoiceEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FeInvoiceEvents
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_FeInvoiceEvents PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        InvoiceId uniqueidentifier NOT NULL,
        CorrelationId nvarchar(80) NOT NULL,
        PreviousStatus nvarchar(40) NULL,
        NewStatus nvarchar(40) NOT NULL,
        EventType nvarchar(60) NOT NULL,
        Message nvarchar(500) NOT NULL,
        TechnicalDetail nvarchar(max) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_FeInvoiceEvents_CreatedAt DEFAULT sysutcdatetime()
    );
END;
GO

IF OBJECT_ID(N'dbo.FeTenantLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FeTenantLogs
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_FeTenantLogs PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        InvoiceId uniqueidentifier NULL,
        CorrelationId nvarchar(80) NULL,
        Level nvarchar(20) NOT NULL,
        Source nvarchar(80) NOT NULL,
        Message nvarchar(500) NOT NULL,
        TechnicalDetail nvarchar(max) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_FeTenantLogs_CreatedAt DEFAULT sysutcdatetime()
    );
END;
GO

IF COL_LENGTH(N'dbo.Documents', N'CorrelationId') IS NULL
BEGIN
    ALTER TABLE dbo.Documents ADD CorrelationId nvarchar(80) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Documents', N'InternalStatus') IS NULL
BEGIN
    ALTER TABLE dbo.Documents ADD InternalStatus nvarchar(40) NOT NULL CONSTRAINT DF_Documents_InternalStatus DEFAULT N'DRAFT';
END;
GO

IF COL_LENGTH(N'dbo.Documents', N'RetryCount') IS NULL
BEGIN
    ALTER TABLE dbo.Documents ADD RetryCount int NOT NULL CONSTRAINT DF_Documents_RetryCount DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.Documents', N'IsRetryable') IS NULL
BEGIN
    ALTER TABLE dbo.Documents ADD IsRetryable bit NOT NULL CONSTRAINT DF_Documents_IsRetryable DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.Documents', N'LastErrorCode') IS NULL
BEGIN
    ALTER TABLE dbo.Documents ADD LastErrorCode nvarchar(80) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Documents', N'LastErrorMessage') IS NULL
BEGIN
    ALTER TABLE dbo.Documents ADD LastErrorMessage nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_FeInvoiceEvents_Tenant_Invoice_CreatedAt'
      AND object_id = OBJECT_ID(N'dbo.FeInvoiceEvents')
)
BEGIN
    CREATE INDEX IX_FeInvoiceEvents_Tenant_Invoice_CreatedAt
        ON dbo.FeInvoiceEvents (TenantId, InvoiceId, CreatedAt);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_FeTenantLogs_Tenant_CreatedAt'
      AND object_id = OBJECT_ID(N'dbo.FeTenantLogs')
)
BEGIN
    CREATE INDEX IX_FeTenantLogs_Tenant_CreatedAt
        ON dbo.FeTenantLogs (TenantId, CreatedAt);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_FeTenantLogs_Tenant_Invoice_CreatedAt'
      AND object_id = OBJECT_ID(N'dbo.FeTenantLogs')
)
BEGIN
    CREATE INDEX IX_FeTenantLogs_Tenant_Invoice_CreatedAt
        ON dbo.FeTenantLogs (TenantId, InvoiceId, CreatedAt);
END;
GO
