# ADR 006-security: Dados e identidades

Status: aceito.

## Contexto
Limitar acesso e exposição de dados.

## Decisão
JWT por escopo e sub, Data Protection em texto livre, eventos mínimos, rate limit e logs sem payload.

## Consequências e alternativas
Demo não equivale a produção. Exigir OIDC real, TLS, KMS, menor privilégio e key backups antes de uso real.
