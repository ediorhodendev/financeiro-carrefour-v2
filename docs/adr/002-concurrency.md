# ADR 002-concurrency: Lock pessimista por conta

Status: aceito.

## Contexto
Débitos simultâneos não podem exceder fundos.

## Decisão
SELECT FOR UPDATE depois de verificar propriedade; bloqueio dura até commit. Chaves e vínculos têm índices únicos.

## Consequências e alternativas
Conta muito quente serializa e pode ter timeout. Repetir a mesma chave, sem retry cego de regra financeira. Otimismo com retries foi descartado pela maior complexidade do caminho crítico.
