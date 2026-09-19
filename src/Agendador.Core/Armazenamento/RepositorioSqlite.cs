using System.Globalization;
using Agendador.Core.Modelo;
using Microsoft.Data.Sqlite;

namespace Agendador.Core.Armazenamento;

/// <summary>
/// Armazenamento em SQLite. O ponto central é a reserva: o <c>UPDATE</c>
/// condicional com <c>RETURNING</c> garante que, mesmo com vários processos
/// apontando para o mesmo arquivo, cada janela de execução saia para um só.
/// </summary>
public sealed class RepositorioSqlite : IRepositorio, IDisposable
{
    private readonly SqliteConnection conexao;

    /// <summary>Abre o banco no caminho informado e cria o esquema se faltar.</summary>
    public RepositorioSqlite(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho))
        {
            throw new ArgumentException("Informe o caminho do banco.", nameof(caminho));
        }

        var emMemoria = caminho == ":memory:";

        conexao = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = caminho,
            Mode = emMemoria ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        conexao.Open();
        CriarEsquema();
    }

    /// <summary>
    /// Abre um banco só em memória, que vive enquanto a conexão viver. Como o
    /// objeto segura a conexão aberta, o banco dura o tempo do repositório.
    /// </summary>
    public static RepositorioSqlite EmMemoria() => new(":memory:");

    /// <inheritdoc />
    public async Task SalvarAsync(Tarefa tarefa, CancellationToken cancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(tarefa);

        await using var comando = conexao.CreateCommand();
        comando.CommandText = """
            INSERT INTO tarefas (
                id, nome, cron, fuso, executor, carga, estado, sobreposicao,
                limite_segundos, maximo_tentativas, atraso_inicial_segundos,
                atraso_maximo_segundos, embaralhamento, proxima_execucao, ultima_execucao)
            VALUES (
                $id, $nome, $cron, $fuso, $executor, $carga, $estado, $sobreposicao,
                $limite, $tentativas, $inicial, $maximo, $embaralhamento, $proxima, $ultima)
            ON CONFLICT(id) DO UPDATE SET
                nome = excluded.nome,
                cron = excluded.cron,
                fuso = excluded.fuso,
                executor = excluded.executor,
                carga = excluded.carga,
                estado = excluded.estado,
                sobreposicao = excluded.sobreposicao,
                limite_segundos = excluded.limite_segundos,
                maximo_tentativas = excluded.maximo_tentativas,
                atraso_inicial_segundos = excluded.atraso_inicial_segundos,
                atraso_maximo_segundos = excluded.atraso_maximo_segundos,
                embaralhamento = excluded.embaralhamento,
                proxima_execucao = excluded.proxima_execucao,
                ultima_execucao = excluded.ultima_execucao;
            """;

        comando.Parameters.AddWithValue("$id", tarefa.Id);
        comando.Parameters.AddWithValue("$nome", tarefa.Nome);
        comando.Parameters.AddWithValue("$cron", tarefa.Cron);
        comando.Parameters.AddWithValue("$fuso", tarefa.Fuso);
        comando.Parameters.AddWithValue("$executor", tarefa.Executor);
        comando.Parameters.AddWithValue("$carga", (object?)tarefa.Carga ?? DBNull.Value);
        comando.Parameters.AddWithValue("$estado", tarefa.Estado.ToString());
        comando.Parameters.AddWithValue("$sobreposicao", tarefa.Sobreposicao.ToString());
        comando.Parameters.AddWithValue("$limite", tarefa.Limite.TotalSeconds);
        comando.Parameters.AddWithValue("$tentativas", tarefa.Retentativa.MaximoDeTentativas);
        comando.Parameters.AddWithValue("$inicial", tarefa.Retentativa.Inicial.TotalSeconds);
        comando.Parameters.AddWithValue("$maximo", tarefa.Retentativa.Teto.TotalSeconds);
        comando.Parameters.AddWithValue("$embaralhamento", tarefa.Retentativa.Embaralhamento);
        comando.Parameters.AddWithValue("$proxima", Texto(tarefa.ProximaExecucao));
        comando.Parameters.AddWithValue("$ultima", Texto(tarefa.UltimaExecucao));

        await comando.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Tarefa?> BuscarAsync(string id, CancellationToken cancelamento = default)
    {
        await using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT * FROM tarefas WHERE id = $id;";
        comando.Parameters.AddWithValue("$id", id);

        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);

        return await leitor.ReadAsync(cancelamento).ConfigureAwait(false) ? LerTarefa(leitor) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tarefa>> ListarAsync(CancellationToken cancelamento = default)
    {
        await using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT * FROM tarefas ORDER BY nome;";

        return await LerTarefas(comando, cancelamento).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoverAsync(string id, CancellationToken cancelamento = default)
    {
        await using var comando = conexao.CreateCommand();
        comando.CommandText = "DELETE FROM execucoes WHERE tarefa_id = $id; DELETE FROM tarefas WHERE id = $id;";
        comando.Parameters.AddWithValue("$id", id);

        return await comando.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tarefa>> ReservarVencidasAsync(
        DateTimeOffset agora,
        DateTimeOffset ate,
        int limite,
        CancellationToken cancelamento = default)
    {
        await using var comando = conexao.CreateCommand();

        // O UPDATE ... RETURNING roda em uma única declaração, então o SQLite
        // resolve a disputa sozinho: dois processos rodando isto ao mesmo tempo
        // recebem conjuntos disjuntos de tarefas.
        comando.CommandText = """
            UPDATE tarefas
               SET proxima_execucao = $ate
             WHERE id IN (
                   SELECT id FROM tarefas
                    WHERE estado = 'Ativa'
                      AND proxima_execucao IS NOT NULL
                      AND proxima_execucao <= $agora
                    ORDER BY proxima_execucao
                    LIMIT $limite)
            RETURNING *;
            """;

        comando.Parameters.AddWithValue("$agora", Texto(agora)!);
        comando.Parameters.AddWithValue("$ate", Texto(ate)!);
        comando.Parameters.AddWithValue("$limite", limite);

        var reservadas = await LerTarefas(comando, cancelamento).ConfigureAwait(false);

        // O RETURNING devolve a linha já alterada; o motor precisa da janela
        // original para registrar a execução no horário certo.
        return reservadas.Select(tarefa => tarefa with { ProximaExecucao = agora }).ToList();
    }

    /// <inheritdoc />
    public async Task SalvarExecucaoAsync(Execucao execucao, CancellationToken cancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(execucao);

        await using var comando = conexao.CreateCommand();
        comando.CommandText = """
            INSERT INTO execucoes (
                id, tarefa_id, agendada, inicio, fim, estado, tentativa, erro, saida, reserva_ate)
            VALUES (
                $id, $tarefa, $agendada, $inicio, $fim, $estado, $tentativa, $erro, $saida, $reserva)
            ON CONFLICT(id) DO UPDATE SET
                fim = excluded.fim,
                estado = excluded.estado,
                tentativa = excluded.tentativa,
                erro = excluded.erro,
                saida = excluded.saida,
                reserva_ate = excluded.reserva_ate;
            """;

        comando.Parameters.AddWithValue("$id", execucao.Id);
        comando.Parameters.AddWithValue("$tarefa", execucao.TarefaId);
        comando.Parameters.AddWithValue("$agendada", Texto(execucao.Agendada)!);
        comando.Parameters.AddWithValue("$inicio", Texto(execucao.Inicio)!);
        comando.Parameters.AddWithValue("$fim", Texto(execucao.Fim));
        comando.Parameters.AddWithValue("$estado", execucao.Estado.ToString());
        comando.Parameters.AddWithValue("$tentativa", execucao.Tentativa);
        comando.Parameters.AddWithValue("$erro", (object?)execucao.Erro ?? DBNull.Value);
        comando.Parameters.AddWithValue("$saida", (object?)execucao.Saida ?? DBNull.Value);
        comando.Parameters.AddWithValue("$reserva", Texto(execucao.ReservaAte));

        await comando.ExecuteNonQueryAsync(cancelamento).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Execucao>> ListarExecucoesAsync(
        string tarefaId,
        int limite = 50,
        CancellationToken cancelamento = default)
    {
        await using var comando = conexao.CreateCommand();
        comando.CommandText = """
            SELECT * FROM execucoes
             WHERE tarefa_id = $tarefa
             ORDER BY inicio DESC
             LIMIT $limite;
            """;

        comando.Parameters.AddWithValue("$tarefa", tarefaId);
        comando.Parameters.AddWithValue("$limite", limite);

        return await LerExecucoes(comando, cancelamento).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Execucao>> ListarAbandonadasAsync(
        DateTimeOffset agora,
        CancellationToken cancelamento = default)
    {
        await using var comando = conexao.CreateCommand();
        comando.CommandText = """
            SELECT * FROM execucoes
             WHERE estado = 'Rodando'
               AND reserva_ate IS NOT NULL
               AND reserva_ate < $agora;
            """;

        comando.Parameters.AddWithValue("$agora", Texto(agora)!);

        return await LerExecucoes(comando, cancelamento).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => conexao.Dispose();

    private void CriarEsquema()
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS tarefas (
                id                      TEXT PRIMARY KEY,
                nome                    TEXT NOT NULL,
                cron                    TEXT NOT NULL,
                fuso                    TEXT NOT NULL,
                executor                TEXT NOT NULL,
                carga                   TEXT NULL,
                estado                  TEXT NOT NULL,
                sobreposicao            TEXT NOT NULL,
                limite_segundos         REAL NOT NULL,
                maximo_tentativas       INTEGER NOT NULL,
                atraso_inicial_segundos REAL NOT NULL,
                atraso_maximo_segundos  REAL NOT NULL,
                embaralhamento          REAL NOT NULL,
                proxima_execucao        TEXT NULL,
                ultima_execucao         TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_tarefas_proxima
                ON tarefas (estado, proxima_execucao);

            CREATE TABLE IF NOT EXISTS execucoes (
                id          TEXT PRIMARY KEY,
                tarefa_id   TEXT NOT NULL,
                agendada    TEXT NOT NULL,
                inicio      TEXT NOT NULL,
                fim         TEXT NULL,
                estado      TEXT NOT NULL,
                tentativa   INTEGER NOT NULL,
                erro        TEXT NULL,
                saida       TEXT NULL,
                reserva_ate TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_execucoes_tarefa
                ON execucoes (tarefa_id, inicio DESC);
            """;

        comando.ExecuteNonQuery();
    }

    private static async Task<IReadOnlyList<Tarefa>> LerTarefas(SqliteCommand comando, CancellationToken cancelamento)
    {
        var tarefas = new List<Tarefa>();

        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            tarefas.Add(LerTarefa(leitor));
        }

        return tarefas;
    }

    private static async Task<IReadOnlyList<Execucao>> LerExecucoes(SqliteCommand comando, CancellationToken cancelamento)
    {
        var execucoes = new List<Execucao>();

        await using var leitor = await comando.ExecuteReaderAsync(cancelamento).ConfigureAwait(false);
        while (await leitor.ReadAsync(cancelamento).ConfigureAwait(false))
        {
            execucoes.Add(LerExecucao(leitor));
        }

        return execucoes;
    }

    private static Tarefa LerTarefa(SqliteDataReader leitor) => new()
    {
        Id = leitor.GetString(leitor.GetOrdinal("id")),
        Nome = leitor.GetString(leitor.GetOrdinal("nome")),
        Cron = leitor.GetString(leitor.GetOrdinal("cron")),
        Fuso = leitor.GetString(leitor.GetOrdinal("fuso")),
        Executor = leitor.GetString(leitor.GetOrdinal("executor")),
        Carga = TextoOuNulo(leitor, "carga"),
        Estado = Enum.Parse<EstadoDaTarefa>(leitor.GetString(leitor.GetOrdinal("estado"))),
        Sobreposicao = Enum.Parse<Sobreposicao>(leitor.GetString(leitor.GetOrdinal("sobreposicao"))),
        Limite = TimeSpan.FromSeconds(leitor.GetDouble(leitor.GetOrdinal("limite_segundos"))),
        Retentativa = new PoliticaDeRetentativa(
            leitor.GetInt32(leitor.GetOrdinal("maximo_tentativas")),
            TimeSpan.FromSeconds(leitor.GetDouble(leitor.GetOrdinal("atraso_inicial_segundos"))),
            TimeSpan.FromSeconds(leitor.GetDouble(leitor.GetOrdinal("atraso_maximo_segundos"))),
            leitor.GetDouble(leitor.GetOrdinal("embaralhamento"))),
        ProximaExecucao = Instante(TextoOuNulo(leitor, "proxima_execucao")),
        UltimaExecucao = Instante(TextoOuNulo(leitor, "ultima_execucao")),
    };

    private static Execucao LerExecucao(SqliteDataReader leitor) => new()
    {
        Id = leitor.GetString(leitor.GetOrdinal("id")),
        TarefaId = leitor.GetString(leitor.GetOrdinal("tarefa_id")),
        Agendada = Instante(leitor.GetString(leitor.GetOrdinal("agendada")))!.Value,
        Inicio = Instante(leitor.GetString(leitor.GetOrdinal("inicio")))!.Value,
        Fim = Instante(TextoOuNulo(leitor, "fim")),
        Estado = Enum.Parse<EstadoDaExecucao>(leitor.GetString(leitor.GetOrdinal("estado"))),
        Tentativa = leitor.GetInt32(leitor.GetOrdinal("tentativa")),
        Erro = TextoOuNulo(leitor, "erro"),
        Saida = TextoOuNulo(leitor, "saida"),
        ReservaAte = Instante(TextoOuNulo(leitor, "reserva_ate")),
    };

    private static string? TextoOuNulo(SqliteDataReader leitor, string coluna)
    {
        var indice = leitor.GetOrdinal(coluna);
        return leitor.IsDBNull(indice) ? null : leitor.GetString(indice);
    }

    // As datas vão para o banco como texto ISO em UTC, que ordena em ordem
    // cronológica e não depende da cultura da máquina.
    private static object Texto(DateTimeOffset? instante) => instante is null
        ? DBNull.Value
        : instante.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Instante(string? texto) => string.IsNullOrWhiteSpace(texto)
        ? null
        : DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
