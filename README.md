# financeiro-carrefour-v2

Desafio de ledger financeiro em C# 14 / .NET 10, desenvolvido para demonstrar decisões explícitas de consistência, concorrência e recuperação. Esta versão usa **Controllers com `[ApiController]`**, CQRS leve e dois contextos internos com três processos: Ledger.Api, Balance.Api e Balance.Worker.

Não substitui nem altera o projeto financeiro-carrefour anterior.

## Frontend Angular disponível

A versão 2 também pode ser testada pela interface em **Angular 22**, disponível em um repositório separado:

**Frontend:** [financeiro-carrefour-web](https://github.com/ediorhodendev/financeiro-carrefour-web)

O painel permite registrar créditos e débitos, consultar saldo atual e histórico, navegar no extrato, realizar estorno integral e acompanhar a mensageria com métricas do RabbitMQ e o avanço da projeção de saldo.

Com este backend já rodando no Docker, execute em outro terminal, na pasta onde deseja clonar o frontend:

```sh
git clone https://github.com/ediorhodendev/financeiro-carrefour-web.git
cd financeiro-carrefour-web
docker compose up -d --build --wait
```

Abra **http://localhost:4200** e clique em **Conectar ambiente** para obter o acesso demonstrativo. Não é necessário parar o backend nem instalar Angular ou Node localmente.

O frontend utiliza a rede Docker `financeiro-carrefour-v2_default`. Se você personalizou a senha do RabbitMQ, configure a mesma senha no `.env` do frontend para habilitar as métricas. Os comandos de execução, exemplos de testes e detalhes da integração estão no [README do frontend](https://github.com/ediorhodendev/financeiro-carrefour-web#readme).

## Por que esta arquitetura foi escolhida

A v2 separa **correção financeira** de **distribuição dos dados para leitura**. O Ledger autoriza os débitos usando seu saldo transacional; o Balance oferece consultas sem participar da decisão de gastar. Assim, o processamento assíncrono pode atrasar sem permitir um débito com base em saldo desatualizado.

| Escolha | Problema resolvido e custo assumido |
|---|---|
| Monólito modular com três processos | Contextos com responsabilidades explícitas no mesmo repositório e banco com schemas separados. Permite separar execução de escrita, leitura e consumo, com mais componentes para operar. Não são microsserviços independentes. |
| Controllers e `[ApiController]` | Atendem ao requisito do desafio e oferecem organização por actions, model binding e validação do contrato HTTP. Regras financeiras permanecem nos handlers e no domínio. |
| Clean Architecture e DI | Domínio não depende de HTTP, EF ou RabbitMQ. Aplicação define contratos; infraestrutura implementa. Isso permite testar regras sem depender do transporte. |
| CQRS leve, EF Core na escrita e Dapper na leitura | Escrita usa entidades e transação; leitura usa projeções SQL explícitas. Não exige MediatR, nem pressupõe ganho de desempenho sem medição. |
| Lock pessimista por conta | Serializa operações da mesma conta para impedir decisões concorrentes sobre o mesmo saldo. Contas muito disputadas podem esperar ou receber conflito transitório; os tempos de espera são limitados. |
| Ledger append-only e estorno | Preserva evidência da original. A correção é outro lançamento, com vínculo e regras de saldo; histórico anterior não é reescrito. |
| ActionFilter idempotente | Mantém o mecanismo de replay fora da regra financeira, mas dentro da mesma transação que confirma a operação e sua resposta. Índice único protege a corrida entre requisições. |
| Transactional Outbox e RabbitMQ | Evitam o intervalo desprotegido entre confirmar no banco e tentar publicar um evento. A entrega pode se repetir; a inbox torna o efeito do consumidor idempotente. |
| Projeção contígua e snapshots | Eventos fora de ordem são armazenados sem expor um saldo com lacunas. Snapshots reduzem o trecho de histórico a agregar; a leitura continua eventual. |
| Pools limitados e compartilhados | Ledger compartilha o DataSource entre EF e Outbox; limites de 40/20/20 deixam reserva no banco para uma instância de cada processo. Réplicas exigem recalcular o orçamento global. |

A transação de escrita confirma **saldo autoritativo + lançamento + Outbox + resposta idempotente**. O consumidor confirma **inbox + evento projetado + avanço de saldo/snapshots** em sua própria transação. Não há transação distribuída entre PostgreSQL e RabbitMQ nem promessa de transporte exatamente uma vez.

A opção por três processos demonstra recuperação e independência de execução, mas acrescenta consistência eventual e trabalho operacional. Para um escopo menor com consulta imediatamente consistente no mesmo banco, a [versão 1](https://github.com/ediorhodendev/financeiro-carrefour) é uma alternativa mais simples. Os contratos das duas versões diferem; use o README correspondente. As justificativas completas e alternativas descartadas estão em [docs/adr](docs/adr/).

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

## Roteiro manual de avaliação: payloads e resultados esperados

### Preparação sem apagar dados anteriores

1. Suba o Compose e abra o Scalar de escrita em http://localhost:5081/scalar/v1.
2. Execute `POST /demo/access` sem body e copie `token` e `accountId`.
3. Configure Bearer com esse token no Scalar de escrita **e** no Scalar de leitura em http://localhost:5082/scalar/v1.
4. Para reproduzir os valores abaixo, abra **uma conta nova** com `POST /api/v1/contas`, sem body e com o token. Copie o `id` retornado como `contaId` para todos os testes seguintes. A conta demonstrativa pode ter saldo de testes anteriores.
5. No terminal, se preferir curl, defina `TOKEN` e `ACCOUNT_ID` com os valores reais. Gere chaves novas se repetir o roteiro em uma conta já usada.

O cadastro retorna `201`. Uma conta nova ainda não tem projeção: antes do primeiro evento, o GET de saldo por conta pode retornar `404`. Isso é uma limitação explicitada desta implementação, não evidência de que o cadastro falhou.

### 1. Crédito de 100

Na Ledger.Api, use `POST /api/v1/contas/{contaId}/lancamentos` com `Idempotency-Key: credit-test-001`:

```json
{
  "tipo": "Credito",
  "valor": 100,
  "descricao": "Credito inicial de teste",
  "referenciaExterna": "TEST-001"
}
```

Espere `201`, `type: 1`, `amount: 100` e `sequence: 1`. Guarde `id`, `sequence` e `registeredAt`. O formato de entrada é `tipo`/`valor`; as respostas usam `type`/`amount`.

### 2. Replay idempotente e conflito de conteúdo

Reenvie **exatamente** o mesmo body, na mesma conta e com `credit-test-001`:

- Espere novamente `201`, com o mesmo corpo, ID, sequência e horário originais.
- O header `Idempotency-Replayed: true` identifica a repetição.
- Não deve surgir outro lançamento nem outro crédito.

Mantenha essa chave e altere somente `valor` para 101: espere `422` com ProblemDetails. O crédito original continua sendo o único registrado. Diferentemente da v1, replay concluído na v2 preserva o status original `201`; o hash usa o body bruto, portanto mudanças de espaços/ordem das propriedades também conflitam.

### 3. Débito de 30

No mesmo endpoint, com `Idempotency-Key: debit-test-001`:

```json
{
  "tipo": "Debito",
  "valor": 30,
  "descricao": "Debito de teste",
  "referenciaExterna": "TEST-002"
}
```

Espere `201`, `type: 2`, `amount: 30` e `sequence: 2`. Guarde o **ID desse débito**, seu `registeredAt` e a sequência.

### 4. Saldo atual, histórico e extrato

Na Balance.Api, execute `GET /api/v1/contas/{contaId}/saldo`, deixando `em` vazio. Após o consumo dos eventos, espere:

```json
{
  "accountId": "c90a3c2c-cdeb-4df0-85cb-d1a299a0d101",
  "currency": "BRL",
  "amount": 70.0000,
  "at": null,
  "lastSequence": 2,
  "processedAt": "2026-11-02T23:04:38.373059+00:00"
}
```

O ID e o horário acima são ilustrativos; a conta nova terá outro ID. Compare `lastSequence` com a sequência do débito. Se ainda for menor, aguarde e repita: o POST confirma a escrita, não a atualização imediata da leitura.

Para histórico, preencha `em` com o `registeredAt` real do crédito, incluindo `Z` ou `+00:00`. Se esse instante for anterior ao débito, espere saldo 100. A comparação é inclusiva; lançamentos com o mesmo instante também entram. Não use uma data fictícia anterior à abertura.

Para o extrato, execute:

```text
GET /api/v1/contas/{contaId}/extrato?pagina=1&tamanho=1
```

Espere o crédito na primeira página e o débito em `pagina=2`, depois de a projeção alcançar a sequência 2. Opcionalmente filtre `de` e `ate` com instantes UTC reais. `pagina=0` ou `tamanho=101` retorna `400`. A v2 usa página/tamanho; não recebe o cursor da v1.

Exemplos curl de consulta:

```sh
curl -sS "http://localhost:5082/api/v1/contas/$ACCOUNT_ID/saldo" \
  -H "Authorization: Bearer $TOKEN"

curl -sS -G "http://localhost:5082/api/v1/contas/$ACCOUNT_ID/saldo" \
  -H "Authorization: Bearer $TOKEN" \
  --data-urlencode "em=$CREDIT_REGISTERED_AT"

curl -sS "http://localhost:5082/api/v1/contas/$ACCOUNT_ID/extrato?pagina=1&tamanho=1" \
  -H "Authorization: Bearer $TOKEN"
```

No exemplo histórico, defina `CREDIT_REGISTERED_AT` com o horário retornado pelo crédito. Para consolidar as contas projetadas do cliente autenticado, use `GET /api/v1/clientes/me/saldo`. Se esse cliente já tiver outras contas com saldo, o consolidado não será apenas o saldo desta conta de teste.

### 5. Rejeitar débito sem saldo

Com saldo 70, envie uma chave nova, `insufficient-funds-001`, e:

```json
{
  "tipo": "Debito",
  "valor": 1000,
  "descricao": "Tentativa sem saldo suficiente"
}
```

Espere `409`. Saldo e extrato não mudam; a sequência continua 2. Uma falha de regra não vira resposta idempotente de sucesso.

### 6. Estornar o débito e preservar o histórico

Na Ledger.Api, execute `POST /api/v1/contas/{contaId}/lancamentos/{lancamentoId}/estorno`, usando o **ID do débito de 30**, com `Idempotency-Key: reversal-test-001`:

```json
{
  "motivo": "Estorno do debito de teste"
}
```

Espere `201`, um novo ID, `type: 1`, `amount: 30`, `sequence: 3` e `reversalOf` igual ao ID do débito. Depois do processamento, saldo 100 e `lastSequence: 3`. O extrato mantém crédito, débito e compensação; o débito original não desaparece.

| Tentativa de estorno | Resultado esperado |
|---|---|
| Mesmo original, body e chave | Mesmo `201` e corpo; saldo permanece 100 |
| Mesmo original com outra chave, `reversal-test-002` | `409`: original já estornada |
| Usar o ID da compensação como original, com chave nova | `409`: estorno de estorno proibido |
| Alterar o motivo com a chave original | `422`: conteúdo diferente |
| Adicionar `valor` ou `tipo` ao body do estorno, com chave nova | `400`: campos não aceitos; não há estorno parcial |

Para testar a exigência de fundos no estorno de crédito, prefira outra conta: credite 100, debite 80 e tente estornar o crédito de 100 com chave nova. Espere `409`, saldo 20 e nenhum lançamento compensatório. Isso evita alterar a sequência do roteiro principal.

### 7. Validação e autenticação

No endpoint de lançamentos, use uma chave nova para cada tentativa abaixo:

| Body/condição | Resultado |
|---|---|
| `{"tipo":"Credito","valor":0,"descricao":"Valor zero"}` | `400` |
| `{"tipo":"Debito","valor":-1,"descricao":"Negativo"}` | `400` |
| `{"tipo":"Credito","valor":10.12345,"descricao":"Cinco casas"}` | `400` |
| `{"tipo":"Creditoooo","valor":10,"descricao":"Tipo invalido"}` | `400` |
| `{"tipo":"Credito","valor":10,"descricao":""}` | `400` |
| Body válido sem `Idempotency-Key` | `400` |
| Requisição protegida sem token ou com token expirado | `401` |
| Token válido sem o escopo exigido | `403` |
| Conta que não pertence ao `sub` autenticado | `404` |

Até quatro casas são permitidas: `10.1234` é válido, diferentemente da v1, que admite até duas. Se registrar esse crédito, use outra conta ou recalcule os saldos esperados do roteiro. O token demo já contém os dois escopos; testar `403` exige outro token válido com escopos restritos, não apenas editar o JWT.

### 8. Ver a fila e demonstrar leitura eventual

Abra http://localhost:15673 com `bank` / `local-demo-rabbit-only` (ou a senha sobrescrita no ambiente). Em **Queues and Streams**, inspecione:

- `bank.balance.v1`: fila consumida pelo worker.
- `bank.balance.dead.v1`: mensagens inválidas ou com falhas repetidas.

Uma fila vazia é normal com consumidor ativo. Para enxergar o acúmulo, no terminal da v2 pare somente o worker:

```sh
docker compose stop balance-worker
```

No endpoint de lançamentos, use `Idempotency-Key: messaging-test-001` e:

```json
{
  "tipo": "Credito",
  "valor": 5,
  "descricao": "Teste de mensageria"
}
```

Espere `201` e `sequence: 4`. Após a publicação, a fila principal deve mostrar a mensagem em **Ready**, enquanto a leitura continua em 100/seq. 3. Isso pressupõe os passos anteriores, sem outras movimentações.

Reinicie o consumidor:

```sh
docker compose start balance-worker
docker compose logs -f ledger-api balance-worker
```

A fila deve drenar, e a leitura alcançar saldo 105/seq. 4. Não use **Get messages** para consumir/rejeitar manualmente a mensagem durante esse teste, pois isso interfere no fluxo.

### 9. Falha do broker e recuperação via Outbox

Com o worker novamente ativo, pare apenas o broker:

```sh
docker compose stop rabbitmq
```

Envie outro crédito de 5 no endpoint de lançamentos, com chave nova `broker-outage-test-001`:

```json
{
  "tipo": "Credito",
  "valor": 5,
  "descricao": "Credito durante indisponibilidade do broker"
}
```

Espere `201` e `sequence: 5`: o saldo autoritativo foi confirmado e o evento ficou na Outbox. O painel do RabbitMQ ficará indisponível enquanto o broker estiver parado, e a leitura pode continuar em 105/seq. 4.

```sh
docker compose start rabbitmq
```

Após a recuperação e as novas tentativas do publicador/consumidor, espere saldo 110/seq. 5. Não repita o crédito com outra chave para tentar acelerar a leitura; isso criaria outra operação. Pare o acompanhamento de logs com Ctrl+C, sem parar os serviços.

### 10. Concorrência e reentrega: testes automatizados

As propriedades que exigem simultaneidade são verificadas pela suíte de integração com banco e broker reais:

- 50 requisições com a mesma chave produzem exatamente um lançamento; respostas em disputa podem retornar `409` para retry.
- 100 débitos de 1 sobre saldo 50 aprovam 50 operações; o saldo final fica zero e igual à soma do ledger.
- Reentregar um evento não altera o saldo duas vezes.
- Eventos fora de ordem não fazem a leitura avançar sobre uma lacuna.

Para executar apenas a integração, com SDK .NET e Docker disponíveis:

```sh
dotnet test tests/Financial.IntegrationTests -c Release
```

Os saldos deste roteiro são expectativas condicionadas à conta nova e à sequência indicada; não são evidência de que os testes foram executados no seu ambiente. A evidência automatizada da entrega está em [docs/validation.md](docs/validation.md).

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


