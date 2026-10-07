BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    DROP INDEX [IX_Documents_TenantId_Environment_Kind_EstablishmentCode_ExpeditionPointCode_ExternalDocumentNumber] ON [Documents];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [TaxpayerProfiles] ADD [Address] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [TaxpayerProfiles] ADD [CityCode] nvarchar(10) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [TaxpayerProfiles] ADD [CityDescription] nvarchar(60) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [TaxpayerProfiles] ADD [DepartmentCode] nvarchar(10) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [TaxpayerProfiles] ADD [DepartmentDescription] nvarchar(60) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [TaxpayerProfiles] ADD [DistrictCode] nvarchar(10) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [TaxpayerProfiles] ADD [DistrictDescription] nvarchar(60) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [TaxpayerProfiles] ADD [Email] nvarchar(80) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [TaxpayerProfiles] ADD [HouseNumber] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [TaxpayerProfiles] ADD [Phone] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [TaxpayerProfiles] ADD [TaxpayerType] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [Documents] ADD [NumberingSequenceId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    ALTER TABLE [Documents] ADD [StampingNumber] nvarchar(8) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    CREATE TABLE [FiscalStamps] (
        [Id] uniqueidentifier NOT NULL,
        [Environment] nvarchar(40) NOT NULL,
        [StampingNumber] nvarchar(8) NOT NULL,
        [ValidFrom] date NOT NULL,
        [ValidTo] date NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_FiscalStamps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FiscalStamps_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    CREATE TABLE [IdempotencyRecords] (
        [Id] uniqueidentifier NOT NULL,
        [Key] nvarchar(128) NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [DocumentId] uniqueidentifier NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IdempotencyRecords_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    CREATE TABLE [NumberingSequences] (
        [Id] uniqueidentifier NOT NULL,
        [Environment] nvarchar(40) NOT NULL,
        [FiscalStampId] uniqueidentifier NOT NULL,
        [DocumentTypeCode] nvarchar(2) NOT NULL,
        [EstablishmentCode] nvarchar(3) NOT NULL,
        [ExpeditionPointCode] nvarchar(3) NOT NULL,
        [Series] nvarchar(10) NOT NULL DEFAULT N'',
        [NextNumber] bigint NOT NULL,
        [Version] bigint NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_NumberingSequences] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NumberingSequences_FiscalStamps_FiscalStampId] FOREIGN KEY ([FiscalStampId]) REFERENCES [FiscalStamps] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_NumberingSequences_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Documents_TenantId_Environment_Kind_StampingNumber_EstablishmentCode_ExpeditionPointCode_ExternalDocumentNumber] ON [Documents] ([TenantId], [Environment], [Kind], [StampingNumber], [EstablishmentCode], [ExpeditionPointCode], [ExternalDocumentNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FiscalStamps_TenantId_Environment_StampingNumber] ON [FiscalStamps] ([TenantId], [Environment], [StampingNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IdempotencyRecords_TenantId_Key] ON [IdempotencyRecords] ([TenantId], [Key]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    CREATE INDEX [IX_NumberingSequences_FiscalStampId] ON [NumberingSequences] ([FiscalStampId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NumberingSequences_TenantId_Environment_FiscalStampId_DocumentTypeCode_EstablishmentCode_ExpeditionPointCode_Series] ON [NumberingSequences] ([TenantId], [Environment], [FiscalStampId], [DocumentTypeCode], [EstablishmentCode], [ExpeditionPointCode], [Series]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007124648_AddFiscalNumberingAndIdempotency'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007124648_AddFiscalNumberingAndIdempotency', N'8.0.22');
END;
GO

COMMIT;
GO

