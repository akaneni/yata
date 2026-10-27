# Assinatura de código (SignPath Foundation)

**Situação:** a SignPath Foundation recusou o Yata em 27/10/2026. O programa pede sinais públicos que um projeto novo ainda não tem: estrelas, forks, contribuidores, artigos e discussões externas (Reddit, Stack Overflow, YouTube). O e-mail convida a pedir de novo quando o projeto tiver mais visibilidade. Até lá as versões saem sem assinatura, com o `SHA256SUMS.txt` e a comprovação de origem do GitHub (`actions/attest` no `release.yml`, conferida com `gh attestation verify`). Tudo abaixo continua valendo para quando a Foundation aprovar: sem a variável `SIGNPATH_ORGANIZATION_ID`, os passos de assinatura são pulados.

A [SignPath Foundation](https://signpath.org) assina de graça projetos de código aberto. O certificado é da Foundation, não de uma pessoa: no Windows, o signatário aparece como **SignPath Foundation**. A política pública fica em [getyata.app/code-signing](https://getyata.app/code-signing) (e [/pt/code-signing](https://getyata.app/pt/code-signing)); ao ser aprovado, ela volta a descrever a assinatura.

## Como funciona no release

O `release.yml` assina em duas etapas, sempre com aprovação manual no SignPath:

1. Compila o `Yata.exe` (`tools/build-installer.ps1 -PublishOnly`) e envia para o SignPath com a configuração `app`.
2. Coloca o `Yata.exe` assinado no instalador (`-SkipPublish`) e envia o instalador com a configuração `installer`.
3. Confere as duas assinaturas com `Get-AuthenticodeSignature` antes de gerar o `SHA256SUMS.txt`.

Cada pedido espera até 1 hora pela aprovação. Sem a variável `SIGNPATH_ORGANIZATION_ID`, essas etapas são puladas e o release sai sem assinatura, como antes.

O desinstalador gerado pelo Inno Setup (`unins000.exe`) não é assinado: ele nasce dentro do instalador, e o Inno só o assinaria com uma ferramenta local de assinatura.

## Configuração (uma vez)

1. **Pedir a vaga**: <https://signpath.org/apply>. Requisitos já atendidos no repositório: licença MIT, versão já publicada, build no GitHub Actions em máquinas do GitHub, política de assinatura no site (com o texto de atribuição e os papéis do time), política de privacidade mostrada no instalador e desinstalação completa.
2. **Autenticação em dois fatores** no GitHub e no SignPath para todos os papéis da política.
3. No SignPath, depois da aprovação:
   - projeto com slug `yata`, ligado ao repositório `akaneni/yata`, nome do produto **Yata**;
   - política de assinatura `release-signing` (a Foundation cria), com aprovação manual;
   - duas configurações de artefato, `app` e `installer`, com o conteúdo de [`.signpath/artifact-configurations/`](../.signpath/artifact-configurations/). Ao mudar uma, mude a outra cópia também;
   - um usuário de CI e o *API token* dele, com permissão de enviar pedidos no projeto `yata`.
4. No GitHub (Settings → Secrets and variables → Actions):
   - **variável** `SIGNPATH_ORGANIZATION_ID` com o ID da organização no SignPath;
   - **secret** `SIGNPATH_API_TOKEN` com o token do usuário de CI.
5. Instalar o GitHub App do SignPath no repositório, se a Foundation pedir.

## Restrições de metadados

As configurações só aceitam arquivos com nome de produto `Yata` e versão do produto igual à da tag. Para isso:

- o `Yata.csproj` não acrescenta o commit à versão (`IncludeSourceRevisionInInformationalVersion=false`), então a versão do produto é `0.1.2`, e não `0.1.2+abc123`;
- o `yata.iss` define `VersionInfoProductName`, `VersionInfoProductVersion` e `VersionInfoProductTextVersion`.

**A conferir no primeiro release assinado:** o Inno Setup preenche esses textos com espaços até um tamanho fixo (o nome do produto do instalador sai com 60 caracteres). A documentação do SignPath não diz se ele ignora esses espaços. Se o pedido do instalador for recusado por `product-name` ou `product-version`, ajuste a configuração `installer` no SignPath (e a cópia aqui) conforme o que o SignPath indicar na recusa.

## Depois do primeiro release assinado

- Trocar o aviso de SmartScreen no site (`dl.l4` em `site/index.html` e `site/pt.json`) e no README.
- Tirar a "Situação" das duas páginas de política de assinatura.
