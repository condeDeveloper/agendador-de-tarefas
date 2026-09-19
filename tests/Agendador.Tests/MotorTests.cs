using Agendador.Core.Armazenamento;
using Agendador.Core.Modelo;
using Agendador.Core.Motor;
using Agendador.Core.Tempo;

namespace Agendador.Tests;

public class MotorTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly RepositorioEmMemoria repositorio = new();
    private readonly RelogioFixo relogio = new(Inicio);
    private readonly Despachante despachante = new();
    private int chamadas;

    private MotorDoAgendador Motor(OpcoesDoMotor? opcoes = null)
        => new(repositorio, despachante, relogio, opcoes, sorteio: () => 0.5);

    private static Tarefa Tarefa(string id = "t", string cron = "*/5 * * * *") => new()
    {
        Id = id,
        Nome = $"Tarefa {id}",
        Cron = cron,
        Fuso = "UTC",
        Executor = "ok",
    };

    private void RegistrarOk() => despachante.Registrar("ok", (_, _) =>
    {
        chamadas++;
        return Task.FromResult<string?>("feito");
    });

    private void RegistrarQueFalha() => despachante.Registrar("ok", (_, _) =>
    {
        chamadas++;
        throw new InvalidOperationException("banco fora do ar");
    });

    [Fact]
    public async Task Agendar_calcula_a_proxima_execucao()
    {
        RegistrarOk();

        var tarefa = await Motor().AgendarAsync(Tarefa());

        tarefa.ProximaExecucao.Should().Be(new DateTimeOffset(2026, 9, 19, 12, 5, 0, TimeSpan.Zero));
        (await repositorio.BuscarAsync("t")).Should().NotBeNull();
    }

    [Fact]
    public async Task Agendar_recusa_executor_nao_registrado()
    {
        var acao = async () => await Motor().AgendarAsync(Tarefa());

        await acao.Should().ThrowAsync<InvalidOperationException>().WithMessage("*não está registrado*");
    }

    [Fact]
    public async Task Agendar_recusa_tarefa_invalida()
    {
        RegistrarOk();

        var acao = async () => await Motor().AgendarAsync(Tarefa(cron: "todo dia"));

        await acao.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Uma_passagem_antes_da_hora_nao_roda_nada()
    {
        RegistrarOk();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa());

        var execucoes = await motor.PassarAsync();

        execucoes.Should().BeEmpty();
        chamadas.Should().Be(0);
    }

    [Fact]
    public async Task Roda_a_tarefa_quando_a_janela_chega()
    {
        RegistrarOk();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa());

        relogio.Avancar(TimeSpan.FromMinutes(5));
        var execucoes = await motor.PassarAsync();

        chamadas.Should().Be(1);
        execucoes.Should().ContainSingle();
        execucoes[0].Estado.Should().Be(EstadoDaExecucao.Concluida);
        execucoes[0].Saida.Should().Be("feito");
        execucoes[0].Tentativa.Should().Be(1);
    }

    [Fact]
    public async Task Depois_de_rodar_agenda_a_janela_seguinte()
    {
        RegistrarOk();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa());

        relogio.Avancar(TimeSpan.FromMinutes(5));
        await motor.PassarAsync();

        var tarefa = await repositorio.BuscarAsync("t");
        tarefa!.ProximaExecucao.Should().Be(new DateTimeOffset(2026, 9, 19, 12, 10, 0, TimeSpan.Zero));
        tarefa.UltimaExecucao.Should().Be(relogio.Agora);
    }

    [Fact]
    public async Task A_mesma_janela_nao_roda_duas_vezes()
    {
        RegistrarOk();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa());

        relogio.Avancar(TimeSpan.FromMinutes(5));
        await motor.PassarAsync();
        await motor.PassarAsync();

        chamadas.Should().Be(1);
    }

    [Fact]
    public async Task Nao_roda_tarefa_pausada()
    {
        RegistrarOk();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa());
        await motor.PausarAsync("t");

        relogio.Avancar(TimeSpan.FromMinutes(10));
        await motor.PassarAsync();

        chamadas.Should().Be(0);
        (await repositorio.BuscarAsync("t"))!.ProximaExecucao.Should().BeNull();
    }

    [Fact]
    public async Task Retomar_recalcula_a_proxima_janela()
    {
        RegistrarOk();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa());
        await motor.PausarAsync("t");

        var retomada = await motor.RetomarAsync("t");

        retomada!.Estado.Should().Be(EstadoDaTarefa.Ativa);
        retomada.ProximaExecucao.Should().Be(new DateTimeOffset(2026, 9, 19, 12, 5, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Pausar_o_que_nao_existe_devolve_nulo()
    {
        (await Motor().PausarAsync("fantasma")).Should().BeNull();
    }

    [Fact]
    public async Task Disparar_agora_roda_sem_mexer_no_agendamento()
    {
        RegistrarOk();
        var motor = Motor();
        var agendada = await motor.AgendarAsync(Tarefa());

        var execucao = await motor.DispararAgoraAsync("t");

        chamadas.Should().Be(1);
        execucao.Estado.Should().Be(EstadoDaExecucao.Concluida);
        (await repositorio.BuscarAsync("t"))!.ProximaExecucao.Should().Be(agendada.ProximaExecucao);
    }

    [Fact]
    public async Task Disparar_o_que_nao_existe_lanca()
    {
        var acao = async () => await Motor().DispararAgoraAsync("fantasma");

        await acao.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Uma_falha_marca_a_execucao_e_agenda_a_retentativa()
    {
        RegistrarQueFalha();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa() with
        {
            Retentativa = new PoliticaDeRetentativa(3, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), 0),
        });

        relogio.Avancar(TimeSpan.FromMinutes(5));
        var execucoes = await motor.PassarAsync();

        execucoes[0].Estado.Should().Be(EstadoDaExecucao.Falhou);
        execucoes[0].Erro.Should().Be("banco fora do ar");

        var tarefa = await repositorio.BuscarAsync("t");
        tarefa!.ProximaExecucao.Should().Be(relogio.Agora.AddSeconds(10));
    }

    [Fact]
    public async Task A_retentativa_incrementa_o_contador_e_desiste_no_limite()
    {
        RegistrarQueFalha();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa() with
        {
            Retentativa = new PoliticaDeRetentativa(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), 0),
        });

        for (var i = 0; i < 3; i++)
        {
            relogio.Avancar(TimeSpan.FromMinutes(5));
            await motor.PassarAsync();
        }

        chamadas.Should().Be(3);

        var execucoes = await repositorio.ListarExecucoesAsync("t");
        execucoes.Select(execucao => execucao.Tentativa).Should().Equal(3, 2, 1);
        execucoes[0].Estado.Should().Be(EstadoDaExecucao.Desistiu);
    }

    [Fact]
    public async Task Depois_de_desistir_volta_para_a_cadencia_normal()
    {
        RegistrarQueFalha();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa() with { Retentativa = PoliticaDeRetentativa.SemRetentativa });

        relogio.Avancar(TimeSpan.FromMinutes(5));
        await motor.PassarAsync();

        var tarefa = await repositorio.BuscarAsync("t");
        tarefa!.ProximaExecucao.Should().Be(new DateTimeOffset(2026, 9, 19, 12, 10, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Uma_execucao_que_passa_do_limite_e_marcada_como_expirada()
    {
        despachante.Registrar("ok", async (_, cancelamento) =>
        {
            chamadas++;
            await Task.Delay(TimeSpan.FromSeconds(30), cancelamento);
            return "nunca chega aqui";
        });

        var motor = Motor();
        await motor.AgendarAsync(Tarefa() with
        {
            Limite = TimeSpan.FromMilliseconds(50),
            Retentativa = PoliticaDeRetentativa.SemRetentativa,
        });

        relogio.Avancar(TimeSpan.FromMinutes(5));
        var execucoes = await motor.PassarAsync();

        execucoes[0].Estado.Should().Be(EstadoDaExecucao.Expirou);
        execucoes[0].Erro.Should().Contain("limite de tempo");
    }

    [Fact]
    public async Task Pula_a_janela_quando_a_execucao_anterior_ainda_roda()
    {
        RegistrarOk();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa());

        // Uma execução deixada em aberto simula o processo ainda trabalhando.
        await repositorio.SalvarExecucaoAsync(new Execucao
        {
            Id = "aberta",
            TarefaId = "t",
            Agendada = Inicio,
            Inicio = Inicio,
            ReservaAte = Inicio.AddHours(1),
        });

        relogio.Avancar(TimeSpan.FromMinutes(5));
        var execucoes = await motor.PassarAsync();

        chamadas.Should().Be(0);
        execucoes[0].Estado.Should().Be(EstadoDaExecucao.Pulada);
    }

    [Fact]
    public async Task Com_sobreposicao_permitida_roda_mesmo_com_outra_em_aberto()
    {
        RegistrarOk();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa() with { Sobreposicao = Sobreposicao.Permitir });

        await repositorio.SalvarExecucaoAsync(new Execucao
        {
            Id = "aberta",
            TarefaId = "t",
            Agendada = Inicio,
            Inicio = Inicio,
            ReservaAte = Inicio.AddHours(1),
        });

        relogio.Avancar(TimeSpan.FromMinutes(5));
        await motor.PassarAsync();

        chamadas.Should().Be(1);
    }

    [Fact]
    public async Task Recolhe_execucao_abandonada_e_devolve_a_tarefa_ao_ciclo()
    {
        RegistrarOk();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa());

        await repositorio.SalvarExecucaoAsync(new Execucao
        {
            Id = "orfa",
            TarefaId = "t",
            Agendada = Inicio,
            Inicio = Inicio,
            ReservaAte = Inicio.AddSeconds(1),
        });

        relogio.Avancar(TimeSpan.FromMinutes(1));
        await motor.PassarAsync();

        var execucoes = await repositorio.ListarExecucoesAsync("t");
        execucoes.Should().ContainSingle();
        execucoes[0].Estado.Should().Be(EstadoDaExecucao.Expirou);
    }

    [Fact]
    public async Task O_lote_limita_quantas_tarefas_rodam_por_passagem()
    {
        RegistrarOk();
        var motor = Motor(new OpcoesDoMotor { TamanhoDoLote = 2 });

        for (var i = 0; i < 5; i++)
        {
            await motor.AgendarAsync(Tarefa($"t{i}"));
        }

        relogio.Avancar(TimeSpan.FromMinutes(5));
        var execucoes = await motor.PassarAsync();

        execucoes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Remover_apaga_a_tarefa()
    {
        RegistrarOk();
        var motor = Motor();
        await motor.AgendarAsync(Tarefa());

        (await motor.RemoverAsync("t")).Should().BeTrue();
        (await repositorio.BuscarAsync("t")).Should().BeNull();
    }

    [Fact]
    public async Task O_laco_para_quando_o_cancelamento_chega()
    {
        RegistrarOk();
        var motor = Motor(new OpcoesDoMotor { Intervalo = TimeSpan.FromMilliseconds(10) });

        using var cancelamento = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var laco = motor.RodarAsync(cancelamento.Token);

        await laco;

        laco.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void As_opcoes_recusam_valores_incoerentes()
    {
        var lote = () => new OpcoesDoMotor { TamanhoDoLote = 0 }.Validar();
        var intervalo = () => new OpcoesDoMotor { Intervalo = TimeSpan.Zero }.Validar();
        var folga = () => new OpcoesDoMotor { FolgaDaReserva = TimeSpan.FromSeconds(-1) }.Validar();

        lote.Should().Throw<ArgumentOutOfRangeException>();
        intervalo.Should().Throw<ArgumentOutOfRangeException>();
        folga.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void O_motor_exige_repositorio_e_despachante()
    {
        var semRepositorio = () => new MotorDoAgendador(null!, despachante);
        var semDespachante = () => new MotorDoAgendador(repositorio, null!);

        semRepositorio.Should().Throw<ArgumentNullException>();
        semDespachante.Should().Throw<ArgumentNullException>();
    }
}
