using Agendador.Core.Modelo;

namespace Agendador.Core.Motor;

/// <summary>O que o executor recebe quando o motor chama.</summary>
/// <param name="Tarefa">A tarefa que disparou.</param>
/// <param name="Janela">A janela para a qual a execução foi agendada.</param>
/// <param name="Tentativa">Número da tentativa, começando em 1.</param>
public readonly record struct Contexto(Tarefa Tarefa, DateTimeOffset Janela, int Tentativa)
{
    /// <summary>Conteúdo livre configurado na tarefa.</summary>
    public string? Carga => Tarefa.Carga;

    /// <summary>Indica se esta já não é a primeira passagem pela janela.</summary>
    public bool EhRetentativa => Tentativa > 1;
}

/// <summary>
/// O trabalho de verdade. O motor só sabe quando chamar; o que acontece
/// depois é problema de quem implementa.
/// </summary>
public interface IExecutor
{
    /// <summary>Nome pelo qual a tarefa aponta para este executor.</summary>
    string Nome { get; }

    /// <summary>
    /// Roda a tarefa. Devolver texto é opcional e vira a saída registrada na
    /// execução; lançar exceção marca a tentativa como falha.
    /// </summary>
    Task<string?> ExecutarAsync(Contexto contexto, CancellationToken cancelamento);
}

/// <summary>Executor montado a partir de uma função, prático em teste e em cadastro rápido.</summary>
public sealed class ExecutorDelegado : IExecutor
{
    private readonly Func<Contexto, CancellationToken, Task<string?>> acao;

    /// <summary>Cria o executor com o nome e a função informados.</summary>
    public ExecutorDelegado(string nome, Func<Contexto, CancellationToken, Task<string?>> acao)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("O executor precisa de um nome.", nameof(nome));
        }

        Nome = nome;
        this.acao = acao ?? throw new ArgumentNullException(nameof(acao));
    }

    /// <inheritdoc />
    public string Nome { get; }

    /// <inheritdoc />
    public Task<string?> ExecutarAsync(Contexto contexto, CancellationToken cancelamento)
        => acao(contexto, cancelamento);
}
