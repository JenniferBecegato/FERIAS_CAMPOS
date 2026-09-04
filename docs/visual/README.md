# Contrato visual — Controle de Férias

Referência: `Layout_Férias.jpeg`, 1600 × 900 px. Este diretório é o gate obrigatório anterior à implementação visual.

## Artefatos

- `visual-spec.md`: tokens, medidas e tipografia.
- `responsive-map.md`: comportamento nas resoluções-alvo.
- `wireframe.md`: anatomia anotada do dashboard e do agendamento.
- `state-catalog.md`: estados interativos e de dados.
- `traceability.md`: ligação entre referência, componente WPF e verificação.
- `baseline-manifest.json`: resoluções, tolerâncias e nomes dos baselines.

## Critério de aprovação

Em 1600 × 900, cada região deve respeitar posição, dimensão e cor do contrato com tolerância de 3 px e Delta-E médio máximo de 4. Em resoluções responsivas, a hierarquia e todos os comandos devem permanecer acessíveis, sem sobreposição ou corte de texto.

Os screenshots são gerados pelo teste visual em `artifacts/visual/current`. A primeira execução aprovada promove as imagens para `docs/visual/baselines`; alterações posteriores são comparadas por região e nunca atualizam o baseline silenciosamente.
