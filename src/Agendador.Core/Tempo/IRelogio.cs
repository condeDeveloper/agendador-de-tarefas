namespace Agendador.Core.Tempo;

/// <summary>
/// Fonte de tempo do agendador. Existe para que o teste possa andar com o
/// relógio na mão em vez de esperar o mundo real passar.
/// </summary>
public interface IRelogio
{
    /// <summary>Instante atual em UTC.</summary>
    DateTimeOffset Agora { get; }
}

/// <summary>Relógio ligado ao do sistema operacional.</summary>
public sealed class RelogioDoSistema : IRelogio
{
    /// <summary>Instância única, já que não guarda estado.</summary>
    public static RelogioDoSistema Instancia { get; } = new();

    /// <inheritdoc />
    public DateTimeOffset Agora => DateTimeOffset.UtcNow;
}

/// <summary>Relógio controlado manualmente, para uso em teste.</summary>
public sealed class RelogioFixo : IRelogio
{
    private DateTimeOffset agora;

    /// <summary>Cria o relógio parado no instante informado.</summary>
    public RelogioFixo(DateTimeOffset inicio)
    {
        agora = inicio;
    }

    /// <inheritdoc />
    public DateTimeOffset Agora => agora;

    /// <summary>Avança o relógio.</summary>
    public void Avancar(TimeSpan quanto)
    {
        if (quanto < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(quanto), "O relógio não anda para trás.");
        }

        agora = agora.Add(quanto);
    }

    /// <summary>Move o relógio para um instante específico.</summary>
    public void Ajustar(DateTimeOffset instante) => agora = instante;
}
