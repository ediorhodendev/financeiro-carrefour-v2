# ADR 004-messaging: RabbitMQ e recuperação

Status: aceito.

## Contexto
Demonstrar integração assíncrona com falhas parciais.

## Decisão
Mensagens persistentes, publisher confirms, filas quorum, inbox e DLQ. Outbox usa batch e SKIP LOCKED.

## Consequências e alternativas
Pode haver duplicatas e reordenação. Staging permite prefixo contínuo. 20 ciclos de falha levam à quarentena; exige operação para convergência. Kafka não é necessário ao volume demonstrado.
