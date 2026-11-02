# Operação e recuperação

## Inspeção

`docker compose ps -a`; `docker compose logs ledger-api balance-api balance-worker`; readiness em 5081/5082. RabbitMQ UI em 15673. Ledger pode estar ready mesmo com broker indisponível, pois a escrita ainda funciona. Worker em execução não comprova ausência de backlog.

Monitorar idade/quantidade de Outbox pendente, quarentena, profundidade de filas/DLQ, sequência pendente por conta, espera de locks e latência HTTP. processedAt não é watermark global nem prova de sincronização completa.

## Quarentena Outbox

Eventos com 20 ciclos falhos permanecem na tabela com dead_lettered_at. Corrija broker/contrato, investigue o registro e só então limpe dead_lettered_at, attempts e next_attempt_at para o evento selecionado, mantendo seu ID e payload. Não faça replay em massa sem investigação. Se um evento faltar, a projeção dessa conta para no prefixo anterior.

## DLQ

Preserve MessageId. Corrija a causa e reencaminhe de forma controlada; a inbox evita repetir efeito. Evento inválido corrigido requer governança: não sobrescreva fatos financeiros nem invente mensagens. Regerar do ledger deve preservar conta, sequência e ID originais.

## Reconstrução e reconciliação

Compare saldo autoritativo com soma assinada do ledger e com a projeção na mesma sequência. Backfill/rebuild automatizado não está implementado nesta entrega. Faça manutenção em janela controlada, em projeção nova, valide totais e só então troque o leitor. Nunca zere a inbox de produção com consumidores ativos.

## Limitações temporais

Histórico por registro confirmado visível, sem backdating. Snapshot a cada 100 registros contíguos. A perda do key ring impede leitura de textos protegidos. Chaves idempotentes expiram após 48h; retries fora da janela não têm deduplicação garantida. Referência externa não é deduplicação permanente.

## Orçamento de conexões

Ledger compartilha um único NpgsqlDataSource entre EF e Outbox, com máximo de 40 conexões. Balance.Api e Balance.Worker usam até 20 cada: orçamento de 80 para uma instância de cada processo, deixando reserva no PostgreSQL padrão. Requisições excedentes aguardam o pool em vez de abrir uma conexão por chamada. Antes de aumentar réplicas, recalcule o orçamento global e ajuste Database__MaximumPoolSize por processo, filas, timeouts e limite de conexões do servidor. A integração executa 100 débitos paralelos sem elevar max_connections para esconder o problema.
