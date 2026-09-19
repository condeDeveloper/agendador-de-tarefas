using Agendador.Core.Modelo;

namespace Agendador.Core.Armazenamento;

/// <summary>
/// Armazenamento em memória. Serve para teste e para quem roda um único
/// processo e não se importa em perder o histórico no reinício. A reserva é
/// protegida por um cadeado simples, o que basta enquanto tudo está no mesmo
/// processo.
/// </summary>
public sealed class RepositorioEmMemoria : IRepositorio
{
    private readonly Dictionary<string, Tarefa> tarefas = [];
    private readonly List<Execucao> execucoes = [];
    private readonly SemaphoreSlim cadeado = new(1, 1);

    /// <inheritdoc />
    public async Task SalvarAsync(Tarefa tarefa, CancellationToken cancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(tarefa);

        await cadeado.WaitAsync(cancelamento).ConfigureAwait(false);
        try
        {
            tarefas[tarefa.Id] = tarefa;
        }
        finally
        {
            cadeado.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Tarefa?> BuscarAsync(string id, CancellationToken cancelamento = default)
    {
        await cadeado.WaitAsync(cancelamento).ConfigureAwait(false);
        try
        {
            return tarefas.GetValueOrDefault(id);
        }
        finally
        {
            cadeado.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tarefa>> ListarAsync(CancellationToken cancelamento = default)
    {
        await cadeado.WaitAsync(cancelamento).ConfigureAwait(false);
        try
        {
            return tarefas.Values.OrderBy(tarefa => tarefa.Nome, StringComparer.Ordinal).ToList();
        }
        finally
        {
            cadeado.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoverAsync(string id, CancellationToken cancelamento = default)
    {
        await cadeado.WaitAsync(cancelamento).ConfigureAwait(false);
        try
        {
            execucoes.RemoveAll(execucao => execucao.TarefaId == id);
            return tarefas.Remove(id);
        }
        finally
        {
            cadeado.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tarefa>> ReservarVencidasAsync(
        DateTimeOffset agora,
        DateTimeOffset ate,
        int limite,
        CancellationToken cancelamento = default)
    {
        await cadeado.WaitAsync(cancelamento).ConfigureAwait(false);
        try
        {
            var vencidas = tarefas.Values
                .Where(tarefa => tarefa.EstaAtiva && tarefa.ProximaExecucao is not null && tarefa.ProximaExecucao <= agora)
                .OrderBy(tarefa => tarefa.ProximaExecucao)
                .Take(limite)
                .ToList();

            // Empurrar a próxima execução aqui dentro é o que impede que a
            // mesma janela seja pega duas vezes: quem chegar depois já não vê
            // a tarefa como vencida.
            foreach (var tarefa in vencidas)
            {
                tarefas[tarefa.Id] = tarefa with { ProximaExecucao = ate };
            }

            return vencidas;
        }
        finally
        {
            cadeado.Release();
        }
    }

    /// <inheritdoc />
    public async Task SalvarExecucaoAsync(Execucao execucao, CancellationToken cancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(execucao);

        await cadeado.WaitAsync(cancelamento).ConfigureAwait(false);
        try
        {
            var indice = execucoes.FindIndex(atual => atual.Id == execucao.Id);

            if (indice >= 0)
            {
                execucoes[indice] = execucao;
            }
            else
            {
                execucoes.Add(execucao);
            }
        }
        finally
        {
            cadeado.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Execucao>> ListarExecucoesAsync(
        string tarefaId,
        int limite = 50,
        CancellationToken cancelamento = default)
    {
        await cadeado.WaitAsync(cancelamento).ConfigureAwait(false);
        try
        {
            return execucoes
                .Where(execucao => execucao.TarefaId == tarefaId)
                .OrderByDescending(execucao => execucao.Inicio)
                .Take(limite)
                .ToList();
        }
        finally
        {
            cadeado.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Execucao>> ListarAbandonadasAsync(
        DateTimeOffset agora,
        CancellationToken cancelamento = default)
    {
        await cadeado.WaitAsync(cancelamento).ConfigureAwait(false);
        try
        {
            return execucoes
                .Where(execucao => execucao.Estado == EstadoDaExecucao.Rodando
                    && execucao.ReservaAte is not null
                    && execucao.ReservaAte < agora)
                .ToList();
        }
        finally
        {
            cadeado.Release();
        }
    }
}
