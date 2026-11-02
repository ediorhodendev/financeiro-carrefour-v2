# financeiro-carrefour-v2

Desafio de ledger financeiro em C# 14 / .NET 10, desenvolvido para demonstrar decisões explícitas de consistência, concorrência e recuperação. Esta versão usa **Controllers com `[ApiController]`**, CQRS leve e dois contextos internos com três processos: Ledger.Api, Balance.Api e Balance.Worker.

Não substitui nem altera o projeto financeiro-carrefour anterior.

## Executar

Pré-requisito: Docker Desktop com containers Linux e Compose v2, ou Docker Engine com Compose v2. Na raiz:

```sh
docker compose up --build -d
docker compose ps -a
```

Não precisa instalar .NET nem criar `.env` para a demonstração. A configuração local usa senhas de demonstração sobrescrevíveis pelo ambiente e gera uma chave JWT aleatória em um volume privado no primeiro início. As migrations criam o esquema antes das APIs. `migrations` com `Exited (0)` significa sucesso.

| Processo/recurso | Endereço |
|---|---|
| Ledger.Api / Scalar | http://localhost:5081/scalar/v1 |
| Balance.Api / Scalar | http://localhost:5082/scalar/v1 |
| OpenAPI nativo | `/openapi/v1.json` em cada API |
| RabbitMQ Management | http://localhost:15673 |

O banco e a porta AMQP não são publicados no host. A interface do RabbitMQ usa usuário `bank` e senha local `local-demo-rabbit-only`. PostgreSQL usa `bank` / `local-demo-postgres-only`, somente na rede Docker. Os valores em `.env.example` são opcionais. Eles não são credenciais de produção. Alterar variáveis de senha não rotaciona uma senha já persistida em volume existente.

## Começar pelo acesso demonstrativo

Execute `POST http://localhost:5081/demo/access`, sem body. Copie `accountId` e `token`. No Scalar, use a autenticação Bearer com esse token nas duas APIs.

O token tem os escopos `ledger.write` e `balance.read`, identifica o cliente fictício `demo-customer` e expira em uma hora. A conta inicial começa zerada. O endpoint só funciona em Development e com `Demo__Enabled=true`. Um provedor OIDC real é obrigatório fora do desenvolvimento.

```sh
curl -X POST http://localhost:5081/demo/access
```

Para os exemplos seguintes, configure o token e a conta retornados:

```sh
export TOKEN='cole-o-token'
export ACCOUNT_ID='cole-o-accountId'
```

## Crédito, débito e estorno

Use valores positivos tanto para crédito quanto para débito. `tipo` recebe exatamente `Credito` ou `Debito`. O contrato HTTP de entrada usa nomes em português; classes e identificadores C# estão em inglês. Respostas usam `type=1` para crédito e `type=2` para débito.

Crédito:

```sh
curl -i -X POST "http://localhost:5081/api/v1/contas/$ACCOUNT_ID/lancamentos"   -H "Authorization: Bearer $TOKEN"   -H 'Content-Type: application/json'   -H 'Idempotency-Key: first-credit'   -d '{"tipo":"Credito","valor":100,"descricao":"Credito inicial","referenciaExterna":"DEMO-001"}'
```

Débito:

```sh
curl -i -X POST "http://localhost:5081/api/v1/contas/$ACCOUNT_ID/lancamentos"   -H "Authorization: Bearer $TOKEN"   -H 'Content-Type: application/json'   -H 'Idempotency-Key: first-debit'   -d '{"tipo":"Debito","valor":30,"descricao":"Pagamento"}'
```

O saldo autoritativo passa a ser 70 BRL. Guarde o `id` do lançamento para um eventual estorno.

Estorno integral:

```sh
curl -i -X POST "http://localhost:5081/api/v1/contas/$ACCOUNT_ID/lancamentos/ID-DO-LANCAMENTO/estorno"   -H "Authorization: Bearer $TOKEN"   -H 'Content-Type: application/json'   -H 'Idempotency-Key: first-reversal'   -d '{"motivo":"Lancamento registrado incorretamente"}'
```

### Regras financeiras

- Valores usam `decimal` / `NUMERIC(19,4)`: até quatro casas decimais, sem arredondamento silencioso.
- Valor máximo e saldo máximo por conta: `999999999999999.9999` BRL.
- Débito não pode deixar saldo negativo. Não há cheque especial nesta versão.
- Descrição obrigatória de 1 a 500 caracteres; referência externa opcional de até 128. Descrição e referência são protegidas em repouso na escrita.
- O horário de registro é atribuído pelo PostgreSQL depois do bloqueio da conta. Todas as datas são UTC. Não há agendamento nem lançamento retroativo.
- Ledger append-only: UPDATE e DELETE de lançamentos são bloqueados pelo EF e por trigger no banco.
- Estorno cria outro lançamento do mesmo valor e tipo oposto, vinculado ao original. Nunca altera o original.
- Motivo de estorno obrigatório, entre 1 e 500 caracteres após normalização pelo domínio.
- Não há estorno parcial, estorno de estorno nem dois estornos para a mesma original.
- Estornar crédito exige saldo para o débito compensatório. Estornar débito respeita o limite máximo do saldo.
- Em caso de rejeição, nenhuma movimentação nem resposta idempotente de sucesso fica confirmada.

## Endpoints

| Método e rota | Processo | Finalidade |
|---|---|---|
| `POST /demo/access` | Ledger | Acesso local fictício, somente Development. |
| `POST /api/v1/contas` | Ledger | Abre uma conta BRL para o cliente autenticado; sem body. Permite múltiplas contas. |
| `POST /api/v1/contas/{contaId}/lancamentos` | Ledger | Crédito ou débito atômico; exige chave idempotente e escopo `ledger.write`. |
| `POST /api/v1/contas/{contaId}/lancamentos/{id}/estorno` | Ledger | Compensação integral com motivo e chave idempotente. |
| `GET /api/v1/contas/{contaId}/saldo?em=` | Balance | Saldo projetado atual ou histórico da conta. |
| `GET /api/v1/contas/{contaId}/extrato?de=&ate=&pagina=&tamanho=` | Balance | Extrato do prefixo processado, em ordem de sequência. |
| `GET /api/v1/clientes/me/saldo?em=` | Balance | Soma das contas BRL do cliente autenticado, em uma visão consistente do banco de leitura. |
| `GET /health/live` | Ambas | Processo respondendo. |
| `GET /health/ready` | Ambas | Acesso ao esquema necessário no PostgreSQL. |

Consultas exigem `balance.read` e propriedade derivada do JWT. Conta inexistente e conta de outro cliente têm o mesmo resultado 404. As APIs não aceitam o proprietário financeiro enviado pelo usuário.

O cadastro de conta é um apoio mínimo ao teste; não tem promessa de replay idempotente. Os endpoints financeiros de lançamento e estorno têm essa garantia. Não há transferências ou cadastro de dados pessoais.

## Idempotência de ponta a ponta

`[Idempotent]` é um ActionFilter reutilizável. A transação envolve reserva da chave, caso de uso e armazenamento da resposta serializada.

| Caso | Resposta |
|---|---|
| Chave ausente, vazia ou maior que 128 caracteres | 400 ProblemDetails |
| Chave nova e operação válida | 201 |
| Mesma conta, chave, operação e corpo já concluídos | Mesmo status **201** e mesmo corpo; `Idempotency-Replayed: true` |
| Mesma chave com outro corpo/operação | 422 |
| Chave disputada por requisição em andamento | 409 e `Retry-After: 1`; repita a mesma intenção |

O hash SHA-256 usa o caminho/método e os bytes UTF-8 do body. Mudança de espaços, ordem de propriedades ou representação decimal muda o hash: reenvie o mesmo body. Não há SELECT/INSERT desprotegido: a reserva usa INSERT com índice único `(scope,key)` e espera limitada para a disputa.

Chaves concluídas expiram após 48 horas e são removidas em lotes por serviço de limpeza. **Após a remoção, reutilizar a chave pode criar outra operação**. A chave não substitui uma referência de negócio permanente; clientes devem gerar uma chave nova por intenção e manter retries dentro da janela. Referência externa não tem unicidade financeira nesta versão.

No consumidor, inbox, staging do evento e avanço da projeção compartilham transação. Reentrega do mesmo `MessageId` não altera o saldo duas vezes.

## Consistência de leitura e histórico

O saldo autoritativo do Ledger é atualizado sincronamente e protege a autorização dos débitos. O saldo do Balance é uma projeção **eventualmente consistente**, atualizada pelo worker. O evento não credita novamente a conta autoritativa.

Cada resposta de saldo informa `lastSequence` e `processedAt`. Compare `lastSequence` com `sequence` retornada pelo POST para verificar se a conta já incorporou a operação. `processedAt` é a última atualização local, não uma promessa de que todos os eventos globais foram consumidos.

Uma conta sem eventos ainda não tem projeção: a consulta por conta retorna 404 até o primeiro processamento; a consolidação inclui somente contas disponíveis na projeção. Um valor consolidado pode estar atrasado em relação à escrita. Não usar essa consulta eventual para autorizar gastos.

Eventos fora de ordem são armazenados, mas só um prefixo contínuo é exposto. Snapshots são gerados a cada 100 lançamentos contíguos por conta. Saldo histórico = último snapshot até `em` + soma das movimentações posteriores até esse instante, limitado à posição processada.

Use `em=2026-11-02T12:00:00Z` com um horário real da sua conta. Datas futuras, anteriores à abertura ou offsets diferentes de UTC são rejeitados. Na consolidação histórica, contas abertas depois do instante são excluídas.

O histórico é por horário de registro, não um snapshot permanente da visibilidade de commits em relógio de parede. O extrato usa `pagina` a partir de 1 e `tamanho` de 1 a 100, padrão 50. Cada consulta usa Repeatable Read; páginas sucessivas não constituem um snapshot fixo.

## Resiliência e observabilidade

PostgreSQL usa bloqueio pessimista por conta e limites de espera. Lançamento, saldo, Outbox e finalização da chave têm commit único. RabbitMQ fora do ar não impede o commit financeiro.

Publicador: lotes, mensagens persistentes, confirmação do broker, backoff exponencial com jitter, timeout e circuit breaker via Polly. Eventos com falhas persistentes são preservados em quarentena na Outbox após 20 ciclos; veja o runbook antes de republicar.

Consumidor: ACK somente após commit. Mensagens inválidas ou repetidamente malsucedidas vão para `bank.balance.dead.v1`. Transporte pelo menos uma vez, sem promessa de exatamente uma vez. A fila principal é `bank.balance.v1`.

Logs Serilog em JSON não incluem bodies, JWTs, query strings nem identificadores de clientes. OpenTelemetry fornece tracing e métricas HTTP; configure `OTEL_EXPORTER_OTLP_ENDPOINT` para exportar para seu collector. O Compose básico não inclui uma plataforma de observabilidade completa.

JWT por escopo, propriedade em cada acesso, rate limiting e ProblemDetails. Fora de Development exige uma autoridade JWT real. Consulte as limitações de segurança e operação nos documentos.

## Testes

Com SDK .NET 10.0.403 e Docker funcionando:

```sh
dotnet restore Financial.slnx
dotnet build Financial.slnx -c Release
dotnet test Financial.slnx -c Release
```

Integração usa Testcontainers com PostgreSQL e RabbitMQ reais e `WebApplicationFactory`. Não há skip silencioso se Docker estiver indisponível. Os testes criam contas e containers isolados.

Alternativa com SDK dentro de container e acesso ao Docker local:

```sh
docker compose --profile tests run --build --rm tests
```

Esse perfil monta o socket Docker para Testcontainers; use apenas em ambiente de desenvolvimento confiável. O GitHub Actions executa testes no host e um smoke test dos três processos no Compose, incluindo indisponibilidade e recuperação do broker.

Os testes cobrem precisão monetária, limite de saldo, estorno, hashes, replay exato, payload conflitante, 50 chamadas com a mesma chave, 100 débitos concorrentes, imutabilidade, criptografia de texto, reentrega, eventos fora de ordem, snapshot e consultas HTTP. A execução de entrega aprovou 20 testes unitários e 12 de integração, além do smoke test no Compose, com build sem erros nem warnings. Veja a [evidência de validação](docs/validation.md).

## Atualizar, parar ou zerar

```sh
git pull --ff-only
docker compose up --build -d
docker compose logs -f ledger-api balance-api balance-worker
docker compose down
```

Para começar do zero, **apagando os dados, filas e chaves locais**:

```sh
docker compose down -v
docker compose up --build -d
```

Excluir o volume de chaves isoladamente pode tornar os textos criptografados ilegíveis. Backup dos dados precisa incluir uma estratégia segura de recuperação das chaves.

## Documentação e decisões

- [Arquitetura e fluxos](docs/architecture.md)
- [Regras de estorno](docs/reversals.md)
- [Segurança](docs/security.md)
- [Operação, diagnóstico e recuperação](docs/operations.md)
- [ADRs](docs/adr/)

## O que faria com mais tempo

Particionamento temporal do ledger, snapshots e arquivamento medidos por volume; processo formal de reconciliação e reparação de projeções; testes de caos e SLOs de atraso; segregação de usuários/runtime e banco por contexto; PostgreSQL e RabbitMQ replicados; backup/PITR e ensaios de DR/multi-região; KMS/HSM e criptografia de chaves com rotação auditada; automação de governança de eventos.

Event Sourcing e Kafka são alternativas a avaliar com requisitos concretos de replay, volume e consumidores. Não foram incluídos por prestígio tecnológico. O ledger atual não representa contabilidade de partidas dobradas: não há plano de contas, balanço contábil nem integração de liquidação. É uma implementação técnica demonstrável, não um core bancário certificado e pronto para produção.
