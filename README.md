# agendador-de-tarefas

Agendador de tarefas por expressão cron, em .NET 8, com estado persistido. Lê
expressões de cinco ou seis campos, calcula o próximo disparo no fuso certo
(inclusive nas viradas do horário de verão), roda a tarefa, e sabe o que fazer
quando ela falha, trava ou pega a execução anterior ainda rodando.

## Por que existe

Um `while (true)` com `Task.Delay` resolve enquanto há um processo só e nada dá
errado. O problema aparece depois: o processo reinicia e a tarefa some, sobem
duas instâncias e tudo roda em dobro, a tarefa falha e ninguém tenta de novo, ou
ela trava e bloqueia o agendamento para sempre. Aqui todo o estado mora no
repositório, e a reserva de cada janela é uma operação atômica — dois processos
sobre o mesmo banco não executam a mesma janela duas vezes.

## O que a expressão aceita

| Sintaxe | Exemplo | Significado |
| --- | --- | --- |
| cinco campos | `30 8 * * *` | 8h30 todo dia (o segundo fica em zero) |
| seis campos | `15 30 8 * * *` | 8h30m15s todo dia |
| lista | `0 8,12,18 * * *` | às 8h, 12h e 18h |
| faixa | `0 9-17 * * 1-5` | de hora em hora, das 9h às 17h, de segunda a sexta |
| passo | `*/15 * * * *` | de 15 em 15 minutos |
| faixa que dá a volta | `0 22-2 * * *` | às 22h, 23h, 0h, 1h e 2h |
| nomes | `0 0 * JAN,DEZ SEG` | segundas de janeiro e dezembro (PT e EN) |
| último dia | `0 23 L * *` | 23h do último dia de cada mês |
| enésimo dia da semana | `0 10 * * 5#3` | 10h da terceira sexta-feira |
| apelidos | `@daily`, `@hourly`, `@weekly`, `@monthly`, `@yearly` | os de sempre |

O domingo aceita `0` e `7`, e `?` vale como `*`. Quando o dia do mês e o dia da
semana estão os dois restritos, o cron dispara em qualquer um dos dois — é o
comportamento clássico, e `0 0 1 * SEG` cai no dia 1 **e** em toda segunda.

## Como o motor se comporta

- **Reserva atômica.** A cada passagem o motor reserva as tarefas vencidas com
  um `UPDATE ... RETURNING` condicional. Quem reserva, roda.
- **Retentativa com recuo exponencial.** O atraso dobra a cada tentativa até o
  teto e leva um embaralhamento proporcional, para que tarefas que falharam
  juntas não voltem todas no mesmo instante.
- **Limite de tempo.** Passou do limite, a execução é cancelada e registrada
  como expirada.
- **Sobreposição.** Por padrão a janela é pulada se a anterior ainda roda;
  `Sobreposicao.Permitir` deixa correr em paralelo.
- **Recolhimento de órfãs.** Se o processo morrer no meio, a reserva vence e a
  execução é recolhida na passagem seguinte, em vez de travar a tarefa.
- **Horário de verão.** Um horário que não existe na virada roda no primeiro
  instante válido; um que acontece duas vezes roda na primeira.

## Estrutura

```
src/Agendador.Core     cron, modelo, armazenamento e motor
src/Agendador.Api      API mínima para cadastrar, acompanhar e disparar
tests/Agendador.Tests  testes de unidade e de integração
```

O armazenamento tem duas implementações do mesmo contrato — em memória e em
SQLite — e a mesma bateria de testes roda contra as duas.

## Como rodar

```bash
dotnet test                             # roda a suíte inteira
dotnet run --project src/Agendador.Api  # sobe a API em http://localhost:5000
```

Sem configuração a API usa o armazenamento em memória. Para persistir, aponte um
arquivo:

```bash
Agendador__Banco=agendador.db dotnet run --project src/Agendador.Api
```

## Endpoints

| Método | Rota | O que faz |
| --- | --- | --- |
| `GET` | `/saude` | verificação de disponibilidade |
| `GET` | `/executores` | nomes registrados |
| `GET` | `/cron/previsao?cron=&fuso=&quantidade=` | próximos disparos de uma expressão |
| `GET` | `/tarefas` | lista as tarefas |
| `POST` | `/tarefas` | cadastra ou atualiza uma tarefa |
| `GET` | `/tarefas/{id}` | detalha uma tarefa |
| `DELETE` | `/tarefas/{id}` | remove a tarefa e o histórico |
| `POST` | `/tarefas/{id}/pausa` | pausa sem apagar |
| `POST` | `/tarefas/{id}/retomada` | retoma e recalcula a janela |
| `POST` | `/tarefas/{id}/disparo` | roda na hora, sem mexer no agendamento |
| `GET` | `/tarefas/{id}/execucoes` | histórico de execuções |
| `POST` | `/motor/passagem` | força uma passagem do motor |

### Exemplo

```bash
curl -s "http://localhost:5000/cron/previsao?cron=0%2010%20*%20*%205%233&quantidade=2"
```

```json
{
  "cron": "0 10 * * 5#3",
  "fuso": "America/Sao_Paulo",
  "proximas": ["2026-10-16T10:00:00-03:00", "2026-11-20T10:00:00-03:00"]
}
```

## Uso como biblioteca

```csharp
var repositorio = new RepositorioSqlite("agendador.db");
var despachante = new Despachante()
    .Registrar("fechamento", async (contexto, cancelamento) =>
    {
        await FecharOCaixaAsync(contexto.Janela, cancelamento);
        return "caixa fechado";
    });

var motor = new MotorDoAgendador(repositorio, despachante);

await motor.AgendarAsync(new Tarefa
{
    Id = "fechamento-diario",
    Nome = "Fechamento do caixa",
    Cron = "0 23 * * *",
    Fuso = "America/Sao_Paulo",
    Executor = "fechamento",
    Retentativa = new PoliticaDeRetentativa(MaximoDeTentativas: 5),
});

await motor.RodarAsync(cancelamento);
```

Para testar sem esperar o relógio, injete um `RelogioFixo` e chame `PassarAsync`
uma passagem por vez.

## Limites conhecidos

- A reserva atômica cobre vários processos sobre o mesmo banco, mas o SQLite
  ainda é um arquivo: para muitos nós, troque a implementação de `IRepositorio`.
- Não há fila nem prioridade entre tarefas vencidas ao mesmo tempo; o critério é
  só a ordem da janela.
- `L` e `#` são aceitos; `W` (dia útil mais próximo) ainda não.

## Licença

MIT.
