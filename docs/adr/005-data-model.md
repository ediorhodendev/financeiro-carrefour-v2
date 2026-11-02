# ADR 005-data-model: Modelo monetário e histórico

Status: aceito.

## Contexto
Precisão financeira e consulta histórica verificável.

## Decisão
decimal/NUMERIC(19,4), ledger append-only, sequência por conta, snapshots a cada 100 registros contíguos.

## Consequências e alternativas
Snapshots são otimização, fonte de verdade permanece ledger. Datas de registro em UTC, sem backdating. Não é contabilidade de partidas dobradas.
