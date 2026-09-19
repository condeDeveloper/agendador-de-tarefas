namespace Agendador.Core.Modelo;

/// <summary>Como terminou uma execução.</summary>
public enum EstadoDaExecucao
{
    /// <summary>Reservada pelo motor, ainda rodando.</summary>
    Rodando,

    /// <summary>Terminou sem erro.</summary>
    Concluida,

    /// <summary>Terminou com erro e ainda cabe outra tentativa.</summary>
    Falhou,

    /// <summary>Falhou e esgotou as tentativas.</summary>
    Desistiu,

    /// <summary>Estourou o limite de tempo da tarefa.</summary>
    Expirou,

    /// <summary>Não rodou porque a anterior ainda estava em andamento.</summary>
    Pulada,
}

/// <summary>
/// O registro de uma passagem pela tarefa. É o que sobra depois que o motor
/// roda: quando começou, quanto durou, em que tentativa estava e o que deu
/// errado. Serve tanto para a tela de acompanhamento quanto para decidir se
/// vale tentar de novo.
/// </summary>
public sealed record Execucao
{
    /// <summary>Identificador único da execução.</summary>
    public required string Id { get; init; }

    /// <summary>Tarefa a que a execução pertence.</summary>
    public required string TarefaId { get; init; }

    /// <summary>Janela para a qual a execução foi agendada.</summary>
    public required DateTimeOffset Agendada { get; init; }

    /// <summary>Instante em que o motor reservou a execução.</summary>
    public required DateTimeOffset Inicio { get; init; }

    /// <summary>Instante em que terminou, nulo enquanto roda.</summary>
    public DateTimeOffset? Fim { get; init; }

    /// <summary>Estado atual.</summary>
    public EstadoDaExecucao Estado { get; init; } = EstadoDaExecucao.Rodando;

    /// <summary>Número da tentativa, começando em 1.</summary>
    public int Tentativa { get; init; } = 1;

    /// <summary>Mensagem de erro, quando houve.</summary>
    public string? Erro { get; init; }

    /// <summary>Saída deixada pelo executor.</summary>
    public string? Saida { get; init; }

    /// <summary>Até quando a reserva do motor vale.</summary>
    public DateTimeOffset? ReservaAte { get; init; }

    /// <summary>Indica se a execução já terminou.</summary>
    public bool Terminou => Estado != EstadoDaExecucao.Rodando;

    /// <summary>Quanto durou, ou quanto já está durando.</summary>
    public TimeSpan Duracao(DateTimeOffset agora) => (Fim ?? agora) - Inicio;

    /// <summary>Marca a execução como concluída.</summary>
    public Execucao Concluir(DateTimeOffset agora, string? saida = null) => this with
    {
        Estado = EstadoDaExecucao.Concluida,
        Fim = agora,
        Saida = saida,
        ReservaAte = null,
    };

    /// <summary>Marca a execução como falha, desistindo ou não.</summary>
    public Execucao Falhar(DateTimeOffset agora, string erro, bool desistiu) => this with
    {
        Estado = desistiu ? EstadoDaExecucao.Desistiu : EstadoDaExecucao.Falhou,
        Fim = agora,
        Erro = erro,
        ReservaAte = null,
    };

    /// <summary>Marca a execução como estourada por tempo.</summary>
    public Execucao Expirar(DateTimeOffset agora) => this with
    {
        Estado = EstadoDaExecucao.Expirou,
        Fim = agora,
        Erro = "A execução passou do limite de tempo da tarefa.",
        ReservaAte = null,
    };
}
