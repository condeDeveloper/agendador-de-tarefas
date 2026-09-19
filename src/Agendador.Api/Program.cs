using Agendador.Api;
using Agendador.Core.Armazenamento;
using Agendador.Core.Cron;
using Agendador.Core.Modelo;
using Agendador.Core.Motor;
using Microsoft.AspNetCore.Mvc;

var construtor = WebApplication.CreateBuilder(args);

construtor.Services.AddEndpointsApiExplorer();
construtor.Services.AddSwaggerGen();

var banco = construtor.Configuration["Agendador:Banco"];
construtor.Services.AddSingleton<IRepositorio>(_ => string.IsNullOrWhiteSpace(banco)
    ? new RepositorioEmMemoria()
    : new RepositorioSqlite(banco));

// Os executores de exemplo existem para que a API suba pronta para uso; em um
// serviço de verdade eles seriam registrados pelo próprio domínio.
construtor.Services.AddSingleton(_ => new Despachante()
    .Registrar("registrar-no-log", (contexto, _) =>
        Task.FromResult<string?>($"Tarefa {contexto.Tarefa.Nome} executada na janela {contexto.Janela:O}."))
    .Registrar("eco", (contexto, _) => Task.FromResult(contexto.Carga)));

// O motor é montado à mão porque o construtor tem parâmetros opcionais que só
// fazem sentido em teste, como o relógio falso e o sorteio fixo.
construtor.Services.AddSingleton(servicos => new MotorDoAgendador(
    servicos.GetRequiredService<IRepositorio>(),
    servicos.GetRequiredService<Despachante>(),
    log: servicos.GetRequiredService<ILogger<MotorDoAgendador>>()));

var aplicacao = construtor.Build();

if (aplicacao.Environment.IsDevelopment())
{
    aplicacao.UseSwagger();
    aplicacao.UseSwaggerUI();
}

aplicacao.MapGet("/saude", () => Results.Ok(new { estado = "ok" }))
    .WithName("Saude")
    .WithTags("Serviço");

aplicacao.MapGet("/executores", (Despachante despachante) => Results.Ok(despachante.Nomes))
    .WithName("ListarExecutores")
    .WithTags("Serviço");

aplicacao.MapGet("/cron/previsao", (string cron, string? fuso, int? quantidade) =>
    {
        if (!ExpressaoCron.TentarAnalisar(cron, out var expressao))
        {
            return Problema("Expressão cron inválida", $"Não consegui interpretar '{cron}'.");
        }

        var zona = ResolverFuso(fuso);
        if (zona is null)
        {
            return Problema("Fuso desconhecido", $"Não conheço o fuso '{fuso}'.");
        }

        var proximas = expressao
            .ProximasExecucoes(DateTimeOffset.UtcNow, zona, Math.Clamp(quantidade ?? 5, 1, 50))
            .ToList();

        return Results.Ok(new Previsao(expressao.Texto, zona.Id, proximas));
    })
    .WithName("PreverCron")
    .WithTags("Cron");

aplicacao.MapGet("/tarefas", async (IRepositorio repositorio) =>
    {
        var tarefas = await repositorio.ListarAsync();
        return Results.Ok(tarefas.Select(TarefaEmResposta.De));
    })
    .WithName("ListarTarefas")
    .WithTags("Tarefas");

aplicacao.MapGet("/tarefas/{id}", async (string id, IRepositorio repositorio) =>
    {
        var tarefa = await repositorio.BuscarAsync(id);
        return tarefa is null ? Results.NotFound() : Results.Ok(TarefaEmResposta.De(tarefa));
    })
    .WithName("BuscarTarefa")
    .WithTags("Tarefas");

aplicacao.MapPost("/tarefas", async ([FromBody] PedidoDeTarefa pedido, MotorDoAgendador motor) =>
    {
        try
        {
            var tarefa = await motor.AgendarAsync(pedido.ParaTarefa());
            return Results.Created($"/tarefas/{tarefa.Id}", TarefaEmResposta.De(tarefa));
        }
        catch (ErroDeExpressao erro)
        {
            return Problema("Expressão cron inválida", erro.Message);
        }
        catch (Exception erro) when (erro is ArgumentException or InvalidOperationException)
        {
            return Problema("Tarefa inválida", erro.Message);
        }
    })
    .WithName("CadastrarTarefa")
    .WithTags("Tarefas");

aplicacao.MapDelete("/tarefas/{id}", async (string id, MotorDoAgendador motor) =>
        await motor.RemoverAsync(id) ? Results.NoContent() : Results.NotFound())
    .WithName("RemoverTarefa")
    .WithTags("Tarefas");

aplicacao.MapPost("/tarefas/{id}/pausa", async (string id, MotorDoAgendador motor) =>
    {
        var tarefa = await motor.PausarAsync(id);
        return tarefa is null ? Results.NotFound() : Results.Ok(TarefaEmResposta.De(tarefa));
    })
    .WithName("PausarTarefa")
    .WithTags("Tarefas");

aplicacao.MapPost("/tarefas/{id}/retomada", async (string id, MotorDoAgendador motor) =>
    {
        var tarefa = await motor.RetomarAsync(id);
        return tarefa is null ? Results.NotFound() : Results.Ok(TarefaEmResposta.De(tarefa));
    })
    .WithName("RetomarTarefa")
    .WithTags("Tarefas");

aplicacao.MapPost("/tarefas/{id}/disparo", async (string id, MotorDoAgendador motor) =>
    {
        try
        {
            var execucao = await motor.DispararAgoraAsync(id);
            return Results.Ok(ExecucaoEmResposta.De(execucao));
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound();
        }
    })
    .WithName("DispararTarefa")
    .WithTags("Tarefas");

aplicacao.MapGet("/tarefas/{id}/execucoes", async (string id, int? limite, IRepositorio repositorio) =>
    {
        var execucoes = await repositorio.ListarExecucoesAsync(id, Math.Clamp(limite ?? 20, 1, 200));
        return Results.Ok(execucoes.Select(ExecucaoEmResposta.De));
    })
    .WithName("ListarExecucoes")
    .WithTags("Tarefas");

aplicacao.MapPost("/motor/passagem", async (MotorDoAgendador motor) =>
    {
        var execucoes = await motor.PassarAsync();
        return Results.Ok(execucoes.Select(ExecucaoEmResposta.De));
    })
    .WithName("PassarMotor")
    .WithTags("Motor");

aplicacao.Run();

static IResult Problema(string titulo, string detalhe) => Results.BadRequest(new ProblemDetails
{
    Title = titulo,
    Detail = detalhe,
    Status = StatusCodes.Status400BadRequest,
});

static TimeZoneInfo? ResolverFuso(string? identificador)
{
    if (string.IsNullOrWhiteSpace(identificador))
    {
        return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    }

    try
    {
        return TimeZoneInfo.FindSystemTimeZoneById(identificador);
    }
    catch (Exception erro) when (erro is TimeZoneNotFoundException or InvalidTimeZoneException)
    {
        return null;
    }
}

/// <summary>Exposta para que os testes de integração possam subir a API.</summary>
public partial class Program;
