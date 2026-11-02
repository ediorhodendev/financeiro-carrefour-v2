# Estorno integral

POST de estorno identifica original por conta+ID. Proprietário deriva do JWT; body aceita apenas motivo. Com lock da conta: verificar original e ausência de estorno, derivar operação oposta e mesmo valor, aplicar regras de saldo, criar vínculo, gravar evento e finalizar idempotência na mesma transação.

Crédito original -> débito compensatório, exige fundos. Débito original -> crédito compensatório, respeita precisão e limite. Original imutável; sem estorno parcial ou de estorno. Unicidade de ReversalOf e trigger protegem também a persistência. Uma chave nova para original já estornada conflita; replay da mesma chave/corpo devolve a resposta original.

Falha não reserva sucesso: é permitido restaurar fundos e repetir a mesma intenção. O estorno afeta o histórico a partir de seu registro, sem reescrever posições anteriores.
