namespace Agendador.Core.Motor;

/// <summary>
/// Guarda os executores registrados e entrega o certo para cada tarefa. Uma
/// tarefa que aponta para um executor que não existe é um erro de configuração,
/// e é melhor que ele apareça na hora do cadastro do que no meio da madrugada.
/// </summary>
public sealed class Despachante
{
    private readonly Dictionary<string, IExecutor> executores = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cria o despachante já com um conjunto de executores.</summary>
    public Despachante(IEnumerable<IExecutor>? executores = null)
    {
        foreach (var executor in executores ?? [])
        {
            Registrar(executor);
        }
    }

    /// <summary>Nomes registrados.</summary>
    public IReadOnlyCollection<string> Nomes => executores.Keys.OrderBy(nome => nome, StringComparer.Ordinal).ToList();

    /// <summary>Registra um executor, substituindo outro de mesmo nome.</summary>
    public Despachante Registrar(IExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        executores[executor.Nome] = executor;
        return this;
    }

    /// <summary>Registra um executor a partir de uma função.</summary>
    public Despachante Registrar(string nome, Func<Contexto, CancellationToken, Task<string?>> acao)
        => Registrar(new ExecutorDelegado(nome, acao));

    /// <summary>Indica se existe executor com esse nome.</summary>
    public bool Conhece(string? nome) => !string.IsNullOrWhiteSpace(nome) && executores.ContainsKey(nome);

    /// <summary>Busca o executor ou devolve nulo.</summary>
    public IExecutor? Buscar(string? nome)
        => string.IsNullOrWhiteSpace(nome) ? null : executores.GetValueOrDefault(nome);

    /// <summary>Busca o executor ou lança quando ele não existe.</summary>
    public IExecutor Exigir(string? nome)
        => Buscar(nome) ?? throw new InvalidOperationException(
            $"Não há executor registrado com o nome '{nome}'. Registrados: {string.Join(", ", Nomes)}.");
}
