-- Audit trail for decisions about historical-data quality items. Source rows are immutable.
CREATE TABLE IF NOT EXISTS QualityReviews (
    Id TEXT NOT NULL CONSTRAINT PK_QualityReviews PRIMARY KEY,
    BatchId TEXT NOT NULL,
    SourceRowId TEXT NOT NULL,
    IssueCode TEXT NOT NULL,
    Outcome TEXT NOT NULL,
    Reviewer TEXT NOT NULL,
    Notes TEXT NOT NULL,
    ProposedPurchaseOrder TEXT NULL,
    ProposedIpNumber TEXT NULL,
    RecordedAt TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_QualityReviews_BatchId_SourceRowId_IssueCode_RecordedAt
ON QualityReviews (BatchId, SourceRowId, IssueCode, RecordedAt DESC);
