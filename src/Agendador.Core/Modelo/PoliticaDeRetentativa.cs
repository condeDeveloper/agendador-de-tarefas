namespace Agendador.Core.Modelo;

/// <summary>
/// Quanto esperar entre uma tentativa e outra quando a tarefa falha. O atraso
/// dobra a cada tentativa até o teto, e recebe um embaralhamento proporcional
/// para que várias tarefas que falharam juntas não voltem todas no mesmo
/// instante e derrubem de novo o que já estava com problema.
/// </summary>
/// <param name="MaximoDeTentativas">Quantas tentativas no total, incluindo a primeira.</param>
/// <param name="AtrasoInicial">Espera depois da primeira falha.</param>
/// <param name="AtrasoMaximo">Teto da espera.</param>
/// <param name="Embaralhamento">Fração do atraso sorteada, de 0 a 1.</param>
public sealed record PoliticaDeRetentativa(
    int MaximoDeTentativas = 3,
    TimeSpan? AtrasoInicial = null,
    TimeSpan? AtrasoMaximo = null,
    double Embaralhamento = 0.2)
{
    /// <summary>Política usada quando a tarefa não define a sua.</summary>
    public static PoliticaDeRetentativa Padrao { get; } = new();

    /// <summary>Política que não tenta de novo.</summary>
    public static PoliticaDeRetentativa SemRetentativa { get; } = new(MaximoDeTentativas: 1);

    /// <summary>Espera depois da primeira falha.</summary>
    public TimeSpan Inicial => AtrasoInicial ?? TimeSpan.FromSeconds(30);

    /// <summary>Teto da espera entre tentativas.</summary>
    public TimeSpan Teto => AtrasoMaximo ?? TimeSpan.FromMinutes(15);

    /// <summary>Indica se ainda cabe outra tentativa depois da informada.</summary>
    public bool PodeTentarDeNovo(int tentativa) => tentativa < MaximoDeTentativas;

    /// <summary>
    /// Atraso antes da tentativa seguinte à informada, já com o embaralhamento
    /// aplicado. O sorteio é injetável para que o teste possa fixá-lo.
    /// </summary>
    public TimeSpan Atraso(int tentativa, Func<double>? sorteio = null)
    {
        if (tentativa < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tentativa), "A contagem de tentativas começa em 1.");
        }

        var expoente = Math.Min(tentativa - 1, 30);
        var bruto = Inicial.TotalMilliseconds * Math.Pow(2, expoente);
        var limitado = Math.Min(bruto, Teto.TotalMilliseconds);

        if (Embaralhamento <= 0)
        {
            return TimeSpan.FromMilliseconds(limitado);
        }

        var fracao = Math.Clamp(Embaralhamento, 0, 1);
        var sorteado = (sorteio ?? Random.Shared.NextDouble)();
        var variacao = limitado * fracao * (sorteado * 2 - 1);

        return TimeSpan.FromMilliseconds(Math.Max(0, limitado + variacao));
    }

    /// <summary>Valida os valores da política.</summary>
    public PoliticaDeRetentativa Validar()
    {
        if (MaximoDeTentativas < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximoDeTentativas), "Precisa haver ao menos uma tentativa.");
        }

        if (Inicial <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(AtrasoInicial), "O atraso inicial precisa ser positivo.");
        }

        if (Teto < Inicial)
        {
            throw new ArgumentOutOfRangeException(nameof(AtrasoMaximo), "O teto não pode ser menor que o atraso inicial.");
        }

        if (Embaralhamento is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Embaralhamento), "O embaralhamento vai de 0 a 1.");
        }

        return this;
    }
}
