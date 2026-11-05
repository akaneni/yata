[English](CONTRIBUTING.md) · **Português**

# Contribuindo com o Yata

Obrigado por querer ajudar. Relatos de bug, correções e traduções são os mais bem-vindos, e forks também: se você fez algo na sua cópia, abra uma issue e conte — correções boas de forks voltam para cá, com crédito.

## Antes de começar

- **Bug?** Abra uma issue com o que aconteceu, o que você esperava, a versão do Windows e, se puder, o log de `%LOCALAPPDATA%\Yata\logs` (ele não guarda músicas, tarefas, eventos, caminhos nem contas).
- **Funcionalidade nova?** Abra uma issue antes e descreva a ideia. O Yata quer continuar pequeno: uma funcionalidade tem mais chance se combina com o que o notch já é.
- **Tradução?** Veja [Translations](README.md#translations) no README em inglês.

## O que combina com o Yata

- **Minimalista.** Uma coisa bem feita vale mais que cinco opções. Monocromático; a cor da capa do álbum é o único destaque.
- **Privado por padrão.** Sem conta, sem telemetria. O que vai para a rede é opcional, manda o mínimo possível e está em [PRIVACY.pt-BR.md](PRIVACY.pt-BR.md). O Yata não lê dados de outros apps (notificações, área de transferência, arquivos) sem o usuário saber.
- **Leve.** O notch é desenhado na CPU: cada animação nova ou janela maior custa frames. Meça antes e depois.
- **O Yata é o corvo.** Sem outros mascotes ou personagens.

## Pull requests

- **Um assunto por PR.** Uma correção, uma funcionalidade ou uma limpeza — não as três. PRs grandes misturando várias coisas não dá para revisar, e vamos pedir para dividir.
- **Testes** para a lógica que dá para testar sem janela (veja `tests/Yata.Tests`).
- **Tem que passar no que o CI roda:**

  ```powershell
  dotnet build -c Release -warnaserror
  dotnet test -c Release
  ```

- **Todo texto visível** vai em `Strings.resx` e `Strings.pt-BR.resx`.
- Não mude a versão nem o CHANGELOG: isso é feito no PR da versão.

## Fontes oficiais

O repositório oficial é [github.com/akaneni/yata](https://github.com/akaneni/yata) e o site é [getyata.app](https://getyata.app); os instaladores oficiais são gerados pelo GitHub Actions a partir deste repositório e podem ser conferidos com `gh attestation verify` (veja o README). Se você publicar builds de um fork, deixe claro que não são os oficiais.

Ao contribuir, você concorda que sua contribuição fica sob a [Licença MIT](LICENSE).
