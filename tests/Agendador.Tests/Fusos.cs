namespace Agendador.Tests;

/// <summary>
/// Fusos usados nos testes. Os identificadores IANA funcionam nos dois sistemas
/// desde o .NET 6, mas a busca ainda pode falhar em imagem sem base de fusos,
/// então o UTC serve de rede de segurança para não quebrar a suíte inteira.
/// </summary>
public static class Fusos
{
    /// <summary>Horário de Brasília, sem horário de verão desde 2019.</summary>
    public static TimeZoneInfo SaoPaulo { get; } = Buscar("America/Sao_Paulo", "E. South America Standard Time");

    /// <summary>Nova York, que ainda tem horário de verão e serve para testar as viradas.</summary>
    public static TimeZoneInfo NovaYork { get; } = Buscar("America/New_York", "Eastern Standard Time");

    /// <summary>UTC.</summary>
    public static TimeZoneInfo Utc => TimeZoneInfo.Utc;

    private static TimeZoneInfo Buscar(params string[] identificadores)
    {
        foreach (var identificador in identificadores)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(identificador);
            }
            catch (Exception erro) when (erro is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // tenta o próximo identificador
            }
        }

        return TimeZoneInfo.Utc;
    }
}
