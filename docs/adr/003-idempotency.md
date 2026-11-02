# ADR 003-idempotency: Idempotência HTTP e inbox

Status: aceito.

## Contexto
Respostas podem se perder depois do commit e eventos podem ser reentregues.

## Decisão
ActionFilter reserva com INSERT e índice único, hash SHA-256 de operação/body bruto, resposta exata; inbox única no mesmo commit da projeção.

## Consequências e alternativas
Disputa não confirmada espera até 750ms, depois 409/Retry-After. 48h de retenção. Canonicalização JSON foi descartada para manter semântica explícita de replay byte a byte.
