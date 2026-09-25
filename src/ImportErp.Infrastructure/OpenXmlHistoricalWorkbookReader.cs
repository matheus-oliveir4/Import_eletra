using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ImportErp.Application;
using ImportErp.Domain;

namespace ImportErp.Infrastructure;

/// <summary>Reads only cached cell values; Excel formula expressions are never evaluated or copied.</summary>
public sealed class OpenXmlHistoricalWorkbookReader : IHistoricalWorkbookReader, IHistoricalWorkbookExtractor
{
    private const string PreShipment = "Pré Embarque";
    private const string PostShipment = "Pós Embarque";

    public Task<WorkbookPreview> PreviewAsync(string workbookPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        var fullPath = Path.GetFullPath(workbookPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Arquivo XLSX não encontrado.", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException("A prévia aceita somente arquivos .xlsx.");
        }

        using var document = SpreadsheetDocument.Open(fullPath, false);
        var workbookPart = document.WorkbookPart ?? throw new DomainValidationException("Workbook inválido.");
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var issues = new List<WorkbookIssue>();
        var previews = new List<SheetPreview>();

        foreach (var sheet in workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = sheet.Name?.Value ?? string.Empty;
            if (!string.Equals(name, PreShipment, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, PostShipment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relationId = sheet.Id?.Value ?? throw new DomainValidationException($"A aba {name} não possui relação.");
            var worksheet = (WorksheetPart)workbookPart.GetPartById(relationId);
            previews.Add(ReadSheet(name, worksheet, sharedStrings, issues, cancellationToken));
        }

        if (previews.Count != 2)
        {
            issues.Add(new WorkbookIssue("BLOCKING", "REQUIRED_SHEET_MISSING", "As abas Pré Embarque e Pós Embarque são obrigatórias."));
        }

        return Task.FromResult(new WorkbookPreview(
            Path.GetFileName(fullPath),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))),
            previews,
            issues));
    }

    public Task<WorkbookExtraction> ExtractAsync(string workbookPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(workbookPath);
        if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException("Informe um arquivo XLSX existente.");
        }

        using var document = SpreadsheetDocument.Open(fullPath, false);
        var workbookPart = document.WorkbookPart ?? throw new DomainValidationException("Workbook inválido.");
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sheets = new List<ExtractedSheet>();

        foreach (var sheet in workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? [])
        {
            var name = sheet.Name?.Value ?? string.Empty;
            if (!string.Equals(name, PreShipment, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, PostShipment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relationId = sheet.Id?.Value ?? throw new DomainValidationException($"A aba {name} não possui relação.");
            var worksheet = (WorksheetPart)workbookPart.GetPartById(relationId);
            sheets.Add(ExtractSheet(name, worksheet, sharedStrings, cancellationToken));
        }

        if (sheets.Count != 2)
        {
            throw new DomainValidationException("As abas Pré Embarque e Pós Embarque são obrigatórias.");
        }

        return Task.FromResult(new WorkbookExtraction(
            Path.GetFileName(fullPath),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))),
            sheets));
    }

    private static ExtractedSheet ExtractSheet(
        string sheetName,
        WorksheetPart worksheetPart,
        SharedStringTable? sharedStrings,
        CancellationToken cancellationToken)
    {
        var expectedEnd = string.Equals(sheetName, PreShipment, StringComparison.OrdinalIgnoreCase) ? "AZ" : "AS";
        var requiredFirstHeader = string.Equals(sheetName, PreShipment, StringComparison.OrdinalIgnoreCase) ? "Necessity" : "IP Number";
        var requiredLastHeader = string.Equals(sheetName, PreShipment, StringComparison.OrdinalIgnoreCase) ? "Rupture Risk" : "Qty Ctnr Dem";
        var headerRow = 0;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ExtractedSourceRow>();

        using var reader = OpenXmlReader.Create(worksheetPart);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
            {
                continue;
            }

            var row = reader.LoadCurrentElement() as Row
                ?? throw new DomainValidationException("Linha XLSX inválida.");
            var rowNumber = (int)(row.RowIndex?.Value ?? 0);
            var cells = row.Elements<Cell>()
                .Select(cell => new ExtractCell(
                    ColumnName(cell.CellReference?.Value),
                    GetStoredValue(cell, sharedStrings),
                    cell.DataType?.Value == CellValues.Error))
                .Where(cell => IsInRange(cell.Column, "B", expectedEnd))
                .ToArray();

            if (headerRow == 0 && HasHeaders(cells.Select(cell => new CellValue(cell.Column, cell.Value, false)), requiredFirstHeader, requiredLastHeader))
            {
                headerRow = rowNumber;
                headers = cells
                    .Where(cell => !string.IsNullOrWhiteSpace(cell.Value))
                    .ToDictionary(cell => cell.Column, cell => cell.Value!, StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (headerRow == 0 || rowNumber <= headerRow || !cells.Any(cell => !string.IsNullOrEmpty(cell.Value)))
            {
                continue;
            }

            var values = headers.ToDictionary(
                header => header.Value,
                header => cells.FirstOrDefault(cell => string.Equals(cell.Column, header.Key, StringComparison.OrdinalIgnoreCase))?.Value,
                StringComparer.OrdinalIgnoreCase);
            var errors = cells
                .Where(cell => cell.IsError && headers.ContainsKey(cell.Column))
                .Select(cell => headers[cell.Column])
                .ToArray();
            rows.Add(new ExtractedSourceRow(sheetName, rowNumber, values, errors));
        }

        if (headerRow == 0)
        {
            throw new DomainValidationException($"Não foi localizado o intervalo {requiredFirstHeader} até {requiredLastHeader} em {sheetName}.");
        }

        return new ExtractedSheet(sheetName, headerRow, headers, rows);
    }

    private static SheetPreview ReadSheet(
        string sheetName,
        WorksheetPart worksheetPart,
        SharedStringTable? sharedStrings,
        ICollection<WorkbookIssue> issues,
        CancellationToken cancellationToken)
    {
        var expectedStart = "B";
        var expectedEnd = string.Equals(sheetName, PreShipment, StringComparison.OrdinalIgnoreCase) ? "AZ" : "AS";
        var requiredFirstHeader = string.Equals(sheetName, PreShipment, StringComparison.OrdinalIgnoreCase) ? "Necessity" : "IP Number";
        var requiredLastHeader = string.Equals(sheetName, PreShipment, StringComparison.OrdinalIgnoreCase) ? "Rupture Risk" : "Qty Ctnr Dem";
        var headerRow = 0;
        var firstDataRow = 0;
        var lastDataRow = 0;
        var businessRows = 0;
        var cachedFormulaValues = 0;
        var formulaWithoutCachedValue = 0;
        IReadOnlyList<string> headers = [];

        using var reader = OpenXmlReader.Create(worksheetPart);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
            {
                continue;
            }

            var row = reader.LoadCurrentElement() as Row
                ?? throw new DomainValidationException("Linha XLSX inválida.");
            var rowNumber = (int)(row.RowIndex?.Value ?? 0);
            var values = row.Elements<Cell>()
                .Select(cell => new CellValue(ColumnName(cell.CellReference?.Value), GetStoredValue(cell, sharedStrings), cell.CellFormula is not null))
                .ToArray();

            var scoped = values.Where(x => IsInRange(x.Column, expectedStart, expectedEnd)).ToArray();
            if (headerRow == 0 && HasHeaders(scoped, requiredFirstHeader, requiredLastHeader))
            {
                headerRow = rowNumber;
                headers = scoped.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Value!).ToArray();
                ValidateHeaderSpan(sheetName, scoped, expectedStart, expectedEnd, requiredFirstHeader, requiredLastHeader, issues, rowNumber);
                continue;
            }

            if (headerRow == 0 || rowNumber <= headerRow)
            {
                continue;
            }

            foreach (var value in scoped.Where(x => x.HasFormula))
            {
                if (string.IsNullOrEmpty(value.Value))
                {
                    formulaWithoutCachedValue++;
                }
                else
                {
                    cachedFormulaValues++;
                }
            }

            if (!scoped.Any(x => !string.IsNullOrEmpty(x.Value)))
            {
                continue;
            }

            firstDataRow = firstDataRow == 0 ? rowNumber : firstDataRow;
            lastDataRow = rowNumber;
            businessRows++;
        }

        if (headerRow == 0)
        {
            issues.Add(new WorkbookIssue("BLOCKING", "HEADER_NOT_FOUND", $"Não foi localizado o intervalo {requiredFirstHeader} até {requiredLastHeader}.", sheetName));
        }

        return new SheetPreview(sheetName, headerRow, firstDataRow, lastDataRow, businessRows, headers, cachedFormulaValues, formulaWithoutCachedValue);
    }

    private static bool HasHeaders(IEnumerable<CellValue> values, string first, string last) => values.Any(x => string.Equals(x.Value, first, StringComparison.OrdinalIgnoreCase))
        && values.Any(x => string.Equals(x.Value, last, StringComparison.OrdinalIgnoreCase));

    private static void ValidateHeaderSpan(string sheetName, IEnumerable<CellValue> values, string expectedStart, string expectedEnd, string first, string last, ICollection<WorkbookIssue> issues, int rowNumber)
    {
        var firstHeader = values.FirstOrDefault(x => string.Equals(x.Value, first, StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainValidationException("Cabeçalho inicial ausente.");
        var lastHeader = values.FirstOrDefault(x => string.Equals(x.Value, last, StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainValidationException("Cabeçalho final ausente.");
        if (firstHeader.Column != expectedStart || lastHeader.Column != expectedEnd)
        {
            issues.Add(new WorkbookIssue(
                "BLOCKING",
                "UNEXPECTED_COLUMN_LAYOUT",
                $"O intervalo esperado é {expectedStart}:{expectedEnd}; recebido {firstHeader.Column}:{lastHeader.Column}.",
                sheetName,
                rowNumber));
        }
    }

    private static string? GetStoredValue(Cell cell, SharedStringTable? sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(cell.CellValue?.Text, out var index))
        {
            return sharedStrings?.Elements<SharedStringItem>().ElementAtOrDefault(index)?.InnerText;
        }

        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText;
        }

        return cell.CellValue?.Text;
    }

    private static string ColumnName(string? reference) => reference is null
        ? string.Empty
        : new string(reference.TakeWhile(char.IsLetter).ToArray());

    private static bool IsInRange(string column, string start, string end) => ToColumnNumber(column) >= ToColumnNumber(start)
        && ToColumnNumber(column) <= ToColumnNumber(end);

    private static int ToColumnNumber(string value)
    {
        var result = 0;
        foreach (var character in value)
        {
            result = (result * 26) + (char.ToUpperInvariant(character) - 'A' + 1);
        }

        return result;
    }

    private sealed record CellValue(string Column, string? Value, bool HasFormula);
    private sealed record ExtractCell(string Column, string? Value, bool IsError);
}
