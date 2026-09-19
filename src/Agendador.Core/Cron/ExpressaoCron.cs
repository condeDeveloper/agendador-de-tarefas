using System.Globalization;

namespace Agendador.Core.Cron;

/// <summary>
/// Uma expressão cron com cinco ou seis campos, na ordem
/// <c>[segundo] minuto hora dia-do-mês mês dia-da-semana</c>. Com cinco campos
/// o segundo fica fixo em zero, que é o comportamento do cron do Unix.
/// </summary>
public sealed class ExpressaoCron
{
    private const int LimiteDeAnos = 5;

    private static readonly IReadOnlyDictionary<string, int> Meses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["JAN"] = 1, ["FEV"] = 2, ["FEB"] = 2, ["MAR"] = 3, ["ABR"] = 4, ["APR"] = 4,
        ["MAI"] = 5, ["MAY"] = 5, ["JUN"] = 6, ["JUL"] = 7, ["AGO"] = 8, ["AUG"] = 8,
        ["SET"] = 9, ["SEP"] = 9, ["OUT"] = 10, ["OCT"] = 10, ["NOV"] = 11, ["DEZ"] = 12, ["DEC"] = 12,
    };

    private static readonly IReadOnlyDictionary<string, int> DiasDaSemana = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["DOM"] = 0, ["SUN"] = 0, ["SEG"] = 1, ["MON"] = 1, ["TER"] = 2, ["TUE"] = 2,
        ["QUA"] = 3, ["WED"] = 3, ["QUI"] = 4, ["THU"] = 4, ["SEX"] = 5, ["FRI"] = 5,
        ["SAB"] = 6, ["SAT"] = 6,
    };

    private static readonly IReadOnlyDictionary<string, string> Apelidos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["@yearly"] = "0 0 1 1 *",
        ["@anually"] = "0 0 1 1 *",
        ["@annually"] = "0 0 1 1 *",
        ["@monthly"] = "0 0 1 * *",
        ["@weekly"] = "0 0 * * 0",
        ["@daily"] = "0 0 * * *",
        ["@midnight"] = "0 0 * * *",
        ["@hourly"] = "0 * * * *",
    };

    private readonly bool ultimoDiaDoMes;
    private readonly int? ocorrenciaDoDiaDaSemana;

    private ExpressaoCron(
        string texto,
        CampoCron segundo,
        CampoCron minuto,
        CampoCron hora,
        CampoCron diaDoMes,
        CampoCron mes,
        CampoCron diaDaSemana,
        bool ultimoDiaDoMes,
        int? ocorrenciaDoDiaDaSemana)
    {
        Texto = texto;
        Segundo = segundo;
        Minuto = minuto;
        Hora = hora;
        DiaDoMes = diaDoMes;
        Mes = mes;
        DiaDaSemana = diaDaSemana;
        this.ultimoDiaDoMes = ultimoDiaDoMes;
        this.ocorrenciaDoDiaDaSemana = ocorrenciaDoDiaDaSemana;
    }

    /// <summary>A expressão como foi escrita.</summary>
    public string Texto { get; }

    /// <summary>Campo dos segundos.</summary>
    public CampoCron Segundo { get; }

    /// <summary>Campo dos minutos.</summary>
    public CampoCron Minuto { get; }

    /// <summary>Campo das horas.</summary>
    public CampoCron Hora { get; }

    /// <summary>Campo do dia do mês.</summary>
    public CampoCron DiaDoMes { get; }

    /// <summary>Campo do mês.</summary>
    public CampoCron Mes { get; }

    /// <summary>Campo do dia da semana.</summary>
    public CampoCron DiaDaSemana { get; }

    /// <summary>Interpreta a expressão ou lança <see cref="ErroDeExpressao"/>.</summary>
    public static ExpressaoCron Analisar(string? expressao)
    {
        if (string.IsNullOrWhiteSpace(expressao))
        {
            throw new ErroDeExpressao(expressao ?? string.Empty, string.Empty, "expressão vazia.");
        }

        var original = expressao.Trim();
        var texto = Apelidos.TryGetValue(original, out var equivalente) ? equivalente : original;

        var campos = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (campos.Length is not (5 or 6))
        {
            throw new ErroDeExpressao(original, texto, $"são esperados 5 ou 6 campos, vieram {campos.Length}.");
        }

        var comSegundos = campos.Length == 6;
        var segundo = comSegundos
            ? CampoCron.Analisar(original, campos[0], 0, 59)
            : CampoCron.De(0, 59, 0);

        var deslocamento = comSegundos ? 1 : 0;
        var diaDoMesTexto = campos[deslocamento + 2];
        var diaDaSemanaTexto = campos[deslocamento + 4];

        var ultimo = diaDoMesTexto.Trim().Equals("L", StringComparison.OrdinalIgnoreCase);
        var (diaDaSemanaLimpo, ocorrencia) = SepararOcorrencia(original, diaDaSemanaTexto);

        return new ExpressaoCron(
            original,
            segundo,
            CampoCron.Analisar(original, campos[deslocamento + 0], 0, 59),
            CampoCron.Analisar(original, campos[deslocamento + 1], 0, 23),
            ultimo ? CampoCron.De(1, 31, 1) : CampoCron.Analisar(original, diaDoMesTexto, 1, 31),
            CampoCron.Analisar(original, campos[deslocamento + 3], 1, 12, Meses),
            CampoCron.Analisar(original, diaDaSemanaLimpo, 0, 6, DiasDaSemana),
            ultimo,
            ocorrencia);
    }

    /// <summary>Tenta interpretar a expressão sem lançar.</summary>
    public static bool TentarAnalisar(string? expressao, out ExpressaoCron resultado)
    {
        try
        {
            resultado = Analisar(expressao);
            return true;
        }
        catch (ErroDeExpressao)
        {
            resultado = null!;
            return false;
        }
    }

    /// <summary>
    /// Próximo instante em que a expressão dispara depois de
    /// <paramref name="apartirDe"/>, no fuso informado. Devolve nulo quando não
    /// há nenhuma ocorrência nos próximos cinco anos, o que acontece em datas
    /// impossíveis como 31 de fevereiro.
    /// </summary>
    public DateTimeOffset? ProximaExecucao(DateTimeOffset apartirDe, TimeZoneInfo fuso)
    {
        ArgumentNullException.ThrowIfNull(fuso);

        var local = TimeZoneInfo.ConvertTime(apartirDe, fuso).DateTime;
        var instante = new DateTime(
            local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second, DateTimeKind.Unspecified)
            .AddSeconds(1);

        var limite = instante.AddYears(LimiteDeAnos);

        while (instante < limite)
        {
            if (!Mes.Contem(instante.Month))
            {
                instante = ComecoDoProximoMes(instante);
                continue;
            }

            if (!DiaPermitido(instante))
            {
                instante = instante.Date.AddDays(1);
                continue;
            }

            var hora = Hora.ProximoOuIgual(instante.Hour);
            if (hora is null)
            {
                instante = instante.Date.AddDays(1);
                continue;
            }

            if (hora != instante.Hour)
            {
                instante = instante.Date.AddHours(hora.Value);
            }

            var minuto = Minuto.ProximoOuIgual(instante.Minute);
            if (minuto is null)
            {
                instante = instante.Date.AddHours(instante.Hour + 1);
                continue;
            }

            if (minuto != instante.Minute)
            {
                instante = instante.Date.AddHours(instante.Hour).AddMinutes(minuto.Value);
            }

            var segundo = Segundo.ProximoOuIgual(instante.Second);
            if (segundo is null)
            {
                instante = instante.Date.AddHours(instante.Hour).AddMinutes(instante.Minute + 1);
                continue;
            }

            instante = instante.Date
                .AddHours(instante.Hour)
                .AddMinutes(instante.Minute)
                .AddSeconds(segundo.Value);

            return Materializar(instante, fuso);
        }

        return null;
    }

    /// <summary>Próxima execução no fuso informado, a partir de agora.</summary>
    public DateTimeOffset? ProximaExecucao(TimeZoneInfo fuso) => ProximaExecucao(DateTimeOffset.UtcNow, fuso);

    /// <summary>As próximas <paramref name="quantidade"/> execuções seguidas.</summary>
    public IEnumerable<DateTimeOffset> ProximasExecucoes(DateTimeOffset apartirDe, TimeZoneInfo fuso, int quantidade)
    {
        var cursor = apartirDe;

        for (var i = 0; i < quantidade; i++)
        {
            var proxima = ProximaExecucao(cursor, fuso);
            if (proxima is null)
            {
                yield break;
            }

            yield return proxima.Value;
            cursor = proxima.Value;
        }
    }

    /// <inheritdoc />
    public override string ToString() => Texto;

    private static (string Campo, int? Ocorrencia) SepararOcorrencia(string expressao, string campo)
    {
        var texto = campo.Trim();
        var cerquilha = texto.IndexOf('#', StringComparison.Ordinal);

        if (cerquilha < 0)
        {
            return (texto, null);
        }

        var sufixo = texto[(cerquilha + 1)..];

        if (!int.TryParse(sufixo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ocorrencia)
            || ocorrencia is < 1 or > 5)
        {
            throw new ErroDeExpressao(expressao, campo, "a ocorrência depois de '#' precisa ir de 1 a 5.");
        }

        return (texto[..cerquilha], ocorrencia);
    }

    private static DateTime ComecoDoProximoMes(DateTime instante)
        => new DateTime(instante.Year, instante.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(1);

    // O cron clássico combina dia-do-mês e dia-da-semana com OU quando os dois
    // estão restritos, e com E quando só um deles está. Parece estranho, mas é
    // o que faz "0 0 1,15 * SEG" cair no dia 1, no dia 15 e em toda segunda.
    private bool DiaPermitido(DateTime instante)
    {
        var diaRestrito = !DiaDoMes.EhCuringa || ultimoDiaDoMes;
        var semanaRestrita = !DiaDaSemana.EhCuringa;

        var casaODia = ultimoDiaDoMes
            ? instante.Day == DateTime.DaysInMonth(instante.Year, instante.Month)
            : DiaDoMes.Contem(instante.Day);

        var casaASemana = DiaDaSemana.Contem((int)instante.DayOfWeek) && OcorrenciaCasa(instante);

        return (diaRestrito, semanaRestrita) switch
        {
            (true, true) => casaODia || casaASemana,
            (true, false) => casaODia,
            (false, true) => casaASemana,
            _ => true,
        };
    }

    private bool OcorrenciaCasa(DateTime instante)
        => ocorrenciaDoDiaDaSemana is null || (instante.Day - 1) / 7 + 1 == ocorrenciaDoDiaDaSemana;

    // Duas vezes por ano o horário local mente: na virada para o horário de
    // verão certos horários não existem, e na volta eles acontecem duas vezes.
    // No buraco a execução vai para o primeiro instante válido; na repetição
    // vale a primeira das duas passagens.
    private static DateTimeOffset Materializar(DateTime local, TimeZoneInfo fuso)
    {
        while (fuso.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var deslocamento = fuso.IsAmbiguousTime(local)
            ? fuso.GetAmbiguousTimeOffsets(local).Max()
            : fuso.GetUtcOffset(local);

        return new DateTimeOffset(local, deslocamento);
    }
}
