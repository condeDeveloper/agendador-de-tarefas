using Agendador.Core.Cron;
using Agendador.Core.Modelo;

namespace Agendador.Tests;

public class TarefaTests
{
    private static Tarefa Exemplo(string cron = "0 3 * * *") => new()
    {
        Id = "limpeza",
        Nome = "Limpeza da base",
        Cron = cron,
        Executor = "eco",
    };

    [Fact]
    public void Nasce_ativa_com_os_valores_padrao()
    {
        var tarefa = Exemplo();

        tarefa.EstaAtiva.Should().BeTrue();
        tarefa.Estado.Should().Be(EstadoDaTarefa.Ativa);
        tarefa.Sobreposicao.Should().Be(Sobreposicao.Pular);
        tarefa.Fuso.Should().Be("America/Sao_Paulo");
        tarefa.Limite.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Calcula_a_proxima_execucao_no_fuso_da_tarefa()
    {
        var tarefa = Exemplo("0 9 * * *") with { Fuso = "UTC" };

        var proxima = tarefa.CalcularProxima(new DateTimeOffset(2026, 9, 19, 6, 0, 0, TimeSpan.Zero));

        proxima.Should().Be(new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Com_proxima_execucao_devolve_uma_copia_agendada()
    {
        var tarefa = Exemplo("0 9 * * *") with { Fuso = "UTC" };
        var agendada = tarefa.ComProximaExecucao(new DateTimeOffset(2026, 9, 19, 6, 0, 0, TimeSpan.Zero));

        agendada.ProximaExecucao.Should().NotBeNull();
        tarefa.ProximaExecucao.Should().BeNull();
    }

    [Fact]
    public void Um_fuso_desconhecido_cai_para_utc_em_vez_de_derrubar_o_agendador()
    {
        var tarefa = Exemplo() with { Fuso = "Marte/Olympus" };

        tarefa.FusoResolvido().Should().Be(TimeZoneInfo.Utc);
    }

    [Theory]
    [InlineData("", "Limpeza", "0 3 * * *", "eco")]
    [InlineData("limpeza", "", "0 3 * * *", "eco")]
    [InlineData("limpeza", "Limpeza", "0 3 * * *", "")]
    public void Validar_recusa_campos_obrigatorios_vazios(string id, string nome, string cron, string executor)
    {
        var tarefa = new Tarefa { Id = id, Nome = nome, Cron = cron, Executor = executor };

        var acao = () => tarefa.Validar();

        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validar_recusa_expressao_cron_invalida()
    {
        var acao = () => (Exemplo("todo dia as 3")).Validar();

        acao.Should().Throw<ErroDeExpressao>();
    }

    [Fact]
    public void Validar_recusa_limite_nao_positivo()
    {
        var acao = () => (Exemplo() with { Limite = TimeSpan.Zero }).Validar();

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validar_aceita_uma_tarefa_bem_formada()
    {
        Exemplo().Validar().Should().Be(Exemplo());
    }
}

public class ExecucaoTests
{
    private static Execucao Exemplo() => new()
    {
        Id = "exec-1",
        TarefaId = "limpeza",
        Agendada = new DateTimeOffset(2026, 9, 19, 3, 0, 0, TimeSpan.Zero),
        Inicio = new DateTimeOffset(2026, 9, 19, 3, 0, 1, TimeSpan.Zero),
        ReservaAte = new DateTimeOffset(2026, 9, 19, 3, 5, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Nasce_rodando()
    {
        var execucao = Exemplo();

        execucao.Estado.Should().Be(EstadoDaExecucao.Rodando);
        execucao.Terminou.Should().BeFalse();
        execucao.Tentativa.Should().Be(1);
    }

    [Fact]
    public void Concluir_fecha_a_execucao_e_solta_a_reserva()
    {
        var fim = new DateTimeOffset(2026, 9, 19, 3, 0, 9, TimeSpan.Zero);

        var concluida = Exemplo().Concluir(fim, "42 linhas apagadas");

        concluida.Estado.Should().Be(EstadoDaExecucao.Concluida);
        concluida.Terminou.Should().BeTrue();
        concluida.Saida.Should().Be("42 linhas apagadas");
        concluida.ReservaAte.Should().BeNull();
        concluida.Duracao(fim).Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void Falhar_distingue_tentar_de_novo_de_desistir()
    {
        var fim = new DateTimeOffset(2026, 9, 19, 3, 0, 9, TimeSpan.Zero);

        Exemplo().Falhar(fim, "sem conexão", desistiu: false).Estado.Should().Be(EstadoDaExecucao.Falhou);
        Exemplo().Falhar(fim, "sem conexão", desistiu: true).Estado.Should().Be(EstadoDaExecucao.Desistiu);
    }

    [Fact]
    public void Expirar_registra_o_estouro_de_tempo()
    {
        var expirada = Exemplo().Expirar(new DateTimeOffset(2026, 9, 19, 3, 6, 0, TimeSpan.Zero));

        expirada.Estado.Should().Be(EstadoDaExecucao.Expirou);
        expirada.Erro.Should().Contain("limite de tempo");
    }

    [Fact]
    public void A_duracao_de_quem_ainda_roda_conta_ate_agora()
    {
        var agora = new DateTimeOffset(2026, 9, 19, 3, 2, 1, TimeSpan.Zero);

        Exemplo().Duracao(agora).Should().Be(TimeSpan.FromMinutes(2));
    }
}
