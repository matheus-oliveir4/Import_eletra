using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

internal static class HistoricalWorkbookFixture
{
    public static void Create(string path)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());

        AddSheet(workbookPart, sheets, "Pré Embarque", 1, PreRows());
        AddSheet(workbookPart, sheets, "Pós Embarque", 2, PostRows());
        workbookPart.Workbook.Save();
    }

    private static void AddSheet(WorkbookPart workbookPart, Sheets sheets, string name, uint id, IEnumerable<Row> rows)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(new SheetData(rows));
        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = id, Name = name });
    }

    private static IEnumerable<Row> PreRows()
    {
        yield return Row(4,
            Text("B", 4, "Necessity"), Text("C", 4, "PO Totvs"), Text("D", 4, "Importer"), Text("E", 4, "IP Number"),
            Text("F", 4, "Unit Price"), Text("G", 4, "NCM"), Text("H", 4, "Status"), Text("AZ", 4, "Rupture Risk"));
        yield return Row(5,
            Text("B", 5, "2026-09-24"), Text("C", 5, "PO-100"), Text("D", 5, "ELETRA MATRIZ"), Text("E", 5, "IP-001"),
            Text("F", 5, "10,50"), Text("G", 5, "85044090"), Text("H", 5, "WAITING SHIPMENT"), Formula("AZ", 5, "2+5", "7"));
        yield return Row(6,
            Text("B", 6, "2026-10-01"), Text("C", 6, "PO-100"), Text("D", 6, "ELETRA MATRIZ"), Text("E", 6, "IP-002"),
            Text("F", 6, "0,5"), Error("G", 6, "#REF!"), Text("H", 6, "WAITING ARRIVAL"), Text("AZ", 6, "LOW"));
        yield return Row(7,
            Text("B", 7, "2026-10-02"), Text("D", 7, "ELETRA MATRIZ"), Text("E", 7, "IP-003"),
            Text("H", 7, "WAITING PRODUCTION"), Text("AZ", 7, "MEDIUM"));
        yield return Row(8,
            Text("B", 8, "2026-10-03"), Text("C", 8, "PO-200"), Text("D", 8, "ELETRA CWB"), Text("E", 8, "CANCELLED"),
            Text("H", 8, "CANCELLED"), Text("AZ", 8, "LOW"));
        yield return Row(9,
            Text("B", 9, "2026-10-04"), Text("D", 9, "ELETRA FOR"), Text("H", 9, "WAITING PRODUCTION"), Text("AZ", 9, "HIGH"));
    }

    private static IEnumerable<Row> PostRows()
    {
        yield return Row(4,
            Text("B", 4, "IP Number"), Text("C", 4, "Importer"), Text("D", 4, "Status"), Text("E", 4, "Freight Ccy."),
            Text("F", 4, "Freight Cost"), Text("G", 4, "Taxes Paid (R$)"), Text("H", 4, "Fines R$"), Text("AS", 4, "Qty Ctnr Dem"));
        yield return Row(5,
            Text("B", 5, "IP-001"), Text("C", 5, "ELETRA MATRIZ"), Text("D", 5, "WAITING SHIPMENT"), Text("E", 5, "USD"),
            Text("F", 5, "1,25"), Text("G", 5, "42.50"), Text("H", 5, "0"));
        yield return Row(6, Text("B", 6, "CANCELLED"), Text("D", 6, "CANCELLED"));
        yield return Row(7, Text("D", 7, "MISSING IP"));
        yield return Row(8, Text("B", 8, "IP-004"), Text("C", 8, "ELETRA FOR"), Text("D", 8, "WAITING ARRIVAL"));
    }

    private static Row Row(uint index, params Cell[] cells) => new(cells) { RowIndex = index };

    private static Cell Text(string column, uint row, string value) => new()
    {
        CellReference = $"{column}{row}",
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value))
    };

    private static Cell Error(string column, uint row, string value) => new()
    {
        CellReference = $"{column}{row}",
        DataType = CellValues.Error,
        CellValue = new CellValue(value)
    };

    private static Cell Formula(string column, uint row, string formula, string cachedValue) => new()
    {
        CellReference = $"{column}{row}",
        CellFormula = new CellFormula(formula),
        CellValue = new CellValue(cachedValue)
    };
}
