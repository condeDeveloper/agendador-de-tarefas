using Agendador.Core.Modelo;

namespace Agendador.Tests;

public class PoliticaDeRetentativaTests
{
    private static readonly PoliticaDeRetentativa SemSorteio = new(
        MaximoDeTentativas: 5,
        AtrasoInicial: TimeSpan.FromSeconds(10),
        AtrasoMaximo: TimeSpan.FromMinutes(5),
        Embaralhamento: 0);

    [Fact]
    public void O_atraso_dobra_a_cada_tentativa()
    {
        SemSorteio.Atraso(1).Should().Be(TimeSpan.FromSeconds(10));
        SemSorteio.Atraso(2).Should().Be(TimeSpan.FromSeconds(20));
        SemSorteio.Atraso(3).Should().Be(TimeSpan.FromSeconds(40));
        SemSorteio.Atraso(4).Should().Be(TimeSpan.FromSeconds(80));
    }

    [Fact]
    public void O_atraso_para_de_crescer_no_teto()
    {
        SemSorteio.Atraso(20).Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void O_embaralhamento_mantem_o_atraso_dentro_da_faixa()
    {
        var politica = SemSorteio with { Embaralhamento = 0.5 };

        politica.Atraso(1, () => 0).Should().Be(TimeSpan.FromSeconds(5));
        politica.Atraso(1, () => 0.5).Should().Be(TimeSpan.FromSeconds(10));
        politica.Atraso(1, () => 1).Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void O_embaralhamento_nunca_gera_atraso_negativo()
    {
        var politica = SemSorteio with { Embaralhamento = 1 };

        politica.Atraso(1, () => 0).Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public void O_sorteio_padrao_fica_perto_do_atraso_nominal()
    {
        var politica = SemSorteio with { Embaralhamento = 0.2 };

        for (var i = 0; i < 50; i++)
        {
            politica.Atraso(1).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(8))
                .And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(12));
        }
    }

    [Fact]
    public void Sabe_quando_ainda_cabe_outra_tentativa()
    {
        var politica = PoliticaDeRetentativa.Padrao;

        politica.PodeTentarDeNovo(1).Should().BeTrue();
        politica.PodeTentarDeNovo(2).Should().BeTrue();
        politica.PodeTentarDeNovo(3).Should().BeFalse();
    }

    [Fact]
    public void A_politica_sem_retentativa_desiste_na_primeira()
    {
        PoliticaDeRetentativa.SemRetentativa.PodeTentarDeNovo(1).Should().BeFalse();
    }

    [Fact]
    public void Recusa_tentativa_menor_que_um()
    {
        var acao = () => SemSorteio.Atraso(0);

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 10, 300, 0.2)]
    [InlineData(3, 0, 300, 0.2)]
    [InlineData(3, 300, 10, 0.2)]
    [InlineData(3, 10, 300, 1.5)]
    public void Validar_recusa_configuracao_incoerente(
        int tentativas, int inicialEmSegundos, int maximoEmSegundos, double embaralhamento)
    {
        var politica = new PoliticaDeRetentativa(
            tentativas,
            TimeSpan.FromSeconds(inicialEmSegundos),
            TimeSpan.FromSeconds(maximoEmSegundos),
            embaralhamento);

        var acao = () => politica.Validar();

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validar_aceita_a_politica_padrao()
    {
        PoliticaDeRetentativa.Padrao.Validar().Should().Be(PoliticaDeRetentativa.Padrao);
    }

    [Fact]
    public void Usa_os_valores_padrao_quando_os_atrasos_nao_sao_informados()
    {
        var politica = new PoliticaDeRetentativa();

        politica.Inicial.Should().Be(TimeSpan.FromSeconds(30));
        politica.Teto.Should().Be(TimeSpan.FromMinutes(15));
    }
}
