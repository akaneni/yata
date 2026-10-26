# Google Agenda: cadastro do app no Google

O Yata lê a agenda pelo fluxo OAuth recomendado pelo Google para apps instalados: navegador do sistema, redirecionamento para `http://127.0.0.1:<porta>`, PKCE (S256) e verificação de `state`. Não existe servidor do Yata no meio: o PC fala direto com o Google.

| O que | Como |
|---|---|
| Escopos | `openid`, `email`, `calendar.calendarlist.readonly`, `calendar.events.readonly` (só leitura) |
| Token guardado | só o *refresh token* e o e-mail, cifrados com DPAPI do usuário do Windows em `%LOCALAPPDATA%\Yata\google.dat` |
| Token de acesso | só na memória, renovado sozinho |
| Eventos | hoje e amanhã, só na memória, atualizados a cada 5 min |
| Desconectar | revoga o acesso no Google (`oauth2.googleapis.com/revoke`) e apaga o arquivo |

As credenciais do cliente OAuth **não ficam no repositório**. Os releases oficiais recebem as credenciais na compilação (secrets do GitHub). Quem compila o código-fonte cadastra o próprio cliente.

---

> O console muda de tempos em tempos. Os nomes abaixo são os do console em português; o equivalente em inglês aparece entre parênteses. Na dúvida, use a busca no topo do console.

## 1. Projeto

1. Entre em <https://console.cloud.google.com> com a conta que vai ser a **dona** do app. Prefira uma conta que você não vá perder; dá para adicionar outras pessoas como proprietárias depois.
2. Na primeira vez, aceite os termos. **Não precisa ativar faturamento**: a Calendar API é gratuita dentro da cota.
3. No seletor de projeto (topo, à esquerda do campo de busca) → **Novo projeto** (*New project*).
   - Nome: `Yata`. O ID (ex.: `yata-123456`) é gerado e não muda depois.
   - Organização: "Sem organização" numa conta Gmail pessoal está ok.
   - **Criar** e, quando terminar, selecione o projeto no seletor. Todos os passos seguintes valem para o projeto selecionado.

## 2. Ativar a Google Calendar API

1. Menu ☰ → **APIs e serviços** → **Biblioteca** (*Library*).
2. Busque "Google Calendar API" → abra → **Ativar** (*Enable*).

Sem isso, o Yata conecta mas mostra "A API do Google Agenda não está ativada no projeto".

## 3. Google Auth Platform: assistente inicial

Menu ☰ → **Google Auth Platform** → **Branding**. Num projeto novo aparece "Google Auth Platform ainda não configurada" → **Começar** (*Get started*). São 4 etapas:

1. **Informações do app:** nome do app `Yata`; e-mail de suporte do usuário (o seu, que aparece na tela de consentimento).
2. **Público-alvo** (*Audience*): **Externo** (*External*). "Interno" só existe para contas Google Workspace e limitaria o uso à sua organização.
3. **Informações de contato:** e-mail para avisos do Google sobre o projeto (verificação, problemas). Use um que você leia.
4. **Concluir:** marque que concorda com a *Google API Services: User Data Policy* → **Continuar** → **Criar**.

O app começa em **Teste** (*Testing*).

## 4. Branding (o que aparece na tela de consentimento)

**Google Auth Platform → Branding**, depois de criar:

- **Logotipo do app:** quadrado 120×120 px, PNG ou JPG, até 1 MB. Opcional, mas **enviar o logo obriga a verificação da marca** antes de ele aparecer. Para testar, deixe sem.
- **Domínio do app:** página inicial, política de privacidade e termos de serviço (opcional). Pode ficar vazio enquanto você só testa. É obrigatório para publicar e verificar (seção 10).
- **Domínios autorizados:** o domínio dessas páginas (ex.: `akaneni.github.io`).
- **Salvar.**

## 5. Escopos (Acesso a dados)

**Google Auth Platform → Acesso a dados** (*Data Access*) → **Adicionar ou remover escopos**.

No painel lateral, marque os itens abaixo. Se algum não aparecer na lista, cole o texto em "Adicionar escopos manualmente" no fim do painel:

| Escopo | Tipo |
|---|---|
| `openid` | não sensível |
| `https://www.googleapis.com/auth/userinfo.email` | não sensível |
| `https://www.googleapis.com/auth/calendar.calendarlist.readonly` | sensível |
| `https://www.googleapis.com/auth/calendar.events.readonly` | sensível |

→ **Atualizar** → **Salvar** no fim da página.

Os dois de agenda aparecem em "Seus escopos sensíveis", e é por eles que o app precisa de verificação para sair da tela de aviso. Os escopos de agenda só aparecem na lista se a Calendar API estiver ativada (seção 2).

Guarde para a verificação a justificativa de uso: *"Mostrar no notch do Windows os eventos de hoje e amanhã das agendas que o usuário escolher e avisar antes de cada um. Somente leitura; os dados ficam no computador do usuário e não são enviados a nenhum servidor."*

## 6. Usuários de teste

**Google Auth Platform → Público-alvo** (*Audience*) → **Usuários de teste** → **Adicionar usuários** → seu Gmail (e de quem mais for testar, até 100) → **Salvar**.

Em modo Teste:

- quem não está na lista recebe `Erro 403: access_denied`;
- quem está vê "O Google não verificou este app" → **Continuar**;
- **o acesso expira em 7 dias**, e o Yata pede para reconectar. É normal nessa fase.

## 7. Criar o cliente OAuth

**Google Auth Platform → Clientes** (*Clients*) → **Criar cliente** (*Create client*):

1. **Tipo de aplicativo:** **App para computador** (*Desktop app*). Não use "Aplicativo da Web": o Yata usa o redirecionamento por loopback (`http://127.0.0.1:<porta>`), que o tipo Desktop aceita em qualquer porta, sem URI cadastrada.
2. **Nome:** `Yata para Windows`. O nome só aparece no console.
3. Se aparecer uma opção sobre agentes de IA agindo em nome do usuário, **deixe desmarcada** (ela não pode ser mudada depois).
4. **Criar.**

Na janela que abre, **baixe o JSON na hora** (*Download JSON*). O Google passou a mostrar o client secret só nesse momento; se você perder, é preciso criar um novo secret no mesmo cliente (**Clientes → Yata para Windows → Adicionar secret**) e apagar o antigo.

O arquivo baixado (`client_secret_....json`) tem este formato:

```json
{"installed":{"client_id":"1234-abc.apps.googleusercontent.com","client_secret":"GOCSPX-...", ...}}
```

Ele não deve ir para o Git: o `.gitignore` já ignora `client_secret*.json` e `google-client.json`.

> Clientes sem uso por muitos meses podem ser apagados automaticamente pelo Google. Se a agenda parar de conectar com `invalid_client` ou `deleted_client`, crie outro cliente e atualize os secrets.

## 8. Testar localmente

```powershell
Copy-Item "$HOME\Downloads\client_secret_*.json" "$env:LOCALAPPDATA\Yata\google-client.json"
dotnet run --project src/Yata
```

No Yata: **Configurações → Agenda → Conectar com Google** → o navegador abre → escolha a conta → "O Google não verificou este app" → **Continuar** → **marque as caixas de agenda** → **Continuar** → a aba mostra "Agenda conectada". As configurações passam a mostrar "Conectado como …" e a aba Agenda mostra os eventos.

## 9. Colocar as credenciais nos releases

**Releases oficiais (GitHub Actions):** em *Settings → Secrets and variables → Actions*, crie:

- `YATA_GOOGLE_CLIENT_ID`
- `YATA_GOOGLE_CLIENT_SECRET`

O workflow `release.yml` repassa os dois como variáveis de ambiente, e o `Yata.csproj` grava os valores como `AssemblyMetadata`.

**Compilando localmente:** use uma das opções:

```powershell
# a) variáveis de ambiente na hora de compilar
$env:YATA_GOOGLE_CLIENT_ID = "....apps.googleusercontent.com"
$env:YATA_GOOGLE_CLIENT_SECRET = "GOCSPX-..."
dotnet run --project src/Yata

# b) ou copie o JSON baixado do Google Cloud, sem recompilar:
Copy-Item client_secret_*.json "$env:LOCALAPPDATA\Yata\google-client.json"
```

O arquivo `google-client.json` tem prioridade sobre o que foi compilado. Sem nenhum dos dois, o Yata funciona normalmente e a agenda aparece como indisponível.

> Por que o *client secret* vai dentro do executável? Num app instalado ele não é confidencial: o Google trata assim e é por isso que o fluxo usa PKCE. O secret fica fora do Git para que forks cadastrem o próprio cliente em vez de usar a cota e a marca do projeto.

## 10. Site e domínio (exigidos na verificação)

O Google exige página inicial e política de privacidade **num domínio verificado por você** no [Search Console](https://search.google.com/search-console). Duas opções simples:

- **GitHub Pages** (`akaneni.github.io/yata`): verifique `akaneni.github.io` no Search Console com a meta tag ou o arquivo HTML e publique a página inicial e o `PRIVACY.md`.
- Um domínio próprio, verificado por DNS.

A página inicial precisa explicar o que o app faz e por que ele pede acesso à agenda, e precisa ter um link para a política.

## 11. Teste → Produção → Verificação

| Fase | Como fica para quem usa |
|---|---|
| **Testes** | Só e-mails cadastrados em *Público-alvo → Usuários de teste* (até 100). **O refresh token expira em 7 dias**: o Yata avisa "O acesso à agenda expirou" e pede para reconectar. |
| **Produção, sem verificação** | Qualquer pessoa, mas com a tela "O Google não verificou este app" e limite de 100 usuários. |
| **Produção, verificado** | Tela normal, sem limite. |

Os escopos de agenda são **sensíveis** (não restritos): a verificação **não** exige auditoria de segurança (CASA). Ela exige:

1. Branding completo, com domínios verificados (seção 10).
2. A política de privacidade descrevendo o uso dos dados do Google, incluindo a frase de *Limited Use* (já está no `PRIVACY.md`).
3. Um **vídeo de demonstração** (YouTube, pode ser não listado) mostrando: o Yata aberto → Configurações → "Conectar com Google" → a tela de consentimento com o **ID do cliente visível na barra de endereço** → os eventos aparecendo no notch e na aba Agenda → "Desconectar".
4. A justificativa de cada escopo (texto da seção 5).

Clique em **Publicar app** e depois em **Preparar para verificação**. A análise costuma levar de alguns dias a algumas semanas, e o Google responde por e-mail pedindo ajustes.

## Problemas comuns

| Mensagem no Yata | Causa |
|---|---|
| "A API do Google Agenda não está ativada no projeto" | Falta o passo 1.2 |
| "Marque a permissão de ver a agenda" | Na tela do Google, a pessoa desmarcou um dos escopos de agenda |
| "O acesso à agenda expirou" | App em modo Testes (7 dias), acesso revogado em myaccount.google.com ou senha trocada |
| `Erro 403: access_denied` no navegador | App em Testes e o e-mail não está na lista de usuários de teste |
| "compilada sem credenciais do Google" | Build sem as variáveis e sem `google-client.json` |
