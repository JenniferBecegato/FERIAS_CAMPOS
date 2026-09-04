# Matriz de rastreabilidade visual

| Ref. | Componente WPF | Verificação |
|---|---|---|
| A/F | `NavigationView` | largura, seleção, rodapé e colapso |
| B | `DashboardView.Header` | título e botão PDF |
| C | `MetricCard` | grade, ícones, números e cores |
| D | `PeriodsGrid` | cabeçalho, linhas, status e paginação |
| G | `EmployeeDetailsView` | dados, botões, histórico e saldos |
| H | `VacationCalendar` | mês, intervalos, legenda e navegação |
| Modal | `ScheduleVacationDialog` | seleção início–fim e validações |

Cada item terá teste de renderização em 1600 × 900, 1366 × 768 e 1920 × 1080. O teste falha se houver região ausente, corte, sobreposição, diferença geométrica superior a 3 px na referência ou diferença de imagem superior a 1,5% dos pixels fora da tolerância.
