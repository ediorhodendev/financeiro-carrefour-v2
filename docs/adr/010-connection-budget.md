# ADR 010: Orçamento global de conexões

Status: aceito.

## Contexto

O teste de 100 débitos simultâneos expôs SQLSTATE 53300 (too_many_connections). Pools independentes com máximos padrão podem somar mais conexões que o limite do PostgreSQL mesmo quando o bloqueio financeiro está correto.

## Decisão

Compartilhar NpgsqlDataSource entre EF e Outbox no Ledger. Limitar Ledger a 40 conexões, Balance.Api a 20 e Balance.Worker a 20, deixando reserva operacional no servidor padrão. Chamadas excedentes aguardam o pool com timeout limitado. Database__MaximumPoolSize pode ser configurado por processo.

## Consequências

O orçamento é global: multiplicar réplicas exige recálculo. Não aumentar max_connections nem aceitar indiscriminadamente 503 para aprovar o teste. PgBouncer e backpressure no gateway são alternativas para volume maior; não substituem transações curtas, índices, limites e medições de espera.
