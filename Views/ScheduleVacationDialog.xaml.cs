using FeriasCampos.Properties;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FeriasCampos.Models;
using FeriasCampos.Services;

namespace FeriasCampos.Views;

public partial class ScheduleVacationDialog : Window
{
    private decimal _saldoSemAjusteFaltas;
    private int _vagasDisponiveis;
    private readonly bool _bloquearInicioAntesRepousoSemanal;
    private readonly ObservableCollection<IntervaloItem> _existentes = [];
    private readonly List<long> _excluirAgendamentos = [];
    public IReadOnlyList<long> ExcluirAgendamentos => _excluirAgendamentos;
    private readonly bool _descontarPorFaltas;
    private readonly int _direitoOriginal;
    private readonly decimal _vendidos;
    private readonly DateTime _vencimento;
    private readonly bool _hasAvailableDates;
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
        _existentes = new(periodo.Movimentacoes
            .Where(item => item.Tipo == TipoMovimentacao.Agendamento &&
                           item.Inicio is not null && item.Fim is not null)
            .OrderBy(item => item.Inicio)
            .Select(item => new IntervaloItem(
                new IntervaloFerias(item.Inicio!.Value, item.Fim!.Value), item.Id, -item.Dias))
            .ToList());
        _vagasDisponiveis = Math.Max(0, 3 - _existentes.Count);

        EmployeeText.Text = periodo.Colaborador.Nome;
        ExistingRangesList.ItemsSource = _existentes;
        NewRangesList.ItemsSource = _novos;
        UnjustifiedAbsencesTextBox.Text = periodo.FaltasNaoJustificadas.ToString();
        RulesConfigurationText.Text =
            string.Format(ScreenTexts.ScheduleVacationDialog_AntecedenciaDe30Dias, (bloquearAgendamentoMenos30Dias ? ScreenTexts.ScheduleVacationDialog_Bloqueada : ScreenTexts.ScheduleVacationDialog_PermitidaComAviso)) +
            string.Format(ScreenTexts.ScheduleVacationDialog_InicioAntesDoRepousoSemanal, (bloquearInicioAntesRepousoSemanal ? ScreenTexts.ScheduleVacationDialog_Bloqueado : ScreenTexts.ScheduleVacationDialog_PermitidoComAviso)) +
            string.Format(ScreenTexts.ScheduleVacationDialog_DescontoPorFaltasNaoJustificadas, (descontarPorFaltas ? ScreenTexts.ScheduleVacationDialog_Ligado : ScreenTexts.ScheduleVacationDialog_Desligado));

        var firstAllowed = new[]
        {
            DateTime.Today,
            periodo.Fim.Date.AddDays(1),
            bloquearAgendamentoMenos30Dias ? DateTime.Today.AddDays(30) : DateTime.Today
        }.Max();
        _hasAvailableDates = firstAllowed <= _vencimento;
        AvailableDatesText.Text = _hasAvailableDates
            ? string.Format(ScreenTexts.ScheduleVacationDialog_DatasDisponiveisParaFeriasA, firstAllowed, _vencimento)
            : ScreenTexts.ScheduleVacationDialog_SemDatasDisponiveisParaAgendamentoNestePeriodo;
        VacationDeadlineText.Text = string.Format(ScreenTexts.ScheduleVacationDialog_TodasAsParcelasDevemTerminarAte, _vencimento);
        RangeCalendar.IsEnabled = _hasAvailableDates;
        StartDatePicker.IsEnabled = _hasAvailableDates;
        VacationDaysTextBox.IsEnabled = _hasAvailableDates;
        if (_hasAvailableDates)
        {
            RangeCalendar.DisplayDateStart = firstAllowed;
            RangeCalendar.DisplayDateEnd = _vencimento;
            StartDatePicker.DisplayDateStart = firstAllowed;
            StartDatePicker.DisplayDateEnd = _vencimento;
            RangeCalendar.DisplayDate = firstAllowed;
        }

        var forbiddenStarts = CalendarioFeriados.IniciosProibidos(firstAllowed, periodo.Vencimento.Date, feriados);
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
    private decimal SaldoDisponivel => Math.Max(0,
        _saldoSemAjusteFaltas + DireitoAjustado - _direitoOriginal);
    private decimal MaximoAbono => Math.Max(0, (DireitoAjustado / 3) - _vendidos);

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
            RangeCountText.Text = ScreenTexts.ScheduleVacationDialog_InformeOInicioEAQuantidadeDeDias;
            AddOrUpdateButton.IsEnabled = false;
            return;
        }

        RangeCountText.Text =
            string.Format(ScreenTexts.ScheduleVacationDialog_DiaSCorridoSTerminoEm, requested.Dias, requested.Fim);
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
                ValidationText.Text = ScreenTexts.ScheduleVacationDialog_OLimiteDeTresParcelasJaFoiAtingido;
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
        if (!_hasAvailableDates) return false;
        if (StartDatePicker.SelectedDate is not DateTime start ||
            !int.TryParse(VacationDaysTextBox.Text, out var days) || days <= 0)
            return false;
        if (days > SaldoDisponivel)
        {
            ValidationText.Text = string.Format(ScreenTexts.ScheduleVacationDialog_OPeriodoPossuiSomenteDiasDisponiveis, SaldoDisponivel);
            return false;
        }
        requested = new IntervaloFerias(start.Date, start.Date.AddDays(days - 1));
        return true;
    }

    private string? ValidateCandidate(IntervaloFerias requested)
    {
        if (requested.Dias < 5)
            return ScreenTexts.ScheduleVacationDialog_UmaParcelaDevePossuirPeloMenos5Dias;
        if (requested.Fim > _vencimento)
            return string.Format(ScreenTexts.ScheduleVacationDialog_AParcelaDeveTerminarAte, _vencimento);
        if (_bloquearInicioAntesRepousoSemanal &&
            requested.Inicio.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
            return ScreenTexts.ScheduleVacationDialog_AParcelaNaoPodeIniciarNaSextaFeira;

        var otherNewRanges = _novos
            .Where(item => item != _editing)
            .Select(item => item.Intervalo)
            .ToList();
        if (_editing is null && otherNewRanges.Count >= _vagasDisponiveis)
            return ScreenTexts.ScheduleVacationDialog_OLimiteDeTresParcelasJaFoiAtingido;
        if (otherNewRanges.Any(item => Overlaps(item, requested)))
            return ScreenTexts.ScheduleVacationDialog_AsNovasParcelasNaoPodemSeSobrepor;
        if (_existentes.Any(item => Overlaps(item.Intervalo, requested)))
            return ScreenTexts.ScheduleVacationDialog_AParcelaSeSobrepoeAFeriasJaAgendadas;

        otherNewRanges.Add(requested);
        return ValidatePlan(otherNewRanges);
    }

    private string? ValidatePlan(IReadOnlyList<IntervaloFerias> newRanges)
    {
        if (DiasAbono > MaximoAbono)
            return string.Format(ScreenTexts.ScheduleVacationDialog_OAbonoPecuniarioEstaLimitadoADiaS, MaximoAbono);

        var newDays = newRanges.Sum(item => item.Dias);
        if ((long)newDays + DiasAbono > SaldoDisponivel)
            return ScreenTexts.ScheduleVacationDialog_ASomaDasParcelasEDoAbonoExcede;

        var allRanges = _existentes.Select(item => item.Intervalo)
            .Concat(newRanges).ToList();
        if (allRanges.Count > 3)
            return ScreenTexts.ScheduleVacationDialog_OLimiteDeTresParcelasJaFoiAtingido;
        if (allRanges.Count > 1 && !allRanges.Any(item => item.Dias >= 14))
        {
            var remainingBalance = SaldoDisponivel - newDays - DiasAbono;
            var remainingSlots = 3 - allRanges.Count;
            if (remainingBalance < 14 || remainingSlots < 1)
                return ScreenTexts.ScheduleVacationDialog_ADivisaoPrecisaConterUmaParcelaDePelo;
        }
        return null;
    }

    private void NewRangeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (NewRangesList.SelectedItem is not IntervaloItem item) return;
        _editing = item;
        AddOrUpdateButton.Content = ScreenTexts.ScheduleVacationDialog_AtualizarParcela;
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

    private void ExistingRangeSelected(object sender, SelectionChangedEventArgs e)
    {
        RemoveExistingButton.IsEnabled = ExistingRangesList.SelectedItem is IntervaloItem item &&
            item.Intervalo.Inicio.Date > DateTime.Today;
    }

    private void RemoveExistingClick(object sender, RoutedEventArgs e)
    {
        if (ExistingRangesList.SelectedItem is not IntervaloItem item ||
            item.Intervalo.Inicio.Date <= DateTime.Today) return;
        _excluirAgendamentos.Add(item.Id);
        _existentes.Remove(item);
        _saldoSemAjusteFaltas += item.DiasSaldo;
        _vagasDisponiveis++;
        RefreshSummary();
        EvaluateDraft();
    }

    private void ClearDraft()
    {
        _adjusting = true;
        RangeCalendar.SelectedDates.Clear();
        StartDatePicker.SelectedDate = null;
        VacationDaysTextBox.Text = string.Empty;
        _adjusting = false;
        AddOrUpdateButton.Content = ScreenTexts.ScheduleVacationDialog_AdicionarParcela;
        AddOrUpdateButton.IsEnabled = false;
        RangeCountText.Text = ScreenTexts.ScheduleVacationDialog_InformeOInicioEAQuantidadeDeDias;
        ValidationText.Text = string.Empty;
    }

    private void RefreshSummary()
    {
        var used = _novos.Sum(item => item.Intervalo.Dias);
        var remaining = SaldoDisponivel - used - DiasAbono;
        BalanceText.Text = string.Format(ScreenTexts.ScheduleVacationDialog_DiasRestantes, Math.Max(0, remaining));
        SlotsText.Text = string.Format(ScreenTexts.ScheduleVacationDialog_ParcelaSDisponivelIs, _vagasDisponiveis - _novos.Count);
        SummaryText.Text =
            string.Format(ScreenTexts.ScheduleVacationDialog_ParcelaSDiasDeDescansoVendidos, _novos.Count, used, DiasAbono);
        AllowanceHelpText.Text =
            string.Format(ScreenTexts.ScheduleVacationDialog_MaximoLegalDiaSOPedidoDoEmpregado, MaximoAbono);
        AbsencesHelpText.Text = _descontarPorFaltas
            ? string.Format(ScreenTexts.ScheduleVacationDialog_RegraCLTLigadaDireitoDoPeriodoEmDia, DireitoAjustado)
            : ScreenTexts.ScheduleVacationDialog_RegraCLTDesligadaRegistroSemDescontoNoSaldo;
        RulesPeriodLimitText.Text =
            string.Format(ScreenTexts.ScheduleVacationDialog_ParaEstePeriodoAteNovaSParcelaS, _vagasDisponiveis) +
            string.Format(ScreenTexts.ScheduleVacationDialog_AteDiaSAdicionaisDeAbono, MaximoAbono);
        ConfirmButton.Content = _excluirAgendamentos.Count > 0
            ? ScreenTexts.ScheduleVacationDialog_ConfirmarAlteracoes
            : ScreenTexts.ScheduleVacationDialog_ConfirmarParcelas;
        ConfirmButton.IsEnabled = (_excluirAgendamentos.Count > 0 && _novos.Count == 0 && DiasAbono == 0) ||
            (_hasAvailableDates && _novos.Count > 0 &&
             ValidatePlan(_novos.Select(item => item.Intervalo).ToList()) is null);
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
            ValidationText.Text = string.Format(ScreenTexts.ScheduleVacationDialog_OAbonoPecuniarioEstaLimitadoADiaS, MaximoAbono);
        else if ((long)days + _novos.Sum(item => item.Intervalo.Dias) > SaldoDisponivel)
            ValidationText.Text = ScreenTexts.ScheduleVacationDialog_ASomaDasFeriasEDoAbonoExcede;
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

    private sealed record IntervaloItem(IntervaloFerias Intervalo, long Id = 0, decimal DiasSaldo = 0)
    {
        public string Descricao =>
            string.Format(ScreenTexts.ScheduleVacationDialog_ADias, Intervalo.Inicio, Intervalo.Fim, Intervalo.Dias);
    }
}
