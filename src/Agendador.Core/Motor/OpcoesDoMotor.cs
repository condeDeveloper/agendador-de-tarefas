namespace Agendador.Core.Motor;

/// <summary>Ajustes de funcionamento do motor.</summary>
public sealed record OpcoesDoMotor
{
    /// <summary>Quantas tarefas o motor reserva por passagem.</summary>
    public int TamanhoDoLote { get; init; } = 20;

    /// <summary>Intervalo entre duas passagens quando o motor roda em laço.</summary>
    public TimeSpan Intervalo { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Folga somada ao limite da tarefa para calcular até quando a reserva
    /// vale. Sem essa folga, uma execução que termina exatamente no limite
    /// pode ser recolhida como abandonada.
    /// </summary>
    public TimeSpan FolgaDaReserva { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Confere os valores e devolve as próprias opções.</summary>
    public OpcoesDoMotor Validar()
    {
        if (TamanhoDoLote < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(TamanhoDoLote), "O lote precisa ter ao menos uma tarefa.");
        }

        if (Intervalo <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Intervalo), "O intervalo precisa ser positivo.");
        }

        if (FolgaDaReserva < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(FolgaDaReserva), "A folga não pode ser negativa.");
        }

        return this;
    }
}
