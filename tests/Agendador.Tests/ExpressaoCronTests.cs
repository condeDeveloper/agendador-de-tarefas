using Agendador.Core.Cron;

namespace Agendador.Tests;

public class ExpressaoCronTests
{
    private static DateTimeOffset Em(int ano, int mes, int dia, int hora = 0, int minuto = 0, int segundo = 0)
        => new(ano, mes, dia, hora, minuto, segundo, TimeSpan.Zero);

    [Fact]
    public void Aceita_cinco_campos_fixando_o_segundo_em_zero()
    {
        var expressao = ExpressaoCron.Analisar("30 8 * * *");

        expressao.Segundo.Valores().Should().Equal(0);
        expressao.Minuto.Valores().Should().Equal(30);
        expressao.Hora.Valores().Should().Equal(8);
    }

    [Fact]
    public void Aceita_seis_campos_com_o_segundo_na_frente()
    {
        var expressao = ExpressaoCron.Analisar("15 30 8 * * *");

        expressao.Segundo.Valores().Should().Equal(15);
        expressao.Minuto.Valores().Should().Equal(30);
        expressao.Hora.Valores().Should().Equal(8);
    }

    [Theory]
    [InlineData("@hourly", "0 * * * *")]
    [InlineData("@daily", "0 0 * * *")]
    [InlineData("@midnight", "0 0 * * *")]
    [InlineData("@weekly", "0 0 * * 0")]
    [InlineData("@monthly", "0 0 1 * *")]
    [InlineData("@yearly", "0 0 1 1 *")]
    public void Traduz_os_apelidos(string apelido, string equivalente)
    {
        var doApelido = ExpressaoCron.Analisar(apelido);
        var direta = ExpressaoCron.Analisar(equivalente);

        var referencia = Em(2026, 3, 10, 13, 45);
        doApelido.ProximaExecucao(referencia, Fusos.Utc)
            .Should().Be(direta.ProximaExecucao(referencia, Fusos.Utc));
    }

    [Fact]
    public void Guarda_a_expressao_original_mesmo_quando_e_apelido()
    {
        ExpressaoCron.Analisar("@daily").Texto.Should().Be("@daily");
    }

    [Fact]
    public void Aceita_nomes_de_mes_e_de_dia_da_semana()
    {
        var expressao = ExpressaoCron.Analisar("0 0 * JAN,DEZ SEG");

        expressao.Mes.Valores().Should().Equal(1, 12);
        expressao.DiaDaSemana.Valores().Should().Equal(1);
    }

    [Fact]
    public void Aceita_nomes_em_ingles()
    {
        ExpressaoCron.Analisar("0 0 * MAR SUN").DiaDaSemana.Valores().Should().Equal(0);
    }

    [Fact]
    public void O_domingo_pode_ser_zero_ou_sete()
    {
        ExpressaoCron.Analisar("0 0 * * 7").DiaDaSemana.Valores().Should().Equal(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("* * *")]
    [InlineData("* * * * * * *")]
    [InlineData("99 * * * *")]
    [InlineData("* * * * XYZ")]
    [InlineData("0 0 * * 1#9")]
    public void Recusa_expressao_invalida(string expressao)
    {
        ExpressaoCron.TentarAnalisar(expressao, out _).Should().BeFalse();
    }

    [Fact]
    public void Analisar_lanca_com_a_expressao_no_erro()
    {
        var acao = () => ExpressaoCron.Analisar("* * *");

        acao.Should().Throw<ErroDeExpressao>().Which.Expressao.Should().Be("* * *");
    }

    [Fact]
    public void Calcula_o_proximo_disparo_no_mesmo_dia()
    {
        var proxima = ExpressaoCron.Analisar("30 8 * * *").ProximaExecucao(Em(2026, 9, 19, 6, 0), Fusos.Utc);

        proxima.Should().Be(Em(2026, 9, 19, 8, 30));
    }

    [Fact]
    public void Pula_para_o_dia_seguinte_quando_o_horario_ja_passou()
    {
        var proxima = ExpressaoCron.Analisar("30 8 * * *").ProximaExecucao(Em(2026, 9, 19, 9, 0), Fusos.Utc);

        proxima.Should().Be(Em(2026, 9, 20, 8, 30));
    }

    [Fact]
    public void Nunca_devolve_o_proprio_instante_informado()
    {
        var expressao = ExpressaoCron.Analisar("* * * * *");
        var agora = Em(2026, 9, 19, 10, 0);

        expressao.ProximaExecucao(agora, Fusos.Utc).Should().Be(Em(2026, 9, 19, 10, 1));
    }

    [Fact]
    public void Respeita_o_campo_de_segundos()
    {
        var proxima = ExpressaoCron.Analisar("*/20 * * * * *").ProximaExecucao(Em(2026, 9, 19, 10, 0, 5), Fusos.Utc);

        proxima.Should().Be(Em(2026, 9, 19, 10, 0, 20));
    }

    [Fact]
    public void Vira_a_hora_quando_acabam_os_minutos_do_campo()
    {
        var proxima = ExpressaoCron.Analisar("0,30 * * * *").ProximaExecucao(Em(2026, 9, 19, 10, 45), Fusos.Utc);

        proxima.Should().Be(Em(2026, 9, 19, 11, 0));
    }

    [Fact]
    public void Salta_para_o_proximo_mes_permitido()
    {
        var proxima = ExpressaoCron.Analisar("0 0 1 3 *").ProximaExecucao(Em(2026, 5, 10), Fusos.Utc);

        proxima.Should().Be(Em(2027, 3, 1));
    }

    [Fact]
    public void Encontra_o_dia_da_semana_pedido()
    {
        // 19/09/2026 é um sábado; a próxima segunda é dia 21.
        var proxima = ExpressaoCron.Analisar("0 9 * * SEG").ProximaExecucao(Em(2026, 9, 19, 12, 0), Fusos.Utc);

        proxima.Should().Be(Em(2026, 9, 21, 9, 0));
        proxima!.Value.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void Combina_dia_do_mes_e_dia_da_semana_com_ou()
    {
        // Com os dois campos restritos o cron dispara em qualquer um dos dois,
        // então o dia 1 entra mesmo não sendo segunda-feira.
        var expressao = ExpressaoCron.Analisar("0 0 1 * SEG");
        var proximas = expressao.ProximasExecucoes(Em(2026, 9, 26), Fusos.Utc, 3).ToList();

        proximas[0].Should().Be(Em(2026, 9, 28));
        proximas[1].Should().Be(Em(2026, 10, 1));
        proximas[2].Should().Be(Em(2026, 10, 5));
    }

    [Fact]
    public void Entende_o_L_como_ultimo_dia_do_mes()
    {
        var expressao = ExpressaoCron.Analisar("0 23 L * *");
        var proximas = expressao.ProximasExecucoes(Em(2026, 1, 15), Fusos.Utc, 3).ToList();

        proximas[0].Should().Be(Em(2026, 1, 31, 23, 0));
        proximas[1].Should().Be(Em(2026, 2, 28, 23, 0));
        proximas[2].Should().Be(Em(2026, 3, 31, 23, 0));
    }

    [Fact]
    public void Entende_a_cerquilha_como_enesima_ocorrencia()
    {
        // Terceira sexta-feira de cada mês.
        var proximas = ExpressaoCron.Analisar("0 10 * * 5#3")
            .ProximasExecucoes(Em(2026, 9, 1), Fusos.Utc, 2).ToList();

        proximas[0].Should().Be(Em(2026, 9, 18, 10, 0));
        proximas[0].DayOfWeek.Should().Be(DayOfWeek.Friday);
        proximas[1].Should().Be(Em(2026, 10, 16, 10, 0));
    }

    [Fact]
    public void Devolve_nulo_quando_a_data_nao_existe()
    {
        ExpressaoCron.Analisar("0 0 30 2 *").ProximaExecucao(Em(2026, 1, 1), Fusos.Utc).Should().BeNull();
    }

    [Fact]
    public void Enumera_varias_execucoes_seguidas()
    {
        var proximas = ExpressaoCron.Analisar("0 */6 * * *")
            .ProximasExecucoes(Em(2026, 9, 19, 1, 0), Fusos.Utc, 4).ToList();

        proximas.Should().Equal(
            Em(2026, 9, 19, 6, 0),
            Em(2026, 9, 19, 12, 0),
            Em(2026, 9, 19, 18, 0),
            Em(2026, 9, 20, 0, 0));
    }

    [Fact]
    public void Le_a_expressao_no_fuso_informado()
    {
        // 9h em São Paulo é meio-dia em UTC, já que o horário de verão acabou.
        var proxima = ExpressaoCron.Analisar("0 9 * * *")
            .ProximaExecucao(Em(2026, 9, 19, 0, 0), Fusos.SaoPaulo);

        proxima.Should().NotBeNull();
        proxima!.Value.UtcDateTime.Should().Be(new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Mostra_a_expressao_no_to_string()
    {
        ExpressaoCron.Analisar("0 9 * * 1-5").ToString().Should().Be("0 9 * * 1-5");
    }
}
