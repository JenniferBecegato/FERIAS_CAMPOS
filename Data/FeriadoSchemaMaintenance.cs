using System.IO;
using Microsoft.EntityFrameworkCore;

namespace FeriasCampos.Data;

public static class FeriadoSchemaMaintenance
{
    public static void Atualizar(FeriasDbContext db)
    {
        using var transaction = db.Database.BeginTransaction();
        var columns = db.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM pragma_table_info('Feriados')").ToList();
        if (columns.Contains("Data"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE Feriados_Novo (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Nome TEXT NOT NULL, Dia INTEGER NOT NULL, Mes INTEGER NOT NULL);
                INSERT INTO Feriados_Novo (Id, Nome, Dia, Mes)
                    SELECT Id, Descricao, CAST(strftime('%d', Data) AS INTEGER),
                        CAST(strftime('%m', Data) AS INTEGER) FROM Feriados;
                DROP TABLE Feriados;
                ALTER TABLE Feriados_Novo RENAME TO Feriados;
                """);
        }
        else if (columns.Count == 0)
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE Feriados (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Nome TEXT NOT NULL, Dia INTEGER NOT NULL, Mes INTEGER NOT NULL);
                """);
        }
        db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS Inicializacoes (Chave TEXT NOT NULL PRIMARY KEY)");
        // Unicode-aware comparison, shared in semantics with the CRUD service.
        var connection = (Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection();
        connection.CreateFunction<string, string, bool>("feriado_nome_igual", (a, b) =>
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase));
        try
        {
            using var stream = typeof(FeriadoSchemaMaintenance).Assembly
                .GetManifestResourceStream("FeriasCampos.Data.SeedFeriados.sql")
                ?? throw new InvalidOperationException("Script de feriados não encontrado.");
            using var reader = new StreamReader(stream);
            db.Database.ExecuteSqlRaw(reader.ReadToEnd());
            transaction.Commit();
        }
        finally
        {
            connection.CreateFunction<string, string, bool>("feriado_nome_igual", null);
        }
    }
}
