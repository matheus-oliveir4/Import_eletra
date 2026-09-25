namespace ImportErp.Application;

public interface IHistoricalWorkbookReader
{
    Task<WorkbookPreview> PreviewAsync(string workbookPath, CancellationToken cancellationToken);
}

public interface IHistoricalWorkbookExtractor
{
    Task<WorkbookExtraction> ExtractAsync(string workbookPath, CancellationToken cancellationToken);
}

public sealed record WorkbookPreview(
    string FileName,
    string Sha256,
    IReadOnlyList<SheetPreview> Sheets,
    IReadOnlyList<WorkbookIssue> Issues);

public sealed record SheetPreview(
    string Name,
    int HeaderRow,
    int FirstDataRow,
    int LastDataRow,
    int BusinessRowCount,
    IReadOnlyList<string> Headers,
    int CachedFormulaValueCount,
    int FormulaWithoutCachedValueCount);

public sealed record WorkbookIssue(string Severity, string Code, string Message, string? SheetName = null, int? RowNumber = null, string? ColumnName = null);

public sealed record WorkbookExtraction(
    string FileName,
    string Sha256,
    IReadOnlyList<ExtractedSheet> Sheets);

public sealed record ExtractedSheet(
    string Name,
    int HeaderRow,
    IReadOnlyDictionary<string, string> HeaderColumns,
    IReadOnlyList<ExtractedSourceRow> Rows);

public sealed record ExtractedSourceRow(
    string SheetName,
    int RowNumber,
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlyList<string> ErrorColumns);
