# Validação da entrega

Execução em 02/11/2026, GitHub Actions, Ubuntu 24.04:

https://github.com/ediorhodendev/financeiro-carrefour-v2/actions/runs/41527658292

Código validado no commit `3aad99d0beec4112f25dc4bc3133ced2aaecc626`. Alterações posteriores apenas de documentação não alteram essa evidência do código.

| Verificação | Resultado |
|---|---|
| Restore e build Release da solution .NET 10 | Sucesso, zero erros e zero warnings |
| Domínio e aplicação | 20 testes aprovados, nenhum skip |
| Integração com Testcontainers | 12 testes aprovados, nenhum skip |
| 50 requisições simultâneas com uma chave | Um lançamento no ledger |
| 100 débitos simultâneos | 50 aprovados sobre saldo de 50, saldo final zero e igual à soma do ledger |
| Replay HTTP | Mesmo status 201 e corpo original |
| Corpo diferente com a mesma chave | 422 |
| Estorno | Integral, único, idempotente e com exigência de saldo |
| Inbox | Reentrega não duplica o efeito |
| Histórico | Snapshot e soma após o corte, com eventos fora de ordem |
| Imutabilidade e proteção de texto | Verificadas no PostgreSQL real |
| Docker Compose com três processos | Inicialização e migrations aprovadas |
| Smoke test | Crédito, débito, estorno, projeção, queda e recuperação do broker |

O smoke test terminou com saldo projetado de 105 BRL após recuperação. A saída literal registrada foi:

```text
PASS: Controllers, exact idempotent replay, full reversal, read projection, broker outage and recovery. Final balance 105 BRL.
```

TRX e logs do Compose estão no artefato `validation-results` dessa execução. A passagem de testes não garante ausência de defeitos nem certificação bancária. A concorrência exercitada não representa uma medição de capacidade máxima em produção.
