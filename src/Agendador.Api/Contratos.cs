using Agendador.Core.Modelo;

namespace Agendador.Api;

/// <summary>Corpo do cadastro de uma tarefa.</summary>
public sealed record PedidoDeTarefa(
    string Id,
    string Nome,
    string Cron,
    string Executor,
    string? Fuso = null,
    string? Carga = null,
    int? MaximoDeTentativas = null,
    int? LimiteEmSegundos = null,
    bool PermitirSobreposicao = false)
{
    /// <summary>Converte o pedido no modelo do núcleo.</summary>
    public Tarefa ParaTarefa() => new Tarefa
    {
        Id = Id,
        Nome = Nome,
        Cron = Cron,
        Executor = Executor,
        Fuso = string.IsNullOrWhiteSpace(Fuso) ? "America/Sao_Paulo" : Fuso,
        Carga = Carga,
        Retentativa = MaximoDeTentativas is null
            ? PoliticaDeRetentativa.Padrao
            : PoliticaDeRetentativa.Padrao with { MaximoDeTentativas = MaximoDeTentativas.Value },
        Limite = LimiteEmSegundos is null
            ? TimeSpan.FromMinutes(5)
            : TimeSpan.FromSeconds(LimiteEmSegundos.Value),
        Sobreposicao = PermitirSobreposicao ? Sobreposicao.Permitir : Sobreposicao.Pular,
    };
}

/// <summary>Tarefa como a API devolve.</summary>
public sealed record TarefaEmResposta(
    string Id,
    string Nome,
    string Cron,
    string Fuso,
    string Executor,
    string Estado,
    string Sobreposicao,
    int MaximoDeTentativas,
    double LimiteEmSegundos,
    DateTimeOffset? ProximaExecucao,
    DateTimeOffset? UltimaExecucao)
{
    /// <summary>Monta a resposta a partir do modelo.</summary>
    public static TarefaEmResposta De(Tarefa tarefa) => new(
        tarefa.Id,
        tarefa.Nome,
        tarefa.Cron,
        tarefa.Fuso,
        tarefa.Executor,
        tarefa.Estado.ToString(),
        tarefa.Sobreposicao.ToString(),
        tarefa.Retentativa.MaximoDeTentativas,
        tarefa.Limite.TotalSeconds,
        tarefa.ProximaExecucao,
        tarefa.UltimaExecucao);
}

/// <summary>Execução como a API devolve.</summary>
public sealed record ExecucaoEmResposta(
    string Id,
    string TarefaId,
    DateTimeOffset Agendada,
    DateTimeOffset Inicio,
    DateTimeOffset? Fim,
    string Estado,
    int Tentativa,
    string? Erro,
    string? Saida)
{
    /// <summary>Monta a resposta a partir do modelo.</summary>
    public static ExecucaoEmResposta De(Execucao execucao) => new(
        execucao.Id,
        execucao.TarefaId,
        execucao.Agendada,
        execucao.Inicio,
        execucao.Fim,
        execucao.Estado.ToString(),
        execucao.Tentativa,
        execucao.Erro,
        execucao.Saida);
}

/// <summary>Resposta da simulação de uma expressão cron.</summary>
/// <param name="Cron">A expressão consultada.</param>
/// <param name="Fuso">O fuso em que ela foi lida.</param>
/// <param name="Proximas">Os próximos disparos.</param>
public sealed record Previsao(string Cron, string Fuso, IReadOnlyList<DateTimeOffset> Proximas);
