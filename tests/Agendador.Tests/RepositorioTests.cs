using Agendador.Core.Armazenamento;
using Agendador.Core.Modelo;

namespace Agendador.Tests;

/// <summary>
/// A mesma bateria roda contra os dois armazenamentos. Se um deles se comportar
/// diferente, o motor vai se comportar diferente junto.
/// </summary>
public abstract class RepositorioTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    protected abstract IRepositorio Criar();

    [Fact]
    public async Task Salva_e_busca_uma_tarefa()
    {
        var repositorio = Criar();
        var tarefa = Tarefa("relatorio");

        await repositorio.SalvarAsync(tarefa);
        var lida = await repositorio.BuscarAsync("relatorio");

        lida.Should().NotBeNull();
        lida!.Nome.Should().Be(tarefa.Nome);
        lida.Cron.Should().Be(tarefa.Cron);
        lida.Executor.Should().Be(tarefa.Executor);
        lida.Limite.Should().Be(tarefa.Limite);
        lida.Retentativa.MaximoDeTentativas.Should().Be(tarefa.Retentativa.MaximoDeTentativas);
        lida.ProximaExecucao.Should().Be(tarefa.ProximaExecucao);
    }

    [Fact]
    public async Task Buscar_o_que_nao_existe_devolve_nulo()
    {
        (await Criar().BuscarAsync("fantasma")).Should().BeNull();
    }

    [Fact]
    public async Task Salvar_de_novo_atualiza_em_vez_de_duplicar()
    {
        var repositorio = Criar();
        await repositorio.SalvarAsync(Tarefa("relatorio"));
        await repositorio.SalvarAsync(Tarefa("relatorio") with { Nome = "Outro nome" });

        var todas = await repositorio.ListarAsync();

        todas.Should().ContainSingle();
        todas[0].Nome.Should().Be("Outro nome");
    }

    [Fact]
    public async Task Lista_as_tarefas_em_ordem_de_nome()
    {
        var repositorio = Criar();
        await repositorio.SalvarAsync(Tarefa("b") with { Nome = "Zebra" });
        await repositorio.SalvarAsync(Tarefa("a") with { Nome = "Abelha" });

        var todas = await repositorio.ListarAsync();

        todas.Select(tarefa => tarefa.Nome).Should().Equal("Abelha", "Zebra");
    }

    [Fact]
    public async Task Remover_apaga_a_tarefa_e_o_historico()
    {
        var repositorio = Criar();
        await repositorio.SalvarAsync(Tarefa("relatorio"));
        await repositorio.SalvarExecucaoAsync(Execucao("e1", "relatorio"));

        (await repositorio.RemoverAsync("relatorio")).Should().BeTrue();
        (await repositorio.BuscarAsync("relatorio")).Should().BeNull();
        (await repositorio.ListarExecucoesAsync("relatorio")).Should().BeEmpty();
    }

    [Fact]
    public async Task Remover_o_que_nao_existe_devolve_falso()
    {
        (await Criar().RemoverAsync("fantasma")).Should().BeFalse();
    }

    [Fact]
    public async Task Reserva_apenas_o_que_ja_venceu()
    {
        var repositorio = Criar();
        await repositorio.SalvarAsync(Tarefa("vencida") with { ProximaExecucao = Agora.AddMinutes(-1) });
        await repositorio.SalvarAsync(Tarefa("futura") with { ProximaExecucao = Agora.AddMinutes(10) });

        var reservadas = await repositorio.ReservarVencidasAsync(Agora, Agora.AddMinutes(5), 10);

        reservadas.Select(tarefa => tarefa.Id).Should().Equal("vencida");
    }

    [Fact]
    public async Task Nao_reserva_tarefa_pausada()
    {
        var repositorio = Criar();
        await repositorio.SalvarAsync(Tarefa("pausada") with
        {
            Estado = EstadoDaTarefa.Pausada,
            ProximaExecucao = Agora.AddMinutes(-1),
        });

        (await repositorio.ReservarVencidasAsync(Agora, Agora.AddMinutes(5), 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task Nao_reserva_tarefa_sem_proxima_execucao()
    {
        var repositorio = Criar();
        await repositorio.SalvarAsync(Tarefa("solta") with { ProximaExecucao = null });

        (await repositorio.ReservarVencidasAsync(Agora, Agora.AddMinutes(5), 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_reserva_empurra_a_proxima_execucao_e_impede_a_segunda_pegada()
    {
        var repositorio = Criar();
        await repositorio.SalvarAsync(Tarefa("vencida") with { ProximaExecucao = Agora.AddMinutes(-1) });

        var primeira = await repositorio.ReservarVencidasAsync(Agora, Agora.AddMinutes(5), 10);
        var segunda = await repositorio.ReservarVencidasAsync(Agora, Agora.AddMinutes(5), 10);

        primeira.Should().ContainSingle();
        segunda.Should().BeEmpty();
    }

    [Fact]
    public async Task A_reserva_respeita_o_tamanho_do_lote()
    {
        var repositorio = Criar();
        for (var i = 0; i < 5; i++)
        {
            await repositorio.SalvarAsync(Tarefa($"t{i}") with { ProximaExecucao = Agora.AddMinutes(-i - 1) });
        }

        var reservadas = await repositorio.ReservarVencidasAsync(Agora, Agora.AddMinutes(5), 2);

        reservadas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Salva_e_lista_execucoes_da_mais_nova_para_a_mais_velha()
    {
        var repositorio = Criar();
        await repositorio.SalvarExecucaoAsync(Execucao("e1", "relatorio") with { Inicio = Agora });
        await repositorio.SalvarExecucaoAsync(Execucao("e2", "relatorio") with { Inicio = Agora.AddMinutes(1) });
        await repositorio.SalvarExecucaoAsync(Execucao("e3", "outra") with { Inicio = Agora.AddMinutes(2) });

        var execucoes = await repositorio.ListarExecucoesAsync("relatorio");

        execucoes.Select(execucao => execucao.Id).Should().Equal("e2", "e1");
    }

    [Fact]
    public async Task Salvar_execucao_de_novo_atualiza_o_estado()
    {
        var repositorio = Criar();
        await repositorio.SalvarExecucaoAsync(Execucao("e1", "relatorio"));
        await repositorio.SalvarExecucaoAsync(Execucao("e1", "relatorio").Concluir(Agora.AddSeconds(5), "ok"));

        var execucoes = await repositorio.ListarExecucoesAsync("relatorio");

        execucoes.Should().ContainSingle();
        execucoes[0].Estado.Should().Be(EstadoDaExecucao.Concluida);
        execucoes[0].Saida.Should().Be("ok");
        execucoes[0].ReservaAte.Should().BeNull();
    }

    [Fact]
    public async Task O_limite_corta_a_listagem_de_execucoes()
    {
        var repositorio = Criar();
        for (var i = 0; i < 5; i++)
        {
            await repositorio.SalvarExecucaoAsync(Execucao($"e{i}", "relatorio") with { Inicio = Agora.AddMinutes(i) });
        }

        (await repositorio.ListarExecucoesAsync("relatorio", limite: 2)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Lista_como_abandonada_a_execucao_cuja_reserva_venceu()
    {
        var repositorio = Criar();
        await repositorio.SalvarExecucaoAsync(Execucao("velha", "relatorio") with { ReservaAte = Agora.AddMinutes(-1) });
        await repositorio.SalvarExecucaoAsync(Execucao("nova", "relatorio") with { ReservaAte = Agora.AddMinutes(1) });
        await repositorio.SalvarExecucaoAsync(Execucao("fechada", "relatorio").Concluir(Agora));

        var abandonadas = await repositorio.ListarAbandonadasAsync(Agora);

        abandonadas.Select(execucao => execucao.Id).Should().Equal("velha");
    }

    private static Tarefa Tarefa(string id) => new()
    {
        Id = id,
        Nome = $"Tarefa {id}",
        Cron = "0 3 * * *",
        Fuso = "UTC",
        Executor = "eco",
        Carga = "{}",
        Limite = TimeSpan.FromMinutes(2),
        Retentativa = new PoliticaDeRetentativa(4, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), 0.1),
        ProximaExecucao = Agora.AddHours(1),
    };

    private static Execucao Execucao(string id, string tarefaId) => new()
    {
        Id = id,
        TarefaId = tarefaId,
        Agendada = Agora,
        Inicio = Agora,
        ReservaAte = Agora.AddMinutes(5),
    };
}

public class RepositorioEmMemoriaTests : RepositorioTests
{
    protected override IRepositorio Criar() => new RepositorioEmMemoria();
}

public class RepositorioSqliteTests : RepositorioTests, IDisposable
{
    private readonly List<RepositorioSqlite> abertos = [];

    protected override IRepositorio Criar()
    {
        var repositorio = RepositorioSqlite.EmMemoria();
        abertos.Add(repositorio);
        return repositorio;
    }

    [Fact]
    public void Recusa_caminho_vazio()
    {
        var acao = () => new RepositorioSqlite("  ");

        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Sobrevive_ao_fechamento_quando_o_banco_esta_em_arquivo()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"agendador-{Guid.NewGuid():N}.db");

        try
        {
            using (var primeiro = new RepositorioSqlite(caminho))
            {
                await primeiro.SalvarAsync(new Tarefa
                {
                    Id = "persistida",
                    Nome = "Persistida",
                    Cron = "0 4 * * *",
                    Fuso = "UTC",
                    Executor = "eco",
                });
            }

            using var segundo = new RepositorioSqlite(caminho);
            var lida = await segundo.BuscarAsync("persistida");

            lida.Should().NotBeNull();
            lida!.Nome.Should().Be("Persistida");
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    public void Dispose()
    {
        foreach (var repositorio in abertos)
        {
            repositorio.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
