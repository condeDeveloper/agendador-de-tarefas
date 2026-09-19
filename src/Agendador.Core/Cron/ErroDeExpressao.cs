namespace Agendador.Core.Cron;

/// <summary>
/// Falha ao interpretar uma expressão cron. Guarda o trecho problemático para
/// que a mensagem aponte o campo, e não só diga que a expressão é inválida.
/// </summary>
public sealed class ErroDeExpressao : Exception
{
    /// <summary>Cria o erro apontando o trecho que não foi entendido.</summary>
    public ErroDeExpressao(string expressao, string trecho, string motivo)
        : base($"Expressão '{expressao}' inválida em '{trecho}': {motivo}")
    {
        Expressao = expressao;
        Trecho = trecho;
    }

    /// <summary>A expressão completa que foi recusada.</summary>
    public string Expressao { get; }

    /// <summary>O trecho que causou a recusa.</summary>
    public string Trecho { get; }
}
