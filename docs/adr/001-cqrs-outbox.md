# ADR 001-cqrs-outbox: CQRS leve e Outbox

Status: aceito.

## Contexto
Separar leitura de escrita e suportar falhas do broker.

## Decisão
Ledger mantém saldo autoritativo; Balance recebe eventos por Outbox. Commit único para ledger, saldo, evento e resposta idempotente.

## Consequências e alternativas
Leitura é eventual e deve informar sequência. Não prometer read-your-writes automático; cliente compara sequência.
