# Importação da previsão de férias

Use **Importar relatório PDF**, selecione a Escala de vencimentos SCI e escolha a unidade dos novos colaboradores.

**Regra de identificação:** se o nome for encontrado, a importação reutiliza o cadastro existente e não cria outra pessoa, independentemente da unidade selecionada, CPF ou data de admissão. Essa regra também vale para nomes repetidos dentro do próprio PDF. Os dados cadastrais preenchidos são preservados.

- O funcionário é localizado pelo nome completo, ignorando acentos, caixa e espaços repetidos. Nomes repetidos em mais de um cadastro geram aviso e impedem apenas a importação da pessoa afetada.
- Nomes semelhantes (sobrenomes omitidos ou pequenas diferenças de escrita) geram aviso com os cadastros que precisam de revisão. A pessoa afetada é ignorada e as demais continuam sendo importadas. Padronize o nome após conferir a identidade e importe novamente. A aplicação não une pessoas automaticamente nem apaga duplicidades anteriores.
- Funcionários novos recebem CPF em branco, a unidade selecionada e admissão estimada pelo início do período mais antigo listado. O PDF não permite recuperar a admissão real. Dados de funcionários existentes são preservados.
- Cada período é identificado pelo funcionário e pelas datas de início e fim. Períodos novos são criados; existentes são preservados, com aviso quando o saldo diverge. Reimportar não repõe dias já consumidos.
- Saldos fracionados são arredondados para baixo na importação: 17,50 vira 17 dias e 2,50 vira 2 dias. Saldos inteiros e zerados são mantidos. O saldo inicial fica registrado em uma movimentação de aquisição. O direito anual permanece em 30 dias, separado do saldo disponível informado pelo relatório.
- O vencimento no app representa o último dia para concluir as férias. É calculado pelo “Último prazo 30 dias” mais 29 dias, incluindo o dia inicial. Isso mantém as prorrogações individuais do PDF. A coluna “Vencimento” do relatório corresponde ao fim do período aquisitivo.
- Linhas de período perdido por afastamento sem início e saldo válidos são ignoradas e informadas no resultado.
- Linhas não reconhecidas, datas ou saldos inválidos, nomes ambíguos e períodos sobrepostos geram avisos com o nome da pessoa e o motivo. Nenhum período dessa pessoa é importado, inclusive quando o conflito aparece depois de um período válido. As demais pessoas são importadas normalmente; o resultado apresenta os totais e os avisos. Se todas forem incoerentes, os totais serão zero e os avisos explicarão os motivos.
- A gravação dos registros válidos é transacional. Falhas ao abrir o arquivo, formato de PDF incompatível ou falhas de banco de dados ainda impedem concluir a operação.
- A manutenção anual continua criando períodos posteriores conforme a data atual, sem alterar o saldo dos períodos importados.

O formato aceito é o PDF textual SCI fornecido, com as duas colunas de último prazo. PDFs digitalizados precisam de reconhecimento de texto antes da importação.

## Validação

Os testes cobrem leitura de nomes em mais de uma linha, arredondamento de saldo fracionado para baixo e saldo zerado, prazo prorrogado, reimportação, preservação de cadastro e movimentações, importação parcial com avisos por pessoa, datas inválidas, nomes ambíguos, períodos sobrepostos e CPF opcional em bancos novos e existentes.

Para executar a integração com o PDF original sem armazenar dados pessoais no repositório, defina `FERIAS_PDF_REFERENCIA` com o caminho do arquivo antes de executar `dotnet test Tests/ControleFerias.Tests.csproj`. Esse teste verifica 48 funcionários e 65 períodos e compara cada saldo salvo com o valor lido arredondado para baixo.
