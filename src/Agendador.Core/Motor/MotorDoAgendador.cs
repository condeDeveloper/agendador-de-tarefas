using Agendador.Core.Armazenamento;
using Agendador.Core.Modelo;
using Agendador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agendador.Core.Motor;

/// <summary>
/// O motor: a cada passagem ele reserva as tarefas vencidas, chama o executor
/// de cada uma e decide o que acontece depois — repetir a tentativa, desistir
/// ou marcar a próxima janela. Todo o estado mora no repositório, então dois
/// processos podem rodar o mesmo motor sobre o mesmo banco sem duplicar
/// execução.
/// </summary>
public sealed class MotorDoAgendador
{
    private readonly IRepositorio repositorio;
    private readonly Despachante despachante;
    private readonly IRelogio relogio;
    private readonly ILogger log;
    private readonly OpcoesDoMotor opcoes;
    private readonly Func<double>? sorteio;

    /// <summary>Monta o motor.</summary>
    public MotorDoAgendador(
        IRepositorio repositorio,
        Despachante despachante,
        IRelogio? relogio = null,
        OpcoesDoMotor? opcoes = null,
        ILogger<MotorDoAgendador>? log = null,
        Func<double>? sorteio = null)
    {
        this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        this.despachante = despachante ?? throw new ArgumentNullException(nameof(despachante));
        this.relogio = relogio ?? RelogioDoSistema.Instancia;
        this.opcoes = (opcoes ?? new OpcoesDoMotor()).Validar();
        this.log = log ?? NullLogger<MotorDoAgendador>.Instance;
        this.sorteio = sorteio;
    }

    /// <summary>Cadastra ou atualiza uma tarefa, já calculando a próxima janela.</summary>
    public async Task<Tarefa> AgendarAsync(Tarefa tarefa, CancellationToken cancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(tarefa);
        tarefa.Validar();

        if (!despachante.Conhece(tarefa.Executor))
        {
            throw new InvalidOperationException(
                $"A tarefa '{tarefa.Id}' aponta para o executor '{tarefa.Executor}', que não está registrado.");
        }

        var agendada = tarefa.EstaAtiva
            ? tarefa.ComProximaExecucao(relogio.Agora)
            : tarefa with { ProximaExecucao = null };

        await repositorio.SalvarAsync(agendada, cancelamento).ConfigureAwait(false);
        return agendada;
    }

    /// <summary>Pausa uma tarefa. Ela continua cadastrada, mas para de disparar.</summary>
    public Task<Tarefa?> PausarAsync(string id, CancellationToken cancelamento = default)
        => MudarEstado(id, EstadoDaTarefa.Pausada, cancelamento);

    /// <summary>Retoma uma tarefa pausada e recalcula a próxima janela.</summary>
    public Task<Tarefa?> RetomarAsync(string id, CancellationToken cancelamento = default)
        => MudarEstado(id, EstadoDaTarefa.Ativa, cancelamento);

    /// <summary>Remove a tarefa e o histórico dela.</summary>
    public Task<bool> RemoverAsync(string id, CancellationToken cancelamento = default)
        => repositorio.RemoverAsync(id, cancelamento);

    /// <summary>Roda a tarefa na hora, sem mexer no agendamento dela.</summary>
    public async Task<Execucao> DispararAgoraAsync(string id, CancellationToken cancelamento = default)
    {
        var tarefa = await repositorio.BuscarAsync(id, cancelamento).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Não existe tarefa com o identificador '{id}'.");

        return await Rodar(tarefa, relogio.Agora, tentativa: 1, manual: true, cancelamento).ConfigureAwait(false);
    }

    /// <summary>
    /// Uma passagem do motor: recolhe execuções abandonadas, reserva o que
    /// venceu e roda. Devolve as execuções desta passagem.
    /// </summary>
    public async Task<IReadOnlyList<Execucao>> PassarAsync(CancellationToken cancelamento = default)
    {
        var agora = relogio.Agora;

        await RecolherAbandonadas(agora, cancelamento).ConfigureAwait(false);

        var horizonte = agora + opcoes.FolgaDaReserva;
        var reservadas = await repositorio
            .ReservarVencidasAsync(agora, horizonte, opcoes.TamanhoDoLote, cancelamento)
            .ConfigureAwait(false);

        var execucoes = new List<Execucao>();

        foreach (var tarefa in reservadas)
        {
            var (janela, tentativa) = await Situacao(tarefa, agora, cancelamento).ConfigureAwait(false);

            if (tarefa.Sobreposicao == Sobreposicao.Pular
                && await EstaRodando(tarefa.Id, cancelamento).ConfigureAwait(false))
            {
                execucoes.Add(await Pular(tarefa, janela, tentativa, agora, cancelamento).ConfigureAwait(false));
                continue;
            }

            execucoes.Add(await Rodar(tarefa, janela, tentativa, manual: false, cancelamento).ConfigureAwait(false));
        }

        return execucoes;
    }

    /// <summary>Roda o motor em laço até o cancelamento.</summary>
    public async Task RodarAsync(CancellationToken cancelamento)
    {
        while (!cancelamento.IsCancellationRequested)
        {
            try
            {
                await PassarAsync(cancelamento).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
            {
                break;
            }
            catch (Exception erro)
            {
                // Uma passagem que explode não pode derrubar o motor inteiro:
                // o próximo ciclo tenta de novo.
                log.LogError(erro, "A passagem do agendador falhou.");
            }

            try
            {
                await Task.Delay(opcoes.Intervalo, cancelamento).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<Tarefa?> MudarEstado(string id, EstadoDaTarefa estado, CancellationToken cancelamento)
    {
        var tarefa = await repositorio.BuscarAsync(id, cancelamento).ConfigureAwait(false);
        if (tarefa is null)
        {
            return null;
        }

        var mudada = estado == EstadoDaTarefa.Ativa
            ? (tarefa with { Estado = estado }).ComProximaExecucao(relogio.Agora)
            : tarefa with { Estado = estado, ProximaExecucao = null };

        await repositorio.SalvarAsync(mudada, cancelamento).ConfigureAwait(false);
        return mudada;
    }

    // A tentativa em curso é deduzida do histórico: se a última execução falhou
    // e ainda cabe tentar, esta passagem é a continuação daquela janela, não
    // uma janela nova.
    private async Task<(DateTimeOffset Janela, int Tentativa)> Situacao(
        Tarefa tarefa,
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        var ultimas = await repositorio.ListarExecucoesAsync(tarefa.Id, limite: 1, cancelamento).ConfigureAwait(false);
        var ultima = ultimas.Count > 0 ? ultimas[0] : null;

        if (ultima is { Estado: EstadoDaExecucao.Falhou or EstadoDaExecucao.Expirou }
            && tarefa.Retentativa.PodeTentarDeNovo(ultima.Tentativa))
        {
            return (ultima.Agendada, ultima.Tentativa + 1);
        }

        return (tarefa.ProximaExecucao ?? agora, 1);
    }

    private async Task<bool> EstaRodando(string tarefaId, CancellationToken cancelamento)
    {
        var recentes = await repositorio.ListarExecucoesAsync(tarefaId, limite: 5, cancelamento).ConfigureAwait(false);
        return recentes.Any(execucao => execucao.Estado == EstadoDaExecucao.Rodando);
    }

    private async Task<Execucao> Pular(
        Tarefa tarefa,
        DateTimeOffset janela,
        int tentativa,
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        log.LogWarning("A tarefa {Tarefa} foi pulada: a execução anterior ainda está rodando.", tarefa.Id);

        var execucao = new Execucao
        {
            Id = Guid.NewGuid().ToString("N"),
            TarefaId = tarefa.Id,
            Agendada = janela,
            Inicio = agora,
            Fim = agora,
            Tentativa = tentativa,
            Estado = EstadoDaExecucao.Pulada,
        };

        await repositorio.SalvarExecucaoAsync(execucao, cancelamento).ConfigureAwait(false);
        await Reagendar(tarefa, agora, cancelamento).ConfigureAwait(false);

        return execucao;
    }

    private async Task<Execucao> Rodar(
        Tarefa tarefa,
        DateTimeOffset janela,
        int tentativa,
        bool manual,
        CancellationToken cancelamento)
    {
        var inicio = relogio.Agora;
        var executor = despachante.Exigir(tarefa.Executor);

        var execucao = new Execucao
        {
            Id = Guid.NewGuid().ToString("N"),
            TarefaId = tarefa.Id,
            Agendada = janela,
            Inicio = inicio,
            Tentativa = tentativa,
            Estado = EstadoDaExecucao.Rodando,
            ReservaAte = inicio + tarefa.Limite + opcoes.FolgaDaReserva,
        };

        await repositorio.SalvarExecucaoAsync(execucao, cancelamento).ConfigureAwait(false);

        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
        prazo.CancelAfter(tarefa.Limite);

        Execucao terminada;

        try
        {
            var saida = await executor
                .ExecutarAsync(new Contexto(tarefa, janela, tentativa), prazo.Token)
                .ConfigureAwait(false);

            terminada = execucao.Concluir(relogio.Agora, saida);
        }
        catch (OperationCanceledException) when (prazo.IsCancellationRequested && !cancelamento.IsCancellationRequested)
        {
            log.LogWarning("A tarefa {Tarefa} passou do limite de {Limite}.", tarefa.Id, tarefa.Limite);
            terminada = execucao.Expirar(relogio.Agora);
        }
        catch (Exception erro) when (erro is not OperationCanceledException)
        {
            var desistiu = !tarefa.Retentativa.PodeTentarDeNovo(tentativa);
            log.LogError(erro, "A tarefa {Tarefa} falhou na tentativa {Tentativa}.", tarefa.Id, tentativa);
            terminada = execucao.Falhar(relogio.Agora, erro.Message, desistiu);
        }

        await repositorio.SalvarExecucaoAsync(terminada, cancelamento).ConfigureAwait(false);

        if (!manual)
        {
            await Reagendar(tarefa, terminada, cancelamento).ConfigureAwait(false);
        }

        return terminada;
    }

    private Task Reagendar(Tarefa tarefa, Execucao execucao, CancellationToken cancelamento)
    {
        var agora = relogio.Agora;
        var falhou = execucao.Estado is EstadoDaExecucao.Falhou or EstadoDaExecucao.Expirou;

        if (falhou && tarefa.Retentativa.PodeTentarDeNovo(execucao.Tentativa))
        {
            var atraso = tarefa.Retentativa.Atraso(execucao.Tentativa, sorteio);
            var repetida = tarefa with { ProximaExecucao = agora + atraso, UltimaExecucao = agora };

            log.LogInformation(
                "A tarefa {Tarefa} vai tentar de novo em {Atraso}.", tarefa.Id, atraso);

            return repositorio.SalvarAsync(repetida, cancelamento);
        }

        return Reagendar(tarefa, agora, cancelamento);
    }

    private Task Reagendar(Tarefa tarefa, DateTimeOffset agora, CancellationToken cancelamento)
    {
        var seguinte = tarefa.ComProximaExecucao(agora) with { UltimaExecucao = agora };
        return repositorio.SalvarAsync(seguinte, cancelamento);
    }

    // Se o processo morrer no meio de uma execução, a linha fica marcada como
    // rodando para sempre e a tarefa nunca mais é reservada. Recolher o que
    // passou da reserva devolve essas tarefas ao ciclo.
    private async Task RecolherAbandonadas(DateTimeOffset agora, CancellationToken cancelamento)
    {
        var abandonadas = await repositorio.ListarAbandonadasAsync(agora, cancelamento).ConfigureAwait(false);

        foreach (var execucao in abandonadas)
        {
            log.LogWarning("A execução {Execucao} da tarefa {Tarefa} foi recolhida por abandono.",
                execucao.Id, execucao.TarefaId);

            await repositorio.SalvarExecucaoAsync(execucao.Expirar(agora), cancelamento).ConfigureAwait(false);

            var tarefa = await repositorio.BuscarAsync(execucao.TarefaId, cancelamento).ConfigureAwait(false);
            if (tarefa is not null && tarefa.EstaAtiva)
            {
                await Reagendar(tarefa, agora, cancelamento).ConfigureAwait(false);
            }
        }
    }
}
