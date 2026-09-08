using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FeriasCampos.Models;
using FeriasCampos.Services;

namespace FeriasCampos.Views;

public partial class ScheduleVacationDialog : Window
{
    private readonly int _saldoSemAjusteFaltas;
    private readonly int _vagasDisponiveis;
    private readonly bool _bloquearInicioAntesRepousoSemanal;
    private readonly IReadOnlyList<IntervaloItem> _existentes;
    private readonly bool _descontarPorFaltas;
    private readonly int _direitoOriginal;
    private readonly int _vendidos;
    private readonly DateTime _vencimento;
    private readonly ObservableCollection<IntervaloItem> _novos = [];
    private bool _adjusting;
    private IntervaloItem? _editing;

    public ScheduleVacationDialog(
        PeriodoAquisitivo periodo,
        bool bloquearAgendamentoMenos30Dias,
        bool bloquearInicioAntesRepousoSemanal,
        bool descontarPorFaltas,
        IEnumerable<Feriado> feriados)
    {
        InitializeComponent();
        _bloquearInicioAntesRepousoSemanal = bloquearInicioAntesRepousoSemanal;
        _descontarPorFaltas = descontarPorFaltas;
        _direitoOriginal = periodo.DireitoDias;
        _vendidos = periodo.Vendidos;
        _saldoSemAjusteFaltas = Math.Max(0,
            periodo.Saldo - RegraFaltasClt.ObterAjusteAtual(periodo));
        _vencimento = periodo.Vencimento.Date;
        _existentes = periodo.Movimentacoes
            .Where(item => item.Tipo == TipoMovimentacao.Agendamento &&
                           item.Inicio is not null && item.Fim is not null)
            .OrderBy(item => item.Inicio)
            .Select(item => new IntervaloItem(
                new IntervaloFerias(item.Inicio!.Value, item.Fim!.Value)))
            .ToList();
        _vagasDisponiveis = Math.Max(0, 3 - _existentes.Count);

        EmployeeText.Text = periodo.Colaborador.Nome;
        ExistingRangesList.ItemsSource = _existentes;
        NewRangesList.ItemsSource = _novos;
        UnjustifiedAbsencesTextBox.Text = periodo.FaltasNaoJustificadas.ToString();
        RulesConfigurationText.Text =
            $"• Antecedência de 30 dias: {(bloquearAgendamentoMenos30Dias ? "bloqueada" : "permitida com aviso")}.\n" +
            $"• Início antes do repouso semanal: {(bloquearInicioAntesRepousoSemanal ? "bloqueado" : "permitido com aviso")}.\n" +
            $"• Desconto por faltas não justificadas: {(descontarPorFaltas ? "ligado" : "desligado")}.";

        var firstAllowed = new[]
        {
            DateTime.Today,
            periodo.Fim.Date.AddDays(1),
            bloquearAgendamentoMenos30Dias ? DateTime.Today.AddDays(30) : DateTime.Today
        }.Max();
        RangeCalendar.DisplayDateStart = firstAllowed;
        RangeCalendar.DisplayDateEnd = periodo.Vencimento.Date;
        StartDatePicker.DisplayDateStart = firstAllowed;
        StartDatePicker.DisplayDateEnd = periodo.Vencimento.Date;
        if (firstAllowed <= periodo.Vencimento.Date) RangeCalendar.DisplayDate = firstAllowed;

        var forbiddenStarts = feriados
            .SelectMany(item => new[] { item.Data.Date.AddDays(-2), item.Data.Date.AddDays(-1) })
            .Where(day => day >= firstAllowed && day <= periodo.Vencimento.Date)
            .ToHashSet();
        if (bloquearInicioAntesRepousoSemanal)
        {
            for (var day = firstAllowed; day <= periodo.Vencimento.Date; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
                    forbiddenStarts.Add(day);
            }
        }
        foreach (var day in forbiddenStarts.OrderBy(item => item))
        {
            RangeCalendar.BlackoutDates.Add(new CalendarDateRange(day));
            StartDatePicker.BlackoutDates.Add(new CalendarDateRange(day));
        }

        RefreshSummary();
    }

    public IReadOnlyList<IntervaloFerias> Intervalos =>
        _novos.Select(item => item.Intervalo).ToList();
    public int DiasAbono
    {
        get
        {
            if (string.IsNullOrEmpty(AllowanceDaysTextBox.Text)) return 0;
            return int.TryParse(AllowanceDaysTextBox.Text, out var days)
                ? days
                : int.MaxValue;
        }
    }
    public int FaltasNaoJustificadas =>
        int.TryParse(UnjustifiedAbsencesTextBox.Text, out var value) ? value : 0;

    private int DireitoAjustado => _descontarPorFaltas
        ? RegraFaltasClt.CalcularDireito(_direitoOriginal, FaltasNaoJustificadas)
        : _direitoOriginal;
    private int SaldoDisponivel => Math.Max(0,
        _saldoSemAjusteFaltas + DireitoAjustado - _direitoOriginal);
    private int MaximoAbono => Math.Max(0, (DireitoAjustado / 3) - _vendidos);

    private void CalendarChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_adjusting || RangeCalendar.SelectedDates.Count == 0) return;
        _adjusting = true;
        StartDatePicker.SelectedDate = RangeCalendar.SelectedDates.Single();
        _adjusting = false;
        EvaluateDraft();
    }

    private void StartDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_adjusting) return;
        _adjusting = true;
        RangeCalendar.SelectedDate = StartDatePicker.SelectedDate;
        if (StartDatePicker.SelectedDate is DateTime start)
            RangeCalendar.DisplayDate = start;
        _adjusting = false;
        EvaluateDraft();
    }

    private void VacationDaysPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }

    private void VacationDaysChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || _adjusting) return;
        var digits = new string(VacationDaysTextBox.Text.Where(char.IsDigit).ToArray());
        if (digits != VacationDaysTextBox.Text)
        {
            VacationDaysTextBox.Text = digits;
            VacationDaysTextBox.CaretIndex = digits.Length;
            return;
        }
        EvaluateDraft();
    }

    private void EvaluateDraft()
    {
        if (!TryGetDraft(out var requested))
        {
            RangeCountText.Text = "Informe o início e a quantidade de dias";
            AddOrUpdateButton.IsEnabled = false;
            return;
        }

        RangeCountText.Text =
            $"{requested.Dias} dia(s) corrido(s) • término em {requested.Fim:dd/MM/yyyy}";
        var error = ValidateCandidate(requested);
        ValidationText.Text = error ?? string.Empty;
        AddOrUpdateButton.IsEnabled = error is null;
    }

    private void AddOrUpdateClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetDraft(out var requested)) return;
        var error = ValidateCandidate(requested);
        if (error is not null) { ValidationText.Text = error; return; }
        if (_editing is null)
        {
            if (_novos.Count >= _vagasDisponiveis)
            {
                ValidationText.Text = "O limite de três parcelas já foi atingido.";
                return;
            }
            _novos.Add(new IntervaloItem(requested));
        }
        else
        {
            var index = _novos.IndexOf(_editing);
            _novos[index] = new IntervaloItem(requested);
            _editing = null;
            NewRangesList.SelectedItem = null;
        }
        ClearDraft();
        RefreshSummary();
    }

    private bool TryGetDraft(out IntervaloFerias requested)
    {
        requested = null!;
        if (StartDatePicker.SelectedDate is not DateTime start ||
            !int.TryParse(VacationDaysTextBox.Text, out var days) || days <= 0)
            return false;
        if (days > SaldoDisponivel)
        {
            ValidationText.Text = $"O período possui somente {SaldoDisponivel} dias disponíveis.";
            return false;
        }
        requested = new IntervaloFerias(start.Date, start.Date.AddDays(days - 1));
        return true;
    }

    private string? ValidateCandidate(IntervaloFerias requested)
    {
        if (requested.Dias < 5)
            return "Uma parcela deve possuir pelo menos 5 dias corridos.";
        if (requested.Fim > _vencimento)
            return $"A parcela deve terminar até {_vencimento:dd/MM/yyyy}.";
        if (_bloquearInicioAntesRepousoSemanal &&
            requested.Inicio.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
            return "A parcela não pode iniciar na sexta-feira ou no sábado, antes do repouso semanal.";

        var otherNewRanges = _novos
            .Where(item => item != _editing)
            .Select(item => item.Intervalo)
            .ToList();
        if (_editing is null && otherNewRanges.Count >= _vagasDisponiveis)
            return "O limite de três parcelas já foi atingido.";
        if (otherNewRanges.Any(item => Overlaps(item, requested)))
            return "As novas parcelas não podem se sobrepor.";
        if (_existentes.Any(item => Overlaps(item.Intervalo, requested)))
            return "A parcela se sobrepõe a férias já agendadas.";

        otherNewRanges.Add(requested);
        return ValidatePlan(otherNewRanges);
    }

    private string? ValidatePlan(IReadOnlyList<IntervaloFerias> newRanges)
    {
        if (DiasAbono > MaximoAbono)
            return $"O abono pecuniário está limitado a {MaximoAbono} dia(s).";

        var newDays = newRanges.Sum(item => item.Dias);
        if ((long)newDays + DiasAbono > SaldoDisponivel)
            return "A soma das parcelas e do abono excede o saldo disponível.";

        var allRanges = _existentes.Select(item => item.Intervalo)
            .Concat(newRanges).ToList();
        if (allRanges.Count > 3)
            return "O limite de três parcelas já foi atingido.";
        if (allRanges.Count > 1 && !allRanges.Any(item => item.Dias >= 14))
        {
            var remainingBalance = SaldoDisponivel - newDays - DiasAbono;
            var remainingSlots = 3 - allRanges.Count;
            if (remainingBalance < 14 || remainingSlots < 1)
                return "A divisão precisa conter uma parcela de pelo menos 14 dias.";
        }
        return null;
    }

    private void NewRangeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (NewRangesList.SelectedItem is not IntervaloItem item) return;
        _editing = item;
        AddOrUpdateButton.Content = "Atualizar parcela";
        _adjusting = true;
        StartDatePicker.SelectedDate = item.Intervalo.Inicio;
        RangeCalendar.SelectedDate = item.Intervalo.Inicio;
        RangeCalendar.DisplayDate = item.Intervalo.Inicio;
        VacationDaysTextBox.Text = item.Intervalo.Dias.ToString();
        _adjusting = false;
        EvaluateDraft();
    }

    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        if (NewRangesList.SelectedItem is not IntervaloItem item) return;
        _novos.Remove(item);
        _editing = null;
        ClearDraft();
        RefreshSummary();
    }

    private void ClearDraft()
    {
        _adjusting = true;
        RangeCalendar.SelectedDates.Clear();
        StartDatePicker.SelectedDate = null;
        VacationDaysTextBox.Text = string.Empty;
        _adjusting = false;
        AddOrUpdateButton.Content = "Adicionar parcela";
        AddOrUpdateButton.IsEnabled = false;
        RangeCountText.Text = "Informe o início e a quantidade de dias";
        ValidationText.Text = string.Empty;
    }

    private void RefreshSummary()
    {
        var used = _novos.Sum(item => item.Intervalo.Dias);
        var remaining = (long)SaldoDisponivel - used - DiasAbono;
        BalanceText.Text = $"{Math.Max(0, remaining)} dias restantes";
        SlotsText.Text = $"{_vagasDisponiveis - _novos.Count} parcela(s) disponível(is)";
        SummaryText.Text =
            $"{_novos.Count} parcela(s) • {used} dias de descanso • {DiasAbono} vendidos";
        AllowanceHelpText.Text =
            $"Máximo legal: {MaximoAbono} dia(s). O pedido do empregado deve ter sido feito no prazo legal.";
        AbsencesHelpText.Text = _descontarPorFaltas
            ? $"Regra CLT ligada: direito do período em {DireitoAjustado} dia(s)."
            : "Regra CLT desligada: registro sem desconto no saldo.";
        RulesPeriodLimitText.Text =
            $"Para este período: até {_vagasDisponiveis} nova(s) parcela(s) e " +
            $"até {MaximoAbono} dia(s) adicionais de abono.";
        ConfirmButton.IsEnabled = _novos.Count > 0 &&
            ValidatePlan(_novos.Select(item => item.Intervalo).ToList()) is null;
    }

    private void AllowanceDaysPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }

    private void AllowanceDaysChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        var digits = new string(AllowanceDaysTextBox.Text.Where(char.IsDigit).ToArray());
        if (digits != AllowanceDaysTextBox.Text)
        {
            AllowanceDaysTextBox.Text = digits;
            AllowanceDaysTextBox.CaretIndex = digits.Length;
            return;
        }

        var days = DiasAbono;
        if (days > MaximoAbono)
            ValidationText.Text = $"O abono pecuniário está limitado a {MaximoAbono} dia(s).";
        else if ((long)days + _novos.Sum(item => item.Intervalo.Dias) > SaldoDisponivel)
            ValidationText.Text = "A soma das férias e do abono excede o saldo disponível.";
        else
            ValidationText.Text = string.Empty;
        RefreshSummary();
        EvaluateDraft();
    }

    private void UnjustifiedAbsencesPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }

    private void UnjustifiedAbsencesChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        var digits = new string(UnjustifiedAbsencesTextBox.Text.Where(char.IsDigit).ToArray());
        if (digits != UnjustifiedAbsencesTextBox.Text)
        {
            UnjustifiedAbsencesTextBox.Text = string.IsNullOrEmpty(digits) ? "0" : digits;
            UnjustifiedAbsencesTextBox.CaretIndex = UnjustifiedAbsencesTextBox.Text.Length;
            return;
        }
        RefreshSummary();
        EvaluateDraft();
    }

    private static bool Overlaps(IntervaloFerias left, IntervaloFerias right) =>
        left.Inicio.Date <= right.Fim.Date && right.Inicio.Date <= left.Fim.Date;

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    private void ConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed record IntervaloItem(IntervaloFerias Intervalo)
    {
        public string Descricao =>
            $"{Intervalo.Inicio:dd/MM/yyyy} a {Intervalo.Fim:dd/MM/yyyy} • {Intervalo.Dias} dias";
    }
}
