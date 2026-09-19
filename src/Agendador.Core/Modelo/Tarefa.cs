using Agendador.Core.Cron;

namespace Agendador.Core.Modelo;

/// <summary>O que fazer quando a execução anterior ainda não terminou.</summary>
public enum Sobreposicao
{
    /// <summary>Pula a execução e agenda a próxima.</summary>
    Pular,

    /// <summary>Deixa rodar em paralelo.</summary>
    Permitir,
}

/// <summary>Estado de uma tarefa no agendador.</summary>
public enum EstadoDaTarefa
{
    /// <summary>Ativa e concorrendo às próximas janelas.</summary>
    Ativa,

    /// <summary>Pausada: continua cadastrada, mas não dispara.</summary>
    Pausada,
}

/// <summary>
/// Uma tarefa agendada. Guarda a expressão cron, o fuso em que ela deve ser
/// lida e o instante em que precisa rodar da próxima vez, que é o campo pelo
/// qual o motor procura o que está vencido.
/// </summary>
public sealed record Tarefa
{
    /// <summary>Identificador único, estável entre reinícios.</summary>
    public required string Id { get; init; }

    /// <summary>Nome legível da tarefa.</summary>
    public required string Nome { get; init; }

    /// <summary>Expressão cron que define a cadência.</summary>
    public required string Cron { get; init; }

    /// <summary>Identificador do fuso em que a expressão é lida.</summary>
    public string Fuso { get; init; } = "America/Sao_Paulo";

    /// <summary>Nome do executor registrado que trata esta tarefa.</summary>
    public required string Executor { get; init; }

    /// <summary>Conteúdo livre repassado ao executor.</summary>
    public string? Carga { get; init; }

    /// <summary>Estado atual.</summary>
    public EstadoDaTarefa Estado { get; init; } = EstadoDaTarefa.Ativa;

    /// <summary>Política de retentativa.</summary>
    public PoliticaDeRetentativa Retentativa { get; init; } = PoliticaDeRetentativa.Padrao;

    /// <summary>Como tratar uma execução que pega a anterior ainda rodando.</summary>
    public Sobreposicao Sobreposicao { get; init; } = Sobreposicao.Pular;

    /// <summary>Tempo máximo de uma execução antes de ser considerada travada.</summary>
    public TimeSpan Limite { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Instante em que a tarefa deve rodar da próxima vez.</summary>
    public DateTimeOffset? ProximaExecucao { get; init; }

    /// <summary>Instante da última execução concluída, com sucesso ou não.</summary>
    public DateTimeOffset? UltimaExecucao { get; init; }

    /// <summary>Indica se a tarefa concorre às próximas janelas.</summary>
    public bool EstaAtiva => Estado == EstadoDaTarefa.Ativa;

    /// <summary>Fuso resolvido a partir do identificador guardado.</summary>
    public TimeZoneInfo FusoResolvido()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(Fuso);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>Calcula o próximo disparo depois do instante informado.</summary>
    public DateTimeOffset? CalcularProxima(DateTimeOffset apartirDe)
        => ExpressaoCron.Analisar(Cron).ProximaExecucao(apartirDe, FusoResolvido());

    /// <summary>Devolve a tarefa com a próxima execução recalculada.</summary>
    public Tarefa ComProximaExecucao(DateTimeOffset apartirDe)
        => this with { ProximaExecucao = CalcularProxima(apartirDe) };

    /// <summary>Confere os campos e devolve a própria tarefa quando estão certos.</summary>
    public Tarefa Validar()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("A tarefa precisa de um identificador.", nameof(Id));
        }

        if (string.IsNullOrWhiteSpace(Nome))
        {
            throw new ArgumentException("A tarefa precisa de um nome.", nameof(Nome));
        }

        if (string.IsNullOrWhiteSpace(Executor))
        {
            throw new ArgumentException("A tarefa precisa apontar um executor.", nameof(Executor));
        }

        if (Limite <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Limite), "O limite precisa ser positivo.");
        }

        ExpressaoCron.Analisar(Cron);
        Retentativa.Validar();

        return this;
    }
}
