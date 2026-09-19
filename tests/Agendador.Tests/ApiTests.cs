using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Agendador.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> fabrica;

    public ApiTests(WebApplicationFactory<Program> fabrica)
    {
        this.fabrica = fabrica;
    }

    private static object Pedido(string id) => new
    {
        id,
        nome = $"Tarefa {id}",
        cron = "0 3 * * *",
        executor = "eco",
        fuso = "UTC",
        carga = "conteudo",
    };

    [Fact]
    public async Task Responde_a_verificacao_de_saude()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/saude");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lista_os_executores_registrados()
    {
        var cliente = fabrica.CreateClient();

        var nomes = await cliente.GetFromJsonAsync<string[]>("/executores");

        nomes.Should().Contain("eco");
    }

    [Fact]
    public async Task Preve_os_proximos_disparos_de_uma_expressao()
    {
        var cliente = fabrica.CreateClient();

        var resposta = await cliente.GetAsync("/cron/previsao?cron=0%209%20*%20*%20*&fuso=UTC&quantidade=3");
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        corpo.GetProperty("proximas").GetArrayLength().Should().Be(3);
        corpo.GetProperty("fuso").GetString().Should().Be("UTC");
    }

    [Fact]
    public async Task Recusa_expressao_invalida_na_previsao()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/cron/previsao?cron=todo%20dia");

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recusa_fuso_desconhecido_na_previsao()
    {
        var resposta = await fabrica.CreateClient()
            .GetAsync("/cron/previsao?cron=0%209%20*%20*%20*&fuso=Marte/Olympus");

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cadastra_busca_e_remove_uma_tarefa()
    {
        var cliente = fabrica.CreateClient();
        var id = $"ciclo-{Guid.NewGuid():N}";

        var criada = await cliente.PostAsJsonAsync("/tarefas", Pedido(id));
        criada.StatusCode.Should().Be(HttpStatusCode.Created);

        var corpo = await criada.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("id").GetString().Should().Be(id);
        corpo.GetProperty("estado").GetString().Should().Be("Ativa");
        corpo.GetProperty("proximaExecucao").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);

        (await cliente.GetAsync($"/tarefas/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await cliente.DeleteAsync($"/tarefas/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cliente.GetAsync($"/tarefas/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recusa_cadastro_com_cron_invalido()
    {
        var cliente = fabrica.CreateClient();

        var resposta = await cliente.PostAsJsonAsync("/tarefas", new
        {
            id = $"ruim-{Guid.NewGuid():N}",
            nome = "Ruim",
            cron = "todo dia as 3",
            executor = "eco",
        });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recusa_cadastro_com_executor_desconhecido()
    {
        var cliente = fabrica.CreateClient();

        var resposta = await cliente.PostAsJsonAsync("/tarefas", new
        {
            id = $"orfa-{Guid.NewGuid():N}",
            nome = "Órfã",
            cron = "0 3 * * *",
            executor = "nao-existe",
        });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Pausa_e_retoma_uma_tarefa()
    {
        var cliente = fabrica.CreateClient();
        var id = $"pausa-{Guid.NewGuid():N}";
        await cliente.PostAsJsonAsync("/tarefas", Pedido(id));

        var pausada = await cliente.PostAsync($"/tarefas/{id}/pausa", null);
        var corpoPausada = await pausada.Content.ReadFromJsonAsync<JsonElement>();

        corpoPausada.GetProperty("estado").GetString().Should().Be("Pausada");
        corpoPausada.GetProperty("proximaExecucao").ValueKind.Should().Be(JsonValueKind.Null);

        var retomada = await cliente.PostAsync($"/tarefas/{id}/retomada", null);
        var corpoRetomada = await retomada.Content.ReadFromJsonAsync<JsonElement>();

        corpoRetomada.GetProperty("estado").GetString().Should().Be("Ativa");
    }

    [Fact]
    public async Task Dispara_a_tarefa_na_hora_e_registra_a_execucao()
    {
        var cliente = fabrica.CreateClient();
        var id = $"disparo-{Guid.NewGuid():N}";
        await cliente.PostAsJsonAsync("/tarefas", Pedido(id));

        var disparo = await cliente.PostAsync($"/tarefas/{id}/disparo", null);
        var corpo = await disparo.Content.ReadFromJsonAsync<JsonElement>();

        disparo.StatusCode.Should().Be(HttpStatusCode.OK);
        corpo.GetProperty("estado").GetString().Should().Be("Concluida");
        corpo.GetProperty("saida").GetString().Should().Be("conteudo");

        var execucoes = await cliente.GetFromJsonAsync<JsonElement>($"/tarefas/{id}/execucoes");
        execucoes.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Devolve_nao_encontrado_para_tarefa_inexistente()
    {
        var cliente = fabrica.CreateClient();

        (await cliente.GetAsync("/tarefas/fantasma")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.DeleteAsync("/tarefas/fantasma")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.PostAsync("/tarefas/fantasma/pausa", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.PostAsync("/tarefas/fantasma/disparo", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Uma_passagem_do_motor_responde_com_a_lista_de_execucoes()
    {
        var cliente = fabrica.CreateClient();

        var resposta = await cliente.PostAsync("/motor/passagem", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<JsonElement>()).ValueKind.Should().Be(JsonValueKind.Array);
    }
}
