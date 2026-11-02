# Segurança

JWT valida emissor, audiência, assinatura, validade e escopo. Propriedade é verificada no banco a partir de sub. Cliente usa identificador opaco: não armazenar CPF, nome, email ou outros dados pessoais no sub/CustomerId. Descrição e referência externa não são necessárias à projeção nem são publicadas em eventos; na escrita são protegidas com ASP.NET Data Protection com propósito específico.

A demonstração mantém chave JWT aleatória e key ring em volume local com permissões de usuário. A proteção dos campos não protege o key ring contra um administrador do host. Produção deve usar storage cifrado, KMS/HSM ou certificado para proteção do key ring, TLS de ponta a ponta, identidades de serviço distintas, rotação e backups coordenados. Os volumes padrão do Docker não oferecem por si só garantia de criptografia física.

Não são logados bodies, tokens, claims, descrições, referências ou connection strings. Exceções inesperadas retornam mensagem genérica; logs de EF são limitados para evitar SQL parametrizado sensível. Rate limit por sub/IP limita abuso por instância e não substitui gateway distribuído.

Credenciais padrão são exclusivamente fictícias para execução local. Produção exige autoridade OIDC real (sem emissor demo), segredos pelo ambiente/secret store, menor privilégio, restrição de acesso ao broker e funções distintas para migrations/runtime. O Compose demonstrativo utiliza um usuário de banco comum e rede interna; não demonstra essa segregação de privilégios. Nenhum certificado bancário, SLA ou hardening completo é declarado.
