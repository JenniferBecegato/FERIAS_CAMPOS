using Microsoft.EntityFrameworkCore;

namespace FeriasCampos.Data;

public static class ColaboradorSchemaMaintenance
{
    public static void Atualizar(FeriasDbContext database)
    {
        var columns = database.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM pragma_table_info('Colaboradores')").ToList();
        // Nomes mantidos somente para remover as colunas de versões anteriores.
        var commands = new Dictionary<string, string>
        {
            ["Matricula"] = "ALTER TABLE Colaboradores DROP COLUMN Matricula",
            ["Ativo"] = "ALTER TABLE Colaboradores DROP COLUMN Ativo",
            ["Cargo"] = "ALTER TABLE Colaboradores DROP COLUMN Cargo",
            ["Setor"] = "ALTER TABLE Colaboradores DROP COLUMN Setor"
        };
        var obsolete = commands.Keys.Where(columns.Contains).ToList();

        using var transaction = database.Database.BeginTransaction();
        database.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_Colaboradores_Cpf");
        database.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IX_Colaboradores_Cpf ON Colaboradores(Cpf) WHERE Cpf <> ''");
        database.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_Colaboradores_Matricula");
        foreach (var column in obsolete)
            database.Database.ExecuteSqlRaw(commands[column]);
        transaction.Commit();
    }
}
