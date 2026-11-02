# ADR 007-deployment: Dois contextos e três processos

Status: aceito.

## Contexto
A lista do desafio define Ledger.Api, Balance.Api e Balance.Worker apesar da menção a dois processos.

## Decisão
Implantar três processos em uma solution/repositório e um Compose; PostgreSQL com schemas separados.

## Consequências e alternativas
É um monólito modular com implantação em processos separados, não microsserviços autônomos. Um banco simplifica a avaliação; isolamento operacional/usuários é evolução.
