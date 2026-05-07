IF COL_LENGTH('Documents', 'DocumentType') IS NULL
    ALTER TABLE Documents ADD DocumentType nvarchar(80) NOT NULL CONSTRAINT DF_Documents_DocumentType DEFAULT ('Factura electronica');

IF COL_LENGTH('Documents', 'SaleCondition') IS NULL
    ALTER TABLE Documents ADD SaleCondition nvarchar(80) NOT NULL CONSTRAINT DF_Documents_SaleCondition DEFAULT ('Contado');

IF COL_LENGTH('Documents', 'Notes') IS NULL
    ALTER TABLE Documents ADD Notes nvarchar(1000) NULL;

IF COL_LENGTH('Documents', 'ReceiverAddress') IS NULL
    ALTER TABLE Documents ADD ReceiverAddress nvarchar(500) NULL;

IF COL_LENGTH('Documents', 'ReceiverEmail') IS NULL
    ALTER TABLE Documents ADD ReceiverEmail nvarchar(250) NULL;

IF COL_LENGTH('Documents', 'ReceiverPhone') IS NULL
    ALTER TABLE Documents ADD ReceiverPhone nvarchar(60) NULL;

IF COL_LENGTH('Documents', 'SubtotalAmount') IS NULL
    ALTER TABLE Documents ADD SubtotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_Documents_SubtotalAmount DEFAULT (0);

IF COL_LENGTH('Documents', 'Vat5Amount') IS NULL
    ALTER TABLE Documents ADD Vat5Amount decimal(18,2) NOT NULL CONSTRAINT DF_Documents_Vat5Amount DEFAULT (0);

IF COL_LENGTH('Documents', 'Vat10Amount') IS NULL
    ALTER TABLE Documents ADD Vat10Amount decimal(18,2) NOT NULL CONSTRAINT DF_Documents_Vat10Amount DEFAULT (0);

IF COL_LENGTH('Documents', 'ExemptAmount') IS NULL
    ALTER TABLE Documents ADD ExemptAmount decimal(18,2) NOT NULL CONSTRAINT DF_Documents_ExemptAmount DEFAULT (0);

IF COL_LENGTH('Documents', 'TotalVatAmount') IS NULL
    ALTER TABLE Documents ADD TotalVatAmount decimal(18,2) NOT NULL CONSTRAINT DF_Documents_TotalVatAmount DEFAULT (0);

IF COL_LENGTH('Documents', 'TestCdc') IS NULL
    ALTER TABLE Documents ADD TestCdc nvarchar(120) NULL;

IF COL_LENGTH('Documents', 'TestQrText') IS NULL
    ALTER TABLE Documents ADD TestQrText nvarchar(500) NULL;

IF COL_LENGTH('Documents', 'IsFiscalPreviewValid') IS NULL
    ALTER TABLE Documents ADD IsFiscalPreviewValid bit NOT NULL CONSTRAINT DF_Documents_IsFiscalPreviewValid DEFAULT (0);

IF NOT EXISTS (
    SELECT 1
    FROM sys.objects
    WHERE object_id = OBJECT_ID(N'[dbo].[DocumentLines]')
      AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[DocumentLines]
    (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [DocumentId] uniqueidentifier NOT NULL,
        [LineNumber] int NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [Quantity] decimal(18,2) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [VatRate] int NOT NULL,
        [VatAmount] decimal(18,2) NOT NULL,
        [ExemptAmount] decimal(18,2) NOT NULL,
        [SubtotalAmount] decimal(18,2) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT DF_DocumentLines_CreatedAt DEFAULT (sysutcdatetime()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_DocumentLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DocumentLines_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_DocumentLines_DocumentId_LineNumber'
      AND object_id = OBJECT_ID(N'[dbo].[DocumentLines]'))
BEGIN
    CREATE UNIQUE INDEX [IX_DocumentLines_DocumentId_LineNumber]
        ON [dbo].[DocumentLines]([DocumentId], [LineNumber]);
END;
