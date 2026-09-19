using System.Globalization;

namespace Agendador.Core.Cron;

/// <summary>
/// Um campo de uma expressão cron já resolvido em um conjunto de valores. Como
/// nenhum campo passa de 60 valores possíveis, o conjunto cabe inteiro em um
/// <see cref="ulong"/>, o que torna a consulta durante o agendamento um simples
/// teste de bit.
/// </summary>
public sealed class CampoCron
{
    private readonly ulong valores;

    private CampoCron(int minimo, int maximo, ulong valores, bool curinga)
    {
        Minimo = minimo;
        Maximo = maximo;
        this.valores = valores;
        EhCuringa = curinga;
    }

    /// <summary>Menor valor aceito pelo campo.</summary>
    public int Minimo { get; }

    /// <summary>Maior valor aceito pelo campo.</summary>
    public int Maximo { get; }

    /// <summary>Indica se o campo foi escrito como <c>*</c> ou <c>?</c>.</summary>
    public bool EhCuringa { get; }

    /// <summary>Indica se o valor está no conjunto do campo.</summary>
    public bool Contem(int valor)
        => valor >= Minimo && valor <= Maximo && (valores & (1UL << valor)) != 0;

    /// <summary>Todos os valores aceitos, em ordem crescente.</summary>
    public IEnumerable<int> Valores()
    {
        for (var valor = Minimo; valor <= Maximo; valor++)
        {
            if (Contem(valor))
            {
                yield return valor;
            }
        }
    }

    /// <summary>
    /// Menor valor aceito que seja maior ou igual ao informado, ou nulo quando
    /// não há mais nenhum até o fim do campo.
    /// </summary>
    public int? ProximoOuIgual(int valor)
    {
        for (var candidato = Math.Max(valor, Minimo); candidato <= Maximo; candidato++)
        {
            if (Contem(candidato))
            {
                return candidato;
            }
        }

        return null;
    }

    /// <summary>
    /// Interpreta um campo. <paramref name="nomes"/> traz apelidos como JAN ou
    /// MON, que o cron aceita no lugar do número.
    /// </summary>
    public static CampoCron Analisar(
        string expressao,
        string trecho,
        int minimo,
        int maximo,
        IReadOnlyDictionary<string, int>? nomes = null)
    {
        if (string.IsNullOrWhiteSpace(trecho))
        {
            throw new ErroDeExpressao(expressao, trecho, "campo vazio.");
        }

        var texto = trecho.Trim();
        var curinga = texto is "*" or "?";
        ulong acumulado = 0;

        foreach (var parte in texto.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            acumulado |= AnalisarParte(expressao, parte, minimo, maximo, nomes);
        }

        if (acumulado == 0)
        {
            throw new ErroDeExpressao(expressao, trecho, "o campo não aceita nenhum valor.");
        }

        return new CampoCron(minimo, maximo, acumulado, curinga);
    }

    /// <summary>Cria um campo com um único valor, útil nos apelidos e em teste.</summary>
    public static CampoCron De(int minimo, int maximo, params int[] valores)
    {
        ArgumentNullException.ThrowIfNull(valores);

        ulong acumulado = 0;
        foreach (var valor in valores)
        {
            if (valor < minimo || valor > maximo)
            {
                throw new ArgumentOutOfRangeException(nameof(valores), $"O valor {valor} está fora do campo.");
            }

            acumulado |= 1UL << valor;
        }

        return new CampoCron(minimo, maximo, acumulado, curinga: false);
    }

    /// <summary>Cria um campo que aceita toda a faixa.</summary>
    public static CampoCron Todos(int minimo, int maximo)
    {
        ulong acumulado = 0;
        for (var valor = minimo; valor <= maximo; valor++)
        {
            acumulado |= 1UL << valor;
        }

        return new CampoCron(minimo, maximo, acumulado, curinga: true);
    }

    private static ulong AnalisarParte(
        string expressao,
        string parte,
        int minimo,
        int maximo,
        IReadOnlyDictionary<string, int>? nomes)
    {
        var passo = 1;
        var corpo = parte;

        var barra = parte.IndexOf('/', StringComparison.Ordinal);
        if (barra >= 0)
        {
            corpo = parte[..barra];
            var texto = parte[(barra + 1)..];

            if (!int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out passo) || passo <= 0)
            {
                throw new ErroDeExpressao(expressao, parte, "o passo depois da barra precisa ser um número positivo.");
            }
        }

        int inicio;
        int fim;

        if (corpo is "*" or "?")
        {
            inicio = minimo;
            fim = maximo;
        }
        else
        {
            var traco = corpo.IndexOf('-', StringComparison.Ordinal);

            if (traco > 0)
            {
                inicio = Valor(expressao, corpo[..traco], minimo, maximo, nomes);
                fim = Valor(expressao, corpo[(traco + 1)..], minimo, maximo, nomes);
            }
            else
            {
                inicio = Valor(expressao, corpo, minimo, maximo, nomes);

                // Sem faixa explícita, a barra ainda significa "daqui até o fim
                // do campo, de tantos em tantos": 15/10 em minutos dá 15, 25...
                fim = barra >= 0 ? maximo : inicio;
            }
        }

        ulong acumulado = 0;

        if (inicio <= fim)
        {
            for (var valor = inicio; valor <= fim; valor += passo)
            {
                acumulado |= 1UL << valor;
            }

            return acumulado;
        }

        // Faixa que dá a volta, como 22-2 em horas ou SEX-SEG em dias da semana.
        for (var valor = inicio; valor <= maximo; valor += passo)
        {
            acumulado |= 1UL << valor;
        }

        for (var valor = minimo; valor <= fim; valor += passo)
        {
            acumulado |= 1UL << valor;
        }

        return acumulado;
    }

    private static int Valor(
        string expressao,
        string texto,
        int minimo,
        int maximo,
        IReadOnlyDictionary<string, int>? nomes)
    {
        var limpo = texto.Trim();

        if (nomes is not null && nomes.TryGetValue(limpo, out var pelonome))
        {
            return pelonome;
        }

        if (!int.TryParse(limpo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero))
        {
            throw new ErroDeExpressao(expressao, texto, "não é um número nem um nome conhecido.");
        }

        // O domingo aceita 0 e 7; normalizar aqui evita espalhar o caso especial.
        if (numero == 7 && minimo == 0 && maximo == 6)
        {
            return 0;
        }

        if (numero < minimo || numero > maximo)
        {
            throw new ErroDeExpressao(expressao, texto, $"fora da faixa {minimo}-{maximo}.");
        }

        return numero;
    }
}
