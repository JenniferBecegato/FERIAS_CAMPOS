# Textos das telas

Os textos de interface ficam em `Properties/ScreenTexts.resx`. As chaves usam
o nome da tela como prefixo: `MainWindow_`, `EmployeesWindow_`,
`ScheduleVacationDialog_`, `RegisterDayOffDialog_`, `VacationAgendaWindow_`,
`ReportsWindow_`, `RulesWindow_` e `SettingsWindow_`.

No XAML, declare `xmlns:texts="clr-namespace:FeriasCampos.Properties"` e use
`Text="{x:Static texts:ScreenTexts.MainWindow_ControleDeFerias}"`.
No C#, importe `FeriasCampos.Properties` e acesse `ScreenTexts.NomeDaChave`.
Textos dinâmicos usam placeholders numerados, como `{0}` ou `{0:dd/MM/yyyy}`,
preenchidos com `string.Format`.

Ao adicionar ou renomear chaves, execute a ferramenta personalizada
`PublicResXFileCodeGenerator` do resource no Visual Studio para atualizar
`ScreenTexts.Designer.cs`. Mantenha esse arquivo versionado para permitir
compilação pela linha de comando. Alterar somente um valor no `.resx` não
exige regenerar o designer, mas exige recompilar a aplicação.

Dados cadastrados, identificadores técnicos, cores, nomes de propriedades de
binding e formatos numéricos de controles permanecem no código de origem.
As mensagens de validação exibidas pelas telas também usam este resource.
