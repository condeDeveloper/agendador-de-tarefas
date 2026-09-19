using Agendador.Core.Cron;

namespace Agendador.Tests;

public class CampoCronTests
{
    [Fact]
    public void O_curinga_aceita_a_faixa_inteira()
    {
        var campo = CampoCron.Analisar("*", "*", 0, 59);

        campo.EhCuringa.Should().BeTrue();
        campo.Valores().Should().HaveCount(60);
        campo.Contem(0).Should().BeTrue();
        campo.Contem(59).Should().BeTrue();
    }

    [Fact]
    public void A_interrogacao_vale_como_curinga()
    {
        CampoCron.Analisar("?", "?", 1, 31).EhCuringa.Should().BeTrue();
    }

    [Fact]
    public void Um_valor_sozinho_aceita_so_ele()
    {
        var campo = CampoCron.Analisar("30", "30", 0, 59);

        campo.Valores().Should().Equal(30);
        campo.EhCuringa.Should().BeFalse();
    }

    [Fact]
    public void A_lista_junta_os_valores()
    {
        CampoCron.Analisar("1,5,9", "1,5,9", 0, 23).Valores().Should().Equal(1, 5, 9);
    }

    [Fact]
    public void A_faixa_inclui_as_pontas()
    {
        CampoCron.Analisar("9-12", "9-12", 0, 23).Valores().Should().Equal(9, 10, 11, 12);
    }

    [Fact]
    public void O_passo_anda_de_tantos_em_tantos()
    {
        CampoCron.Analisar("*/15", "*/15", 0, 59).Valores().Should().Equal(0, 15, 30, 45);
    }

    [Fact]
    public void O_passo_dentro_de_uma_faixa_respeita_o_limite()
    {
        CampoCron.Analisar("10-40/10", "10-40/10", 0, 59).Valores().Should().Equal(10, 20, 30, 40);
    }

    [Fact]
    public void Um_valor_com_passo_vai_ate_o_fim_do_campo()
    {
        CampoCron.Analisar("45/5", "45/5", 0, 59).Valores().Should().Equal(45, 50, 55);
    }

    [Fact]
    public void A_faixa_que_da_a_volta_pega_as_duas_pontas()
    {
        CampoCron.Analisar("22-2", "22-2", 0, 23).Valores().Should().Equal(0, 1, 2, 22, 23);
    }

    [Fact]
    public void O_proximo_ou_igual_encontra_o_valor_seguinte()
    {
        var campo = CampoCron.Analisar("0,30", "0,30", 0, 59);

        campo.ProximoOuIgual(0).Should().Be(0);
        campo.ProximoOuIgual(1).Should().Be(30);
        campo.ProximoOuIgual(30).Should().Be(30);
        campo.ProximoOuIgual(31).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("60")]
    [InlineData("-1")]
    [InlineData("1-")]
    [InlineData("*/0")]
    [InlineData("*/abc")]
    [InlineData("xyz")]
    public void Recusa_trecho_invalido(string trecho)
    {
        var acao = () => CampoCron.Analisar(trecho, trecho, 0, 59);

        acao.Should().Throw<ErroDeExpressao>();
    }

    [Fact]
    public void O_erro_aponta_a_expressao_e_o_trecho()
    {
        var acao = () => CampoCron.Analisar("* 99 * * *", "99", 0, 23);

        acao.Should().Throw<ErroDeExpressao>()
            .Where(erro => erro.Expressao == "* 99 * * *" && erro.Trecho == "99");
    }

    [Fact]
    public void De_monta_o_campo_com_valores_soltos()
    {
        CampoCron.De(0, 59, 5, 10).Valores().Should().Equal(5, 10);
    }

    [Fact]
    public void De_recusa_valor_fora_da_faixa()
    {
        var acao = () => CampoCron.De(0, 23, 24);

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Todos_monta_o_campo_completo()
    {
        var campo = CampoCron.Todos(1, 12);

        campo.EhCuringa.Should().BeTrue();
        campo.Valores().Should().HaveCount(12);
    }
}
