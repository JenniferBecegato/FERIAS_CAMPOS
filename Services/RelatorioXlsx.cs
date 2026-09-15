using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FeriasCampos.Models;

namespace FeriasCampos.Services;

// Minimal Office Open XML workbook, using typed cells and no executable formulas.
internal static class RelatorioXlsx
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static byte[] Gerar(ResultadoRelatorio report)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Part(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(xml);
            }
            Part("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            Part("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            Part("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Férias" sheetId="1" r:id="rId1"/></sheets></workbook>
                """);
            Part("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
                """);
            Part("xl/styles.xml", """
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                <numFmts count="2"><numFmt numFmtId="164" formatCode="dd/mm/yyyy"/><numFmt numFmtId="165" formatCode="0.##"/></numFmts>
                <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font></fonts>
                <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF0878E8"/><bgColor indexed="64"/></patternFill></fill></fills>
                <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                <cellXfs count="5">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyAlignment="1"><alignment wrapText="1" vertical="center"/></xf>
                <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
                <xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment wrapText="1" vertical="center"/></xf>
                </cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                </styleSheet>
                """);
            var data = new XElement(S + "sheetData");
            void Row(int number, IEnumerable<object> values, int? style = null, int? height = null)
            {
                var row = new XElement(S + "row", new XAttribute("r", number));
                if (height is int h) { row.Add(new XAttribute("ht", h), new XAttribute("customHeight", 1)); }
                var col = 0;
                foreach (var value in values)
                {
                    var cell = new XElement(S + "c", new XAttribute("r", $"{(char)('A' + col++)}{number}"));
                    cell.Add(new XAttribute("s", style ?? (value is DateTime ? 2 : value is decimal ? 3 : 0)));
                    if (value is DateTime date) cell.Add(new XElement(S + "v", date.ToOADate().ToString(CultureInfo.InvariantCulture)));
                    else if (value is decimal or int) cell.Add(new XElement(S + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
                    else cell.Add(new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), RelatorioExportacao.Texto(value))));
                    row.Add(cell);
                }
                data.Add(row);
            }
            Row(1, [report.Titulo], 1, 28);
            Row(2, [$"Gerado em {report.GeradoEm:dd/MM/yyyy HH:mm}"], 4, 22);
            Row(3, [report.Filtros], 4, 48);
            Row(4, [report.Totais], 4, 48);
            Row(6, report.Colunas, 1, 34);
            for (var i = 0; i < report.Linhas.Count; i++) Row(i + 7, report.Linhas[i].Valores);
            var sheet = new XElement(S + "worksheet",
                new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0),
                    new XElement(S + "pane", new XAttribute("ySplit", 6), new XAttribute("topLeftCell", "A7"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                new XElement(S + "cols", Enumerable.Range(1, 8).Select(i => new XElement(S + "col", new XAttribute("min", i), new XAttribute("max", i), new XAttribute("width", i == 1 ? 38 : 24), new XAttribute("customWidth", 1)))),
                data, new XElement(S + "autoFilter", new XAttribute("ref", $"A6:H{Math.Max(6, report.Linhas.Count + 6)}")),
                new XElement(S + "mergeCells", new XAttribute("count", 4), Enumerable.Range(1, 4).Select(i => new XElement(S + "mergeCell", new XAttribute("ref", $"A{i}:H{i}")))));
            Part("xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return output.ToArray();
    }
}
