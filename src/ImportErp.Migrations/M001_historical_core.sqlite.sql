-- Local-development schema. PostgreSQL production migrations remain in M001_historical_core.sql.
CREATE TABLE IF NOT EXISTS Batches (
    Id TEXT NOT NULL CONSTRAINT PK_Batches PRIMARY KEY,
    FileName TEXT NOT NULL,
    FileSha256 TEXT NOT NULL,
    MappingVersion TEXT NOT NULL,
    State TEXT NOT NULL,
    SourceRowCount INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS SourceRows (
    Id TEXT NOT NULL CONSTRAINT PK_SourceRows PRIMARY KEY,
    BatchId TEXT NOT NULL,
    SheetName TEXT NOT NULL,
    RowNumber INTEGER NOT NULL,
    RawValuesJson TEXT NOT NULL,
    ErrorColumnsJson TEXT NOT NULL,
    RowHash TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS PurchaseOrders (
    Id TEXT NOT NULL CONSTRAINT PK_PurchaseOrders PRIMARY KEY,
    Importer TEXT NOT NULL,
    ExternalNumber TEXT NOT NULL,
    NormalizedNumber TEXT NOT NULL,
    OperationalFieldsJson TEXT NOT NULL,
    Version INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS ImportProcesses (
    Id TEXT NOT NULL CONSTRAINT PK_ImportProcesses PRIMARY KEY,
    Importer TEXT NULL,
    IpNumber TEXT NOT NULL,
    NormalizedIpNumber TEXT NOT NULL,
    LogisticsStatus TEXT NULL
);

CREATE TABLE IF NOT EXISTS Observations (
    Id TEXT NOT NULL CONSTRAINT PK_Observations PRIMARY KEY,
    PurchaseOrderId TEXT NOT NULL,
    SourceRowId TEXT NOT NULL,
    SourceRowNumber INTEGER NOT NULL,
    RawValuesJson TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ProcessPurchaseOrders (
    PurchaseOrderId TEXT NOT NULL,
    ProcessId TEXT NOT NULL,
    CONSTRAINT PK_ProcessPurchaseOrders PRIMARY KEY (PurchaseOrderId, ProcessId)
);

CREATE TABLE IF NOT EXISTS ProcessCosts (
    Id TEXT NOT NULL CONSTRAINT PK_ProcessCosts PRIMARY KEY,
    ProcessId TEXT NOT NULL,
    SourceRowId TEXT NOT NULL,
    SourceColumn TEXT NOT NULL,
    Type TEXT NOT NULL,
    Amount TEXT NOT NULL,
    Currency TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Issues (
    Id TEXT NOT NULL CONSTRAINT PK_Issues PRIMARY KEY,
    BatchId TEXT NOT NULL,
    SourceRowId TEXT NULL,
    Severity TEXT NOT NULL,
    Code TEXT NOT NULL,
    ColumnName TEXT NULL,
    EvidenceJson TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_Batches_FileSha256_MappingVersion ON Batches (FileSha256, MappingVersion);
CREATE UNIQUE INDEX IF NOT EXISTS IX_SourceRows_BatchId_SheetName_RowNumber ON SourceRows (BatchId, SheetName, RowNumber);
CREATE UNIQUE INDEX IF NOT EXISTS IX_PurchaseOrders_Importer_NormalizedNumber ON PurchaseOrders (Importer, NormalizedNumber);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ImportProcesses_NormalizedIpNumber ON ImportProcesses (NormalizedIpNumber);
CREATE UNIQUE INDEX IF NOT EXISTS IX_Observations_SourceRowId ON Observations (SourceRowId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ProcessCosts_SourceRowId_SourceColumn ON ProcessCosts (SourceRowId, SourceColumn);
CREATE UNIQUE INDEX IF NOT EXISTS IX_Issues_BatchId_SourceRowId_Code_ColumnName ON Issues (BatchId, SourceRowId, Code, ColumnName);
