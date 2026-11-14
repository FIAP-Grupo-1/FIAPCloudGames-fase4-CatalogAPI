# FIAPCloudGames — Catalog API

API RESTful responsável pelo catálogo de jogos da plataforma FIAPCloudGames, desenvolvida como parte da Fase 2 do projeto acadêmico. Segue os princípios de **Clean Architecture** com separação em camadas: Domain, Application, Infrastructure e API.

---

## Tecnologias

| Camada | Tecnologias |
|---|---|
| API | .NET 10, ASP.NET Core Minimal APIs, Swagger (Swashbuckle) |
| Application | MediatR (CQRS), Flunt (notificações/validação) |
| Infrastructure | Entity Framework Core 10, PostgreSQL 18 (Npgsql), Redis (StackExchange.Redis), FiapCloudGames.RabbitMq |
| Domain | C# puro, sem dependências externas |
| Testes | xUnit, NSubstitute, Testcontainers, coverlet, k6 (carga) |
| Infraestrutura | Docker, Docker Compose, RabbitMQ 4, Redis 7, InfluxDB + Grafana (métricas do k6) |

---

## Arquitetura

O domínio é organizado em **bounded contexts** (`Contexts/`): **Games** (catálogo de jogos) e **Libraries** (biblioteca de jogos adquiridos por usuário).

```
src/
├── CatalogAPI.Domain/          # Contexts/{Games,Libraries}: Entidades, Value Objects, Enums, Exceções, Interfaces, Eventos
├── CatalogAPI.Application/     # Contexts/{Games,Libraries}: Use Cases (Commands/Queries via MediatR), DTOs; Shared/Messaging
├── CatalogAPI.Infrastructure/  # DbContext, Repositórios, Messaging, Migrations (EF Core + PostgreSQL)
└── CatalogAPI.API/             # Endpoints (Minimal API), DI, Configuração (Swagger, JWT, tratamento de erros global)

test/
├── CatalogAPI.Domain.Tests/         # Testes unitários — Domain
├── CatalogAPI.Application.Tests/    # Testes unitários — Application
├── CatalogAPI.Infrastructure.Tests/ # Testes de integração — Repositórios (Testcontainers)
└── CatalogAPI.API.Tests/            # Testes de integração — Endpoints (Testcontainers)
```

---

## Endpoints

### Games — `/api/v1/games`

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/` | Cria um novo jogo |
| `GET` | `/` | Lista jogos com paginação |
| `GET` | `/{id}` | Busca jogo por ID |
| `PUT` | `/{id}` | Atualiza dados de um jogo |
| `DELETE` | `/{id}` | Remove um jogo |

### Library — `/api/v1/library`

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/add` | Inicia a aquisição de um jogo para a biblioteca do usuário autenticado |
| `GET` | `/` | Lista os jogos da biblioteca do usuário autenticado (paginado) |

Apenas os endpoints de **Library** exigem autenticação (`RequireAuthorization`). O `UserId` é extraído da claim `sub` (ou `NameIdentifier`) do token.

> ⚠️ **Atenção:** os endpoints de **Games** (`POST`, `PUT`, `DELETE`, `GET`) atualmente **não exigem autenticação nem autorização** — qualquer chamador pode criar, editar e remover jogos. O domínio prevê uma `InsufficientGameManagementPermissionException` (sugerindo gestão restrita a administradores), mas essa checagem ainda **não está aplicada nos endpoints**. Tratar antes de ir para produção.

- **`POST /add`** — o corpo informa apenas o `GameId`. A aquisição é **assíncrona**: validado o jogo e a posse, um evento `OrderPlacedEvent` é publicado no message bus e a API responde `202 Accepted`. A confirmação da posse ocorre depois, de forma assíncrona, pelo consumer (ver _Mensageria e Eventos_).
- **`GET /`** — retorna a biblioteca do usuário paginada via query string `?page=&pageSize=` (`Page` padrão 1, `PageSize` padrão 20, **máximo 50**). A resposta `200 OK` traz os jogos (`GameId`, `Title`, `Genre`, `Price`) e os metadados de paginação (`Page`, `PageSize`, `Total`).

A documentação interativa via Swagger está disponível em `/swagger` no ambiente de desenvolvimento.

---

## Autenticação

A API usa **JWT Bearer**, validado **offline** (sem chamada HTTP à UsersAPI). O token é emitido pela **UsersAPI** e verificado aqui com uma chave simétrica compartilhada (HMAC). As claims do token (em especial `sub`) identificam o usuário nas operações de biblioteca.

Parâmetros de validação (`BuilderExtension.AddBearerAuthentication`):

| Validação | Ativa? |
|---|---|
| Assinatura (`ValidateIssuerSigningKey`) | ✅ Sim — HMAC com a `SecretKey` compartilhada |
| Expiração (`ValidateLifetime`) | ✅ Sim |
| Issuer (`ValidateIssuer`) | ❌ Não |
| Audience (`ValidateAudience`) | ❌ Não |

`MapInboundClaims = false` (os nomes das claims não são remapeados) e `RoleClaimType = ClaimTypes.Role`.

> A `SecretKey` **precisa ser exatamente a mesma configurada na UsersAPI** (emissora do token), pois a assinatura é verificada localmente. É lida de `JwtSettings:SecretKey` e é **obrigatória**: a aplicação lança exceção no startup se ela não estiver configurada (ver _Configuração_).

---

## Mensageria e Eventos

A aquisição de um jogo é uma **saga assíncrona** coordenada por dois eventos de integração, cujos
contratos e nomes de exchange/routing key vêm do pacote compartilhado **`FiapCloudGames.Contracts`**:

1. **`OrderPlacedEvent (UserId, GameId, Price)`** — publicado pela API ao iniciar a compra (`POST /add`), após validar que o jogo existe e que o usuário ainda não o possui. É publicado na exchange `catalog.exchange` (topic) com a routing key `order.placed` (`CatalogMessaging`). A PaymentsAPI consome esse evento.
2. **`PaymentProcessedEvent (UserId, GameId, PaymentStatus)`** — publicado de volta pela PaymentsAPI na exchange `payments.exchange` com a routing key `payment.status` (`PaymentsMessaging`). `PaymentStatus` pode ser `Approved` ou `Rejected`.

Toda a integração com o broker é feita pelo pacote **`FiapCloudGames.RabbitMq`** (o mesmo usado pelas
demais APIs da plataforma), registrado via `AddRabbitMq(configuration)`. A conexão é configurada pela
seção **`RabbitMq`** (`Host`, `Port`, `Username`, `Password`, `VirtualHost`) — ver _Configuração do RabbitMQ_.

### Publicação

A camada de Application define a abstração `IIntegrationEventPublisher` (`Shared/Messaging`), cuja
implementação `RabbitMqIntegrationEventPublisher` (Infrastructure) adapta o `IRabbitMqPublisher` do
pacote. O handler de `InitiateGamePurchase` publica o `OrderPlacedEvent` informando exchange e routing
key a partir das constantes de `CatalogMessaging` — sem _magic strings_.

### Consumo

O consumo é feito por um `IMessageProcessor` registrado com
`AddRabbitMqConsumer<PaymentProcessedMessageProcessor>(...)`. O pacote sobe um `BackgroundService`
(hosted service) que declara a topologia (exchange/fila/binding) e consome a fila `catalog.payment-processed`
(consumo manual, `autoAck: false`) — **não expõe endpoint HTTP**. A responsabilidade é separada em três camadas:

1. **`PaymentProcessedMessageProcessor`** (Infrastructure) — _fino_, sem lógica de aplicação: desserializa o `PaymentProcessedEvent`, delega ao `IEventDispatcher` e traduz o resultado para um `MessageProcessingResult`.
2. **`IEventDispatcher` / `EventDispatcher`** — abstração (Application) e implementação (Infrastructure) que resolvem o `IEventHandler<TEvent>` correspondente num **escopo de DI por mensagem** — único ponto que conhece o contêiner.
3. **`PaymentProcessedEventHandler : IEventHandler<PaymentProcessedEvent>`** (Application) — concentra a **lógica de aplicação**: se o pagamento foi `Approved`, adiciona o jogo à biblioteca (via repositório de domínio `IGamePurchase`, idempotente); caso contrário, nada faz.

O mapeamento do desfecho do despacho para a política de reentrega do broker fica no processor:

- **`Success`** (ack) — o handler concluiu (pagamento `Approved` → jogo adicionado à biblioteca, ou `Rejected` → nada a fazer). A adição é **idempotente**: se o usuário já possui o jogo, nada é inserido;
- **`PoisonMessage`** (nack sem _requeue_) — mensagem malformada/vazia, jogo inexistente no catálogo (`GameNotFoundException`) ou reentrega duplicada (violação de unicidade `(UserId, GameId)`): descartada;
- **`TransientFailure`** (nack com _requeue_) — falha passageira (ex.: erro de banco de dados): reenfileirada para nova tentativa.

Essa estrutura espelha o padrão da NotificationsAPI: o processor de mensageria fica livre de regra de negócio, que vive em handlers de evento testáveis na camada de Application.

### Serialização das mensagens

Os eventos trafegam em **JSON** no barramento. O `PaymentProcessedMessageProcessor` desserializa a
mensagem com opções que **precisam casar com o formato gravado pelo publisher** (PaymentsAPI):

```csharp
new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() },
}
```

- `JsonSerializerDefaults.Web` → nomes de propriedade **case-insensitive** (aceita `camelCase` e `PascalCase`);
- `JsonStringEnumConverter` → enums aceitos tanto como **string** (`"Approved"`) quanto como **número** (`1`).

> ⚠️ Usar as opções **padrão** do `System.Text.Json` aqui causa um bug silencioso: com `PropertyNameCaseInsensitive = false`, propriedades em `camelCase` não fazem match com os parâmetros do record posicional (`UserId`/`GameId` viram `Guid.Empty`) e enums como string estouram `JsonException`. Como `PaymentStatus` não tem valor `0`, um `Status` não desserializado cai no _early-return_ do handler e **nada é persistido**. Mantenha as opções acima alinhadas com o publisher.

---

## Cache (Redis)

As consultas mais frequentes passam por um cache distribuído no Redis (`ICacheService`, implementado por
`RedisCacheService` com StackExchange.Redis). Na primeira consulta o resultado vem do PostgreSQL e é gravado
no Redis em JSON; até expirar, as próximas consultas iguais são respondidas pelo Redis, sem ir ao banco.

| Consulta | Chave no Redis | Expira em |
|---|---|---|
| `GET /api/v1/games` (listagem paginada) | `games:all:page={page}:size={pageSize}` | 3 min |
| `GET /api/v1/games/{id}` | `games:{id}` | 20 min |
| `GET /api/v1/library` (biblioteca do usuário) | `libraries:all:userId={userId}:page={page}:size={pageSize}` | 3 min |

Os tempos ficam centralizados em `CatalogAPI.Application/Shared/Cache/CacheExpiration.cs`.

**Invalidação:** o cache não espera expirar quando o dado muda.

| Quando | O que é removido |
|---|---|
| Jogo criado | todas as páginas da listagem (`games:all:*`) |
| Jogo editado ou removido | o detalhe do jogo (`games:{id}`), a listagem (`games:all:*`) e as bibliotecas (`libraries:all:*`) |
| Pagamento aprovado (`PaymentProcessed`) | as páginas da biblioteca daquele usuário (`libraries:all:userId={userId}:*`) |

A remoção por prefixo usa `SCAN` (`KeysAsync`), que não bloqueia o Redis como o `KEYS`.

### Ligar e desligar o cache (`Cache:Enabled`)

```json
"Cache": {
  "Enabled": true
}
```

Com `false` (ou a variável de ambiente `Cache__Enabled=false`), a API registra o `NoOpCacheService`: toda
consulta vai direto ao PostgreSQL e a API **nem conecta no Redis**. Serve para medir o ganho do cache com o
teste de carga (ver [Teste de carga](#teste-de-carga-k6--grafana)). O padrão é `true`.

A conexão vem de `ConnectionStrings:Redis` (no `compose.yaml`, `ConnectionStrings__Redis`), com o usuário
`catalog` criado pelo próprio container do Redis.

---

## Domain

### Entidade `Game`

| Campo | Tipo | Regras |
|---|---|---|
| `Id` | `Guid` | Gerado automaticamente |
| `Title` | `GameTitle` | Obrigatório, máximo 200 caracteres |
| `Description` | `string` | Máximo 2000 caracteres |
| `Price` | `Price` | Valor não negativo |
| `Genre` | `GameGenre` | Action, RPG, Strategy, Sports, Puzzle, Other |
| `Status` | `GameStatus` | Active, Inactive, ComingSoon |
| `ReleaseDate` | `DateOnly` | Deve ser a data atual ou futura |
| `CreatedAt` | `DateTime` | Gerado automaticamente |
| `UpdatedAt` | `DateTime?` | Atualizado a cada modificação |

### Value Objects

- **`GameTitle`** — valida obrigatoriedade e limite de 200 caracteres; aplica trim automático.
- **`Price`** — valida que o valor não seja negativo.

### Entidade `LibraryItem`

Representa a posse de um jogo por um usuário (contexto Libraries).

| Campo | Tipo | Regras |
|---|---|---|
| `Id` | `Guid` | Gerado automaticamente |
| `UserId` | `Guid` | Identificador do usuário |
| `GameId` | `Guid` | Identificador do jogo |
| `AcquiredOn` | `DateTime` | Data/hora UTC da aquisição |

A combinação `(UserId, GameId)` é única — um usuário não pode adquirir o mesmo jogo duas vezes.

### Exceções de domínio

As regras de negócio sinalizam falhas com exceções que herdam de `DomainException` (`Shared`). Os
handlers da camada de Application **lançam** essas exceções (ou deixam que as lançadas pelo domínio
propaguem); a tradução para status HTTP é centralizada — ver _Tratamento de Erros_.

| Exceção | Contexto | Quando | Status HTTP |
|---|---|---|---|
| `InvalidGameTitleException` | Games | Título vazio ou acima de 200 caracteres (`GameTitle`) | `400` |
| `InvalidPriceException` | Games | Preço negativo (`Price`) | `400` |
| `InvalidReleaseDateException` | Games | Data de lançamento inválida (`Game`) | `400` |
| `DomainValidationException` | Games | Outras invariantes (ex.: descrição acima de 2000 caracteres) | `400` |
| `GameNotFoundException` | Games | Jogo inexistente no catálogo (get/update/delete/compra) | `404` |
| `GameAlreadyOwnedException` | Libraries | Usuário já possui o jogo ao iniciar a compra | `409` |
| `InsufficientGameManagementPermissionException` | Games | Gestão de jogos sem permissão de admin — **placeholder**, ainda não lançada (ver aviso em _Endpoints_) | `403` |
| `UnauthorizedAccessException` | Libraries | Acesso indevido a recurso de outro usuário — **placeholder**, ainda não lançada | `403` |

> As duas últimas são reservadas para regras de autorização ainda **não implementadas**. Já estão
> mapeadas no handler global, então passam a valer automaticamente assim que forem lançadas.

---

## Tratamento de Erros

O tratamento de erros é **centralizado** num `IExceptionHandler` global
(`CatalogAPI.API/Configuration/GlobalExceptionHandler.cs`), registrado no `Program.cs` via
`AddExceptionHandler<GlobalExceptionHandler>()` + `AddProblemDetails()` e ativado com
`app.UseExceptionHandler()`.

- **Handlers da Application** não capturam mais exceções para montar respostas de erro: eles apenas
  **lançam** a exceção de domínio adequada (ex.: `throw new GameNotFoundException(...)`) ou retornam
  sucesso. A única resposta de erro montada no handler é a **validação de request** (Flunt/Specification),
  que retorna `400` com a lista de `Notifications`.
- O `GlobalExceptionHandler` mapeia a exceção para o status HTTP (tabela acima) e responde com
  **`ProblemDetails`** (RFC 7807). Erros `5xx` são logados e têm a mensagem interna omitida do corpo;
  exceções de domínio (`4xx`) expõem sua mensagem em `detail`.
- Falhas de infraestrutura de banco (`DbException`) e quaisquer outras exceções não previstas viram
  `500`.

Isso mantém os handlers focados no fluxo de sucesso e garante respostas de erro consistentes em toda a API.

---

## Testes

O projeto possui 202 testes distribuídos em quatro camadas. Os contextos **Games** e **Libraries** (aquisição de jogos, processamento de pagamento e consulta da biblioteca) estão cobertos.

| Projeto | Tipo | Testes | Ferramentas |
|---|---|---|---|
| `CatalogAPI.Domain.Tests` | Unitário | 37 | xUnit |
| `CatalogAPI.Application.Tests` | Unitário | 82 | xUnit, NSubstitute |
| `CatalogAPI.Infrastructure.Tests` | Integração/Unitário | 40 | xUnit, NSubstitute, Testcontainers (PostgreSQL) |
| `CatalogAPI.API.Tests` | Integração (E2E) | 43 | xUnit, Testcontainers, WebApplicationFactory |

Os testes de integração sobem containers efêmeros via **Testcontainers** — é necessário ter o Docker rodando, mas nenhum PostgreSQL ou Redis local:

- `CatalogAPI.Infrastructure.Tests`: PostgreSQL; cada classe limpa as tabelas que usa antes de cada teste.
- `CatalogAPI.API.Tests`: PostgreSQL e Redis (o `ApiFactory` mantém o cache real, `RedisCacheService`). Antes de cada teste, o `ResetDatabaseAsync` apaga as tabelas e executa `FLUSHALL` no Redis, então todo teste começa com banco e cache vazios.

> Se o Testcontainers falhar com `unauthorized: incorrect username or password` ao baixar imagens, o login salvo em `~/.docker/config.json` está inválido. Rode `docker logout` (as imagens usadas são públicas) ou faça login novamente.

> Os testes de endpoint não dependem de um broker RabbitMQ: o `ApiFactory` remove os hosted services (incluindo o consumer do `FiapCloudGames.RabbitMq`) e substitui o `IIntegrationEventPublisher` por um stub _no-op_. A lógica de consumo é coberta por testes unitários do `PaymentProcessedEventHandler` (Application) e do `PaymentProcessedMessageProcessor` (mapeamento de falhas para `MessageProcessingResult`, com o `IEventDispatcher` mockado); a persistência em si, pelos testes do repositório `GamePurchase`.

### Executar todos os testes

```bash
dotnet test
```

### Executar por camada

```bash
dotnet test test/CatalogAPI.Domain.Tests/
dotnet test test/CatalogAPI.Application.Tests/
dotnet test test/CatalogAPI.Infrastructure.Tests/
dotnet test test/CatalogAPI.API.Tests/
```

---

## Teste de carga (k6 + Grafana)

Este roteiro mede o ganho do cache Redis em `GET /api/v1/games?page=1&pageSize=20`: a mesma carga roda duas vezes,
uma com o cache ligado e outra desligado, e o resultado aparece no resumo do k6 e no Grafana.

| Peça | Onde | O que faz |
|---|---|---|
| `performance/seed-games.sh` | script bash | cadastra a massa de dados (padrão: 10.000 jogos) pela API |
| `performance/games.js` | script k6 | aquecimento e rampa de 10 a 50 usuários virtuais em 90 s; falha se mais de 1% der erro |
| `docker-compose.monitoring.yml` | Docker | InfluxDB 1.8 (guarda as métricas do k6) e Grafana (mostra os gráficos) |
| `Cache__Enabled` | variável da API | liga (`true`) ou desliga (`false`) o cache de verdade (ver [Cache](#cache-redis)) |
| `CACHE_MODE` | variável do k6 | **só uma etiqueta** (tag `cache`) gravada em cada medição, para o Grafana separar as execuções |

> ⚠️ O `CACHE_MODE` não muda nada na API. Ele precisa **combinar** com o `Cache__Enabled` da API que está
> rodando: `true` com `on`, `false` com `off`. Se não combinar, as duas linhas do Grafana se misturam.

O k6 roda pela imagem oficial `grafana/k6` no Docker: não é preciso instalar nada além do Docker e do .NET SDK.
Use **dois terminais**, os dois na raiz deste repositório: o **terminal 1** fica com a API e o **terminal 2**
com os demais comandos.

### 1. Subir as dependências e o monitoramento (terminal 2)

```bash
docker compose up -d catalogdb redis rabbitmq
dotnet ef database update --project src/CatalogAPI.Infrastructure --startup-project src/CatalogAPI.API

export GRAFANA_ADMIN_PASSWORD='<escolha uma senha>'
docker compose -f docker-compose.monitoring.yml up -d
```

O Grafana fica em `http://localhost:3000` (usuário `admin`, senha da variável acima), já com a fonte de dados
`k6` apontando para o InfluxDB (`monitoring/grafana/provisioning/datasources/k6.yml`).

> A `GRAFANA_ADMIN_PASSWORD` é exigida em **todo** comando com o `docker-compose.monitoring.yml`, não só no
> `up`: sem ela até o `ps`, o `logs` e o `down` são recusados. Num terminal novo, exporte de novo (no `down`,
> qualquer valor serve).

### 2. Subir a API com cache (terminal 1)

```bash
Cache__Enabled=true dotnet run --project src/CatalogAPI.API
```

Espere `Now listening on: http://localhost:5140` e deixe o terminal aberto.

### 3. Cadastrar a massa de dados (terminal 2, uma vez só)

```bash
performance/seed-games.sh
```

O script cadastra **10.000 jogos** pela própria API (passando pela validação de domínio), 8 requisições por vez,
e termina mostrando o total do catálogo. Leva cerca de 1 minuto. Configurável por variáveis:

```bash
QTD=20000 PARALELO=16 performance/seed-games.sh            # mais jogos, mais requisições simultâneas
BASE_URL=http://localhost:5001 performance/seed-games.sh   # API do docker compose
```

**Por que tantos jogos:** a listagem faz duas consultas no PostgreSQL a cada requisição, um `COUNT(*)` da tabela
inteira e a página com `LIMIT 20`. A tabela `games` só tem índice na chave primária, então o `COUNT(*)` percorre
todas as linhas: com poucos jogos ele é instantâneo e a diferença do cache quase não aparece; com milhares, o banco
vira o gargalo sob carga. Com cache, as duas consultas saem do caminho.

Para conferir o total:

```bash
curl -s "http://localhost:5140/api/v1/games?page=1&pageSize=1"    # campo "total"
```

### 4. Limpar medições anteriores (opcional, terminal 2)

Recria o banco `k6` do InfluxDB, para o Grafana mostrar só as execuções novas:

```bash
curl -s -XPOST http://localhost:8086/query --data-urlencode "q=DROP DATABASE k6"
curl -s -XPOST http://localhost:8086/query --data-urlencode "q=CREATE DATABASE k6"
```

### 5. Medição com cache (terminal 2)

Com a API do passo 2 (`Cache__Enabled=true`) no ar:

```bash
docker exec catalogredis redis-cli --user catalog --pass pass --no-auth-warning INFO stats | grep keyspace_hits

docker run --rm -i --network host -v "$PWD/performance:/scripts:ro" grafana/k6:2.4.0 \
  run --out influxdb=http://localhost:8086/k6 -e CACHE_MODE=on /scripts/games.js

docker exec catalogredis redis-cli --user catalog --pass pass --no-auth-warning INFO stats | grep keyspace_hits
```

- No topo da saída do k6, a linha `output:` precisa mostrar `InfluxDBv1 (http://localhost:8086)`. Se mostrar
  `output: -`, a medição não foi gravada (faltou o `--out`).
- O `keyspace_hits` (acertos de cache no Redis) precisa **subir** entre a primeira e a última leitura, mais ou
  menos uma unidade por requisição do k6. É a prova de que as respostas vieram do Redis.
- Anote do resumo final: `http_req_duration` (`avg` e `p(95)`) e `http_reqs` (requisições por segundo).

### 6. Medição sem cache

No **terminal 1**, pare a API com **Ctrl+C** e suba de novo com o cache desligado:

```bash
Cache__Enabled=false dotnet run --project src/CatalogAPI.API
```

No **terminal 2**, repita a medição com a etiqueta `off`:

```bash
docker exec catalogredis redis-cli --user catalog --pass pass --no-auth-warning INFO stats | grep keyspace_hits

docker run --rm -i --network host -v "$PWD/performance:/scripts:ro" grafana/k6:2.4.0 \
  run --out influxdb=http://localhost:8086/k6 -e CACHE_MODE=off /scripts/games.js

docker exec catalogredis redis-cli --user catalog --pass pass --no-auth-warning INFO stats | grep keyspace_hits
```

Agora o `keyspace_hits` **não pode mudar**: com `Cache__Enabled=false` a API usa o `NoOpCacheService` e nem
conecta no Redis, então toda requisição vai ao PostgreSQL. Se o número subiu, a API ainda está com cache: confira
o comando do terminal 1.

### 7. Comparar

**No terminal**, compare os resumos dos passos 5 e 6. Referência medida numa máquina de desenvolvimento, com
10.400 jogos e a API rodando local (os números variam com a máquina; a proporção é o que importa):

| Métrica do k6 | Com cache | Sem cache | Ganho |
|---|---|---|---|
| `http_req_duration` `avg` | 4,24 ms | 20,81 ms | ~4,9× mais rápido |
| `http_req_duration` `p(95)` | 9,84 ms | 43,79 ms | ~4,5× |
| `http_reqs` (vazão) | 5.699 req/s | 1.177 req/s | ~4,8× |

**No Grafana**, menu **Explore** → fonte de dados **k6** → período **Last 15 minutes** (ou o intervalo das duas
execuções) → clique no lápis para escrever a consulta em texto → **Run query**:

```sql
-- Resumo lado a lado (troque a visualização para Table): média, p95 e total por modo
SELECT mean("value") AS media_ms, percentile("value", 95) AS p95_ms, count("value") AS requisicoes
FROM "http_req_duration" WHERE $timeFilter AND "cache" != '' GROUP BY "cache"

-- Latência média ao longo do tempo, uma linha por modo
SELECT mean("value") FROM "http_req_duration" WHERE $timeFilter AND "cache" != '' GROUP BY time(5s), "cache" fill(none)

-- p95 ao longo do tempo
SELECT percentile("value", 95) FROM "http_req_duration" WHERE $timeFilter AND "cache" != '' GROUP BY time(5s), "cache" fill(none)

-- Vazão (requisições por segundo)
SELECT count("value") / 5 FROM "http_reqs" WHERE $timeFilter AND "cache" != '' GROUP BY time(5s), "cache" fill(none)
```

Cole uma consulta por vez. O filtro `"cache" != ''` tira a requisição de aquecimento do script, que é gravada sem
etiqueta. Como as execuções rodam uma depois da outra, nos gráficos de tempo as linhas `on` e `off` aparecem em
sequência, não sobrepostas; o resumo em tabela é o que mostra os dois lado a lado.

### Problemas comuns

| Sintoma | Causa e solução |
|---|---|
| k6 mostra `output: -` e nada aparece no Grafana | faltou `--out influxdb=http://localhost:8086/k6` no comando |
| Só uma linha (`on` ou `off`) no Grafana | uma das execuções rodou sem `--out`, ou o período do Grafana não cobre as duas |
| Números com e sem cache quase iguais | a API não foi reiniciada com `Cache__Enabled=false` (o `keyspace_hits` subiu no passo 6), ou o catálogo tem poucos jogos |
| `seed-games.sh`: "A API não respondeu" | a API do terminal 1 não está no ar (ou está em outra porta: use `BASE_URL`) |
| Cadastro manual devolve `Release date cannot be in the past` | a API exige data de lançamento futura; o script já usa um ano à frente |
| `dotnet ef database update` não conecta em `localhost:5432` | o container `catalogdb` foi criado sem a porta publicada: rode `docker compose up -d catalogdb` para recriá-lo (os dados ficam no volume) |
| `docker compose -f docker-compose.monitoring.yml ...` recusa rodar | falta a `GRAFANA_ADMIN_PASSWORD` no terminal (ver passo 1) |
| Docker Desktop (macOS/Windows): k6 não alcança a API | o `--network host` é do Linux; use `-e BASE_URL=http://host.docker.internal:5140` e `--out influxdb=http://host.docker.internal:8086/k6` |

### Limpar depois

```bash
docker exec catalogdb psql -U postgres -d catalogdb -c "DELETE FROM games WHERE title LIKE 'Jogo de carga %'"
GRAFANA_ADMIN_PASSWORD=x docker compose -f docker-compose.monitoring.yml down   # -v apaga também as métricas
```

Ao apagar jogos direto no banco, o cache não é invalidado: as listagens em cache expiram sozinhas em até 3 minutos.

---

## CI

O workflow `.github/workflows/test.yml` roda em todo pull request:

| Job | O que faz |
|---|---|
| `test` | Faz restore e build em Release e roda os testes unitários (`Domain`, `Application`) e de integração (`Infrastructure`, `API`). PostgreSQL e Redis dos testes vêm do Testcontainers. |
| `container-test` | Roda só se o `test` passar (`needs: test`). Faz o build da imagem pelo `src/CatalogAPI.API/Dockerfile`, sobe PostgreSQL, Redis, RabbitMQ e a API numa rede Docker e faz um smoke test em `GET /api/v1/games` (espera HTTP 200). Os logs da API são exibidos sempre. |

### Deploy contínuo

Depois do merge, a entrega fica no `.github/workflows/ecr-push.yml`: todo push na `main` passa pelos testes,
publica a imagem no ECR e atualiza o `catalog-api` no EKS por rolling update, sem derrubar o serviço.

| Job | Quando roda | O que faz |
|---|---|---|
| `Build (PR)` | pull request | só builda a imagem, sem tocar na AWS |
| `Build e testes unitários` | push na `main` ou tag `v*` | restore, build em Release e testes de `Domain` e `Application`. Se falhar, nada é publicado |
| `Build e push no ECR` | depois dos testes | publica `fcg/catalog-api:sha-<commit>` (na `main`) ou `fcg/catalog-api:v*` (na tag) |
| `Deploy no EKS` | depois do push | `kubectl set image` no deployment `catalog-api` do namespace `fcg` e espera o `rollout status` (até 5 min) |

- **Tags:** `sha-*` expira em 15 dias pela lifecycle do ECR; `v*` nunca expira. Para uma release:
  `git tag v1.2.3 && git push origin v1.2.3`. As tags do ECR são imutáveis, então uma tag já publicada não é
  sobrescrita (o push é pulado e o deploy usa a imagem existente).
- **Zero downtime:** o deployment usa `RollingUpdate` com `maxUnavailable: 0`. O pod novo só recebe tráfego
  depois da readiness probe; se ele não ficar pronto, os antigos continuam atendendo e o job falha.
- **Cluster desligado:** o cluster EKS é efêmero. Sem ele, o job avisa (*"Cluster fcg desligado; deploy
  pulado"*) e termina verde; a imagem entra no próximo `make workloads-deploy` do Orchestration.
- **Credenciais:** OIDC com a role `fcg-gha-catalog-api` (variável de repositório `AWS_ROLE_ARN`), sem access
  key no GitHub. A role publica só no `fcg/catalog-api` e, no cluster, edita só o namespace `fcg`
  (`github-oidc.yaml` e `eks.yaml` do Orchestration).
- **Refazer um deploy sem mudar código:** na aba Actions, abra a execução do `ecr-push` do commit desejado e
  use *Re-run all jobs*.

### Lint

O workflow `.github/workflows/lint.yml` roda em todo pull request: executa o `dotnet format` com as regras do `.editorconfig`, faz o commit da formatação na própria branch do PR e depois checa o resultado. Ele **não bloqueia o merge**: o que não puder ser corrigido automaticamente aparece como aviso.

Quando o bot fizer esse commit, rode `git pull` antes de continuar na branch. Para formatar localmente antes de subir:

```bash
dotnet format
```

### Segurança

O workflow `.github/workflows/security.yml` roda em todo pull request, em push na `main`, toda segunda-feira (CVEs novas aparecem sem mudança no código) e manualmente pela aba Actions. Ele funciona em **modo relatório**: mostra o que encontrou no resumo da execução (Summary) e como avisos no PR, mas **nunca falha nem bloqueia o merge**.

| Job | O que verifica | Ferramenta |
|---|---|---|
| `dependencies` | Pacotes NuGet com vulnerabilidade conhecida, inclusive transitivos | `dotnet list package --vulnerable` |
| `image` | Pacotes do sistema e do .NET na imagem Docker (MEDIUM ou maior, com correção disponível) | Trivy |
| `secrets` | Chaves, tokens e senhas commitados em todo o histórico do git | Gitleaks |
| `codeql` | Falhas de segurança no código C# (alertas na aba **Security** do repositório) | CodeQL |

O `.github/dependabot.yml` abre PRs semanais atualizando pacotes NuGet, a imagem base do Dockerfile e as GitHub Actions.

Para rodar os mesmos scans localmente (só precisa de Docker):

```bash
# Dependências
dotnet list package --vulnerable --include-transitive

# Imagem
docker build --pull -f src/CatalogAPI.API/Dockerfile -t catalog-api:scan .
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy:0.76.1 image --severity MEDIUM,HIGH,CRITICAL --ignore-unfixed catalog-api:scan

# Segredos
docker run --rm -v "$PWD:/repo:ro" zricethezav/gitleaks:v8.30.1 git /repo --redact
```

---

## Configuração

Todas as chaves abaixo podem ser definidas no `appsettings.json` ou sobrescritas por **variáveis de
ambiente** (usando `__` como separador de seção — ex.: `RabbitMq__Host`, `ConnectionStrings__DefaultConnection`).

### JWT (`JwtSettings`)

```json
"JwtSettings": {
  "SecretKey": "<chave HMAC compartilhada com a UsersAPI>"
}
```

> **Obrigatório.** A API lança exceção no startup se `JwtSettings:SecretKey` não estiver presente. A chave
> precisa ser **idêntica** à usada pela UsersAPI, pois a assinatura do token é validada offline (ver _Autenticação_).

### RabbitMQ (`RabbitMq`)

O pacote `FiapCloudGames.RabbitMq` lê a seção `RabbitMq` (ou variáveis com o prefixo `RabbitMq__`):

```json
"RabbitMq": {
  "Host": "rabbitmq",
  "Port": 5672,
  "Username": "fcg",
  "Password": "fcg123",
  "VirtualHost": "/"
}
```

No `appsettings.Development.json` o `Host` é `localhost`. No `compose.yaml` esses valores são
sobrescritos por variáveis de ambiente (`RabbitMq__Host=rabbitmq`, `RabbitMq__Username=guest`, etc.).

> ℹ️ As credenciais variam por ambiente: o `appsettings.json` usa `fcg/fcg123` (padrão da plataforma),
> enquanto o `compose.yaml` provisiona os containers de Postgres e RabbitMQ com `postgres/pass` e
> `guest/pass` e injeta essas credenciais na API via variáveis de ambiente. Ao rodar via Docker, valem
> os valores do compose; fora do Docker, valem os do `appsettings`.

---

## Executando

### Com Docker (recomendado)

```bash
docker compose up -d --build

dotnet ef database update --project src/CatalogAPI.Infrastructure --startup-project src/CatalogAPI.API
```

Isso sobe quatro serviços:

| Serviço | Porta (host:container) |
|---|---|
| CatalogAPI | `5001:8080` |
| PostgreSQL 18 | `5432:5432` |
| Redis 7 (cache) | `6379:6379` |
| RabbitMQ 4 | `5672:5672` / `15672:15672` (management) |

A API ficará disponível em `http://localhost:5001`. O container roda com
`ASPNETCORE_ENVIRONMENT=Development`, então o **Swagger fica ativo** em `http://localhost:5001/swagger`.
