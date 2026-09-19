using Agendador.Core.Modelo;

namespace Agendador.Core.Armazenamento;

/// <summary>
/// Onde as tarefas e as execuções ficam guardadas. O contrato é pensado para
/// que mais de um processo possa apontar para o mesmo armazenamento: a reserva
/// de uma tarefa é uma operação condicional, e só um dos concorrentes ganha.
/// </summary>
public interface IRepositorio
{
    /// <summary>Grava uma tarefa nova ou atualiza a existente.</summary>
    Task SalvarAsync(Tarefa tarefa, CancellationToken cancelamento = default);

    /// <summary>Busca uma tarefa pelo identificador.</summary>
    Task<Tarefa?> BuscarAsync(string id, CancellationToken cancelamento = default);

    /// <summary>Lista todas as tarefas cadastradas.</summary>
    Task<IReadOnlyList<Tarefa>> ListarAsync(CancellationToken cancelamento = default);

    /// <summary>Remove uma tarefa e devolve se ela existia.</summary>
    Task<bool> RemoverAsync(string id, CancellationToken cancelamento = default);

    /// <summary>
    /// Reserva, de forma atômica, as tarefas ativas cuja próxima execução já
    /// venceu. Quem reserva ganha o direito de rodar até <paramref name="ate"/>.
    /// </summary>
    Task<IReadOnlyList<Tarefa>> ReservarVencidasAsync(
        DateTimeOffset agora,
        DateTimeOffset ate,
        int limite,
        CancellationToken cancelamento = default);

    /// <summary>Grava uma execução nova ou atualiza a existente.</summary>
    Task SalvarExecucaoAsync(Execucao execucao, CancellationToken cancelamento = default);

    /// <summary>Lista as execuções de uma tarefa, da mais recente para a mais antiga.</summary>
    Task<IReadOnlyList<Execucao>> ListarExecucoesAsync(
        string tarefaId,
        int limite = 50,
        CancellationToken cancelamento = default);

    /// <summary>Execuções ainda marcadas como rodando cuja reserva já venceu.</summary>
    Task<IReadOnlyList<Execucao>> ListarAbandonadasAsync(
        DateTimeOffset agora,
        CancellationToken cancelamento = default);
}
