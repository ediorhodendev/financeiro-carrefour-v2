# ADR 009-read-consistency: Projeção contígua e consolidação

Status: aceito.

## Contexto
Eventos fora de ordem não podem produzir um saldo intermediário enganoso.

## Decisão
Persistir staging e inbox; drenar apenas sequência esperada; snapshots e extrato limitados ao prefixo. Consolidar contas por cliente em Repeatable Read.

## Consequências e alternativas
Não há barreira de consumo global; conta nova fica sem projeção até seu primeiro evento. Sincronização forte exigiria contrato de espera/timeout ou consulta ao autoritativo.
