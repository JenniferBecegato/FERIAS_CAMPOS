-- Datas fixas: https://www.presidenteprudente.sp.gov.br/site/noticia/66177
-- Executado dentro da transação de FeriadoSchemaMaintenance.
WITH Padrao(Nome, Dia, Mes) AS (VALUES
    ('São Sebastião', 20, 1),
    ('Tiradentes', 21, 4),
    ('Dia do Trabalho', 1, 5),
    ('Revolução Constitucionalista de 1932', 9, 7),
    ('Independência do Brasil', 7, 9),
    ('Fundação de Presidente Prudente', 14, 9),
    ('Nossa Senhora Aparecida', 12, 10),
    ('Finados', 2, 11),
    ('Proclamação da República', 15, 11),
    ('Dia Nacional de Zumbi e da Consciência Negra', 20, 11),
    ('Imaculada Conceição de Nossa Senhora', 8, 12),
    ('Natal', 25, 12)
)
INSERT INTO Feriados (Nome, Dia, Mes)
SELECT p.Nome, p.Dia, p.Mes FROM Padrao p
WHERE NOT EXISTS (SELECT 1 FROM Inicializacoes WHERE Chave = 'feriados-fixos-v1')
AND NOT EXISTS (SELECT 1 FROM Feriados f WHERE f.Dia = p.Dia AND f.Mes = p.Mes
    AND feriado_nome_igual(f.Nome, p.Nome));

INSERT OR IGNORE INTO Inicializacoes (Chave) VALUES ('feriados-fixos-v1');
