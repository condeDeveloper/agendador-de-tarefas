using Agendador.Core.Motor;

namespace Agendador.Tests;

public class DespachanteTests
{
    [Fact]
    public void Registra_e_encontra_pelo_nome()
    {
        var despachante = new Despachante().Registrar("eco", (contexto, _) => Task.FromResult(contexto.Carga));

        despachante.Conhece("eco").Should().BeTrue();
        despachante.Buscar("eco").Should().NotBeNull();
        despachante.Nomes.Should().Equal("eco");
    }

    [Fact]
    public void O_nome_nao_diferencia_maiusculas()
    {
        var despachante = new Despachante().Registrar("Eco", (_, _) => Task.FromResult<string?>(null));

        despachante.Conhece("ECO").Should().BeTrue();
    }

    [Fact]
    public void Registrar_de_novo_substitui_o_anterior()
    {
        var despachante = new Despachante()
            .Registrar("eco", (_, _) => Task.FromResult<string?>("primeiro"))
            .Registrar("eco", (_, _) => Task.FromResult<string?>("segundo"));

        despachante.Nomes.Should().ContainSingle();
    }

    [Fact]
    public void Aceita_um_conjunto_no_construtor()
    {
        var despachante = new Despachante(
        [
            new ExecutorDelegado("a", (_, _) => Task.FromResult<string?>(null)),
            new ExecutorDelegado("b", (_, _) => Task.FromResult<string?>(null)),
        ]);

        despachante.Nomes.Should().Equal("a", "b");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nao-existe")]
    public void Nao_conhece_nome_vazio_nem_desconhecido(string? nome)
    {
        var despachante = new Despachante().Registrar("eco", (_, _) => Task.FromResult<string?>(null));

        despachante.Conhece(nome).Should().BeFalse();
        despachante.Buscar(nome).Should().BeNull();
    }

    [Fact]
    public void Exigir_lanca_listando_o_que_existe()
    {
        var despachante = new Despachante().Registrar("eco", (_, _) => Task.FromResult<string?>(null));

        var acao = () => despachante.Exigir("relatorio");

        acao.Should().Throw<InvalidOperationException>().WithMessage("*eco*");
    }

    [Fact]
    public void O_executor_delegado_exige_nome_e_acao()
    {
        var semNome = () => new ExecutorDelegado(" ", (_, _) => Task.FromResult<string?>(null));
        var semAcao = () => new ExecutorDelegado("eco", null!);

        semNome.Should().Throw<ArgumentException>();
        semAcao.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task O_contexto_leva_a_carga_e_a_tentativa()
    {
        var tarefa = new Core.Modelo.Tarefa
        {
            Id = "t",
            Nome = "T",
            Cron = "* * * * *",
            Executor = "eco",
            Carga = "conteudo",
        };

        var contexto = new Contexto(tarefa, DateTimeOffset.UnixEpoch, 2);

        contexto.Carga.Should().Be("conteudo");
        contexto.EhRetentativa.Should().BeTrue();

        var executor = new ExecutorDelegado("eco", (atual, _) => Task.FromResult(atual.Carga));
        (await executor.ExecutarAsync(contexto, CancellationToken.None)).Should().Be("conteudo");
    }
}
