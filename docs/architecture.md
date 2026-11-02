# Arquitetura

Monólito modular em dois bounded contexts, implantado em três processos. Ledger é a fonte de verdade; Balance mantém projeções. Dependências: Domain <- Application <- Infrastructure; cada Api/Worker compõe suas dependências. Contratos de integração são separados do transporte RabbitMQ.

## Escrita e recuperação

```mermaid
flowchart TD
    A[Controller autenticado] --> B[ActionFilter idempotente]
    B --> C[Transação e bloqueio da conta]
    C --> D[Domínio valida e aplica]
    D --> E[Saldo, lançamento e Outbox]
    E --> F[Resposta idempotente e commit]
    B --> G[Replay ou conflito]
```

## Integração

```mermaid
flowchart TD
    A[Outbox pendente] --> B[Publisher com confirmação]
    B --> C[RabbitMQ]
    C --> D[Inbox e staging]
    D --> E[Avança sequência contínua]
    E --> F[Projeção e snapshots]
    F --> G[Commit e ACK]
    B --> H[Retry ou quarentena]
    C --> I[DLQ para falhas persistentes]
```

## Leitura

```mermaid
flowchart TD
    A[Balance Controller] --> B[Consulta Dapper]
    B --> C{Instante informado?}
    C -->|Não| D[Saldo materializado]
    C -->|Sim| E[Snapshot e lançamentos até o instante]
    D --> F[Resultado e posição processada]
    E --> F
```

Débitos nunca consultam o saldo eventual. A consolidação entre contas usa uma transação Repeatable Read no contexto Balance, mas não representa uma barreira de processamento global entre contas. Convergência exige recuperar eventos faltantes/quarentenados.

O worker usa polling limitado para simplificar a demonstração; consumidores push com prefetch e conexões separadas são evolução possível após medições. HTTP e AMQP podem confirmar antes de uma falha do processo; as chaves financeiras e inbox tornam retries seguros dentro das janelas documentadas.
