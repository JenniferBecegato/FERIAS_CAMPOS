# Relatórios de férias

Acesse **Relatórios** no menu lateral. Escolha uma consulta, ajuste os filtros e clique em **Gerar relatório**. A busca considera somente o nome do colaborador. A consulta inclui todos os colaboradores.

Unidade e status permitem múltiplas seleções: clique novamente para desmarcar. Sem seleção, todas as opções são incluídas. Unidades vazias aparecem como **(Não informado)**.

## Consultas

| Consulta | Critério |
|---|---|
| Vencimentos | Períodos com saldo positivo; vencidos ou próximos 30, 60 e 90 dias. Os intervalos futuros incluem hoje e o último dia da faixa. |
| Saldos | Uma linha por período aquisitivo, separando direito, agendamentos, gozo registrado, vendas, folgas e saldo. |
| Programação | Uma linha por parcela de férias que intersecta as datas pesquisadas. A duração exibida é a parcela inteira. |
| Pendências | Períodos já adquiridos com saldo positivo, sem programação ou com programação parcial. |
| Ausências simultâneas | Intervalos com pelo menos duas pessoas distintas na mesma unidade, recortados às datas consultadas. O mínimo é configurável. |
| Extrato | Movimentações por colaborador e data de registro, com sinais de crédito/débito e motivo. |
| Vendas e folgas | Somente os lançamentos desses dois tipos. |
| Conferência | Movimentações filtráveis por tipo, data de registro, identificação e motivo. |

Os campos **Data inicial** e **Data final** ficam logo após o nome do colaborador. As datas significam **vencimento** nos relatórios de períodos; **datas de férias** na programação e sobreposições; **data local do registro** nos relatórios de movimentações. Datas inicial e final são inclusivas e opcionais.

O período aquisitivo usa as datas de início e fim da aquisição, não as datas do período concessivo. O saldo é a soma das movimentações atuais, sem reconstrução de saldo histórico. Agendamentos não são convertidos automaticamente em gozo: somente lançamentos explícitos de tipo Gozo compõem a coluna **Gozo registrado**. O retorno previsto é o dia seguinte ao fim da parcela e deve ser conferido com a escala.

Os totais contam colaboradores e períodos distintos. Na sobreposição, uma pessoa não é contada duas vezes por possuir mais de um período; o pico é calculado por unidade dentro dos filtros aplicados. Créditos, débitos e variação líquida de um extrato filtrado não representam necessariamente o saldo total. A identificação registrada pode ser o nome do computador, não uma identidade autenticada de usuário.

## Exportação

**Exportar CSV (Excel)** gera UTF-8 com BOM, separador ponto e vírgula e escape de aspas e quebras de linha. Textos que possam ser interpretados como fórmulas são neutralizados. **Exportar PDF** gera um documento A4 paginado, em formato de registros para preservar a legibilidade de todas as colunas e dos motivos longos.

Ambos exportam todos os registros da última consulta gerada, seus filtros, data de geração e totais. Alterar filtros exige clicar em **Gerar relatório** novamente. Ordenar a tabela muda apenas a visualização; os arquivos mantêm a ordem original da consulta.

