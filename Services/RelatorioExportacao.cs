using System.Globalization;
using System.IO;
using System.Text;
using FeriasCampos.Models;

namespace FeriasCampos.Services;

public static class RelatorioExportacao
{
    public static string Texto(object value) => value is DateTime date ? date.ToString("dd/MM/yyyy") : Convert.ToString(value, CultureInfo.GetCultureInfo("pt-BR")) ?? "";

    public static string Csv(ResultadoRelatorio report)
    {
        // Quoting alone does not prevent Excel from interpreting user-entered text as formulas.
        static string Cell(object value)
        {
            var text = Texto(value);
            if (value is string && text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder();
        void Row(IEnumerable<object> cells) => csv.AppendLine(string.Join(";", cells.Select(Cell)));
        Row([report.Titulo]); Row([$"Gerado em {report.GeradoEm:dd/MM/yyyy HH:mm}"]);
        Row([report.Filtros]); Row([report.Totais]); Row(report.Colunas);
        foreach (var row in report.Linhas) Row(row.Valores);
        return csv.ToString();
    }

    public static Task SalvarCsvAsync(ResultadoRelatorio report, string path) => File.WriteAllTextAsync(path, Csv(report), new UTF8Encoding(true));

    // A4, searchable text, repeated page headers and wrapped fields. A record layout avoids
    // reducing the wide report tables to unreadably small print on paper.
    public static byte[] Pdf(ResultadoRelatorio report)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
        var pages = new List<List<string>>();
        List<string> page = [];
        static IEnumerable<string> Wrap(string text)
        {
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var remaining = raw.Replace('\t', ' ');
                while (remaining.Length > 86)
                {
                    var cut = remaining.LastIndexOf(' ', 86, 86);
                    if (cut < 20) cut = 86;
                    yield return remaining[..cut];
                    remaining = remaining[cut..].TrimStart();
                }
                yield return remaining;
            }
        }
        void NewPage()
        {
            page = []; pages.Add(page);
            page.AddRange(Wrap(report.Titulo));
            page.Add($"Gerado em {report.GeradoEm:dd/MM/yyyy HH:mm} | Página {pages.Count}"); page.Add("");
        }
        void Write(string text)
        {
            foreach (var line in Wrap(text)) { if (page.Count >= 58) NewPage(); page.Add(line); }
        }
        NewPage(); Write("FILTROS APLICADOS"); Write(report.Filtros); Write(""); Write(report.Totais); Write("");
        if (report.Linhas.Count == 0) Write("Nenhum registro encontrado para os filtros aplicados.");
        for (var i = 0; i < report.Linhas.Count; i++)
        {
            var row = report.Linhas[i];
            var lines = report.Colunas.SelectMany((c, j) => Wrap($"{c}: {Texto(row.Valores[j])}")).ToList();
            if (page.Count + lines.Count + 2 > 58 && lines.Count + 2 <= 53) NewPage();
            Write($"REGISTRO {i + 1}"); foreach (var line in lines) Write(line); Write("");
        }
        var objects = new List<byte[]>();
        void Obj(string text) => objects.Add(encoding.GetBytes(text));
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj($"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{4 + i * 2} 0 R"))}] >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < pages.Count; i++)
        {
            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + i * 2} 0 R >>");
            var content = new StringBuilder("BT /F1 10 Tf 13 TL 36 806 Td\n");
            foreach (var line in pages[i])
            {
                var safe = new string(line.Where(c => !char.IsControl(c)).ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                content.Append('(').Append(safe).Append(") Tj T*\n");
            }
            content.Append("ET");
            var bytes = encoding.GetBytes(content.ToString());
            Obj($"<< /Length {bytes.Length} >>\nstream\n{content}\nendstream");
        }
        using var output = new MemoryStream();
        void Raw(string text) => output.Write(encoding.GetBytes(text));
        Raw("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++) { offsets.Add(output.Position); Raw($"{i + 1} 0 obj\n"); output.Write(objects[i]); Raw("\nendobj\n"); }
        var xref = output.Position;
        Raw($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Raw($"{offset:0000000000} 00000 n \n");
        Raw($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }
}
