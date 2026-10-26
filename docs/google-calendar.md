# Google Agenda e Google Tasks: cadastro do app no Google

O Yata usa o fluxo OAuth recomendado pelo Google para apps instalados: navegador do sistema, redirecionamento para `http://127.0.0.1:<porta>`, PKCE (S256) e verificação de `state`. Não existe servidor do Yata no meio: o PC fala direto com o Google.

Agenda e tarefas usam a mesma conta, mas cada uma pede a própria permissão quando é ligada (autorização incremental, `include_granted_scopes=true`). Quem só quer a agenda nunca vê o pedido de tarefas, e vice-versa.

| O que | Como |
|---|---|
| Escopos | `openid`, `email`; agenda: `calendar.calendarlist.readonly`, `calendar.events.readonly` (só leitura); tarefas: `tasks` (leitura e escrita) |
| Token guardado | só o *refresh token*, o e-mail e a lista de escopos concedidos, cifrados com DPAPI do usuário do Windows em `%LOCALAPPDATA%\Yata\google.dat` |
| Token de acesso | só na memória, renovado sozinho |
| Eventos | hoje e amanhã, só na memória, atualizados a cada 5 min |
| Tarefas | uma lista escolhida, sincronizada nos dois sentidos a cada 2 min (e logo depois de cada mudança no Yata) |
| Desconectar | revoga o acesso no Google (`oauth2.googleapis.com/revoke`) e apaga o arquivo |

As credenciais do cliente OAuth **não ficam no repositório**. Os releases oficiais recebem as credenciais na compilação (secrets do GitHub). Quem compila o código-fonte cadastra o próprio cliente.

---

> O console muda de tempos em tempos. Os nomes abaixo são os do console em português; o equivalente em inglês aparece entre parênteses. Na dúvida, use a busca no topo do console.

## 1. Projeto

1. Entre em <https://console.cloud.google.com> com a conta que vai ser a **dona** do app. Prefira uma conta que você não vá perder; dá para adicionar outras pessoas como proprietárias depois.
2. Na primeira vez, aceite os termos. **Não precisa ativar faturamento**: as APIs de Agenda e Tasks são gratuitas dentro da cota.
3. No seletor de projeto (topo, à esquerda do campo de busca) → **Novo projeto** (*New project*).
   - Nome: `Yata`. O ID (ex.: `yata-123456`) é gerado e não muda depois.
   - Organização: "Sem organização" numa conta Gmail pessoal está ok.
   - **Criar** e, quando terminar, selecione o projeto no seletor. Todos os passos seguintes valem para o projeto selecionado.

## 2. Ativar as APIs

1. Menu ☰ → **APIs e serviços** → **Biblioteca** (*Library*).
2. Busque "Google Calendar API" → abra → **Ativar** (*Enable*).
3. Volte à Biblioteca, busque "Google Tasks API" → abra → **Ativar**.

Sem isso, o Yata conecta mas mostra "A API do Google Agenda não está ativada no projeto" (ou "do Google Tasks").

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
| `https://www.googleapis.com/auth/tasks` | sensível |

→ **Atualizar** → **Salvar** no fim da página.

Os três aparecem em "Seus escopos sensíveis", e é por eles que o app precisa de verificação para sair da tela de aviso. Cada escopo só aparece na lista se a API correspondente estiver ativada (seção 2).

Guarde para a verificação a justificativa de cada um:

- **Agenda:** *"Mostrar no notch do Windows os eventos de hoje e amanhã das agendas que o usuário escolher e avisar antes de cada um. Somente leitura; os dados ficam no computador do usuário e não são enviados a nenhum servidor."*
- **Tasks:** *"Sincronizar a lista de tarefas do notch com uma lista do Google Tasks escolhida pelo usuário: mostrar as tarefas e, quando o usuário cria, conclui ou remove uma tarefa no Yata, fazer o mesmo no Google Tasks. É preciso escrita porque a sincronização é nos dois sentidos. Os dados ficam no computador do usuário e não são enviados a nenhum servidor."*

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

No Yata: **Configurações → Google → Conectar com Google** → o navegador abre → escolha a conta → "O Google não verificou este app" → **Continuar** → **marque as caixas de agenda e de tarefas** → **Continuar** → a aba mostra "Conta Google conectada". As configurações passam a mostrar "Conectado como …", a aba Agenda mostra os eventos e a aba Tarefas passa a mostrar a lista "Minhas tarefas".

Para testar a permissão incremental, conecte só com a agenda marcada e depois clique em **Tarefas → Permitir**: o Google pede só a permissão nova e a agenda continua funcionando.

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

A página inicial precisa explicar o que o app faz e por que ele pede acesso à agenda e às tarefas, e precisa ter um link para a política.

## 11. Teste → Produção → Verificação

| Fase | Como fica para quem usa |
|---|---|
| **Testes** | Só e-mails cadastrados em *Público-alvo → Usuários de teste* (até 100). **O refresh token expira em 7 dias**: o Yata avisa "O acesso à conta Google expirou" e pede para reconectar. |
| **Produção, sem verificação** | Qualquer pessoa, mas com a tela "O Google não verificou este app" e limite de 100 usuários. |
| **Produção, verificado** | Tela normal, sem limite. |

Os escopos de agenda e de tarefas são **sensíveis** (não restritos): a verificação **não** exige auditoria de segurança (CASA). Ela exige:

1. Branding completo, com domínios verificados (seção 10).
2. A política de privacidade descrevendo o uso dos dados do Google, incluindo a frase de *Limited Use* (já está no `PRIVACY.md`).
3. Um **vídeo de demonstração** (YouTube, pode ser não listado) mostrando: o Yata aberto → Configurações → "Conectar com Google" → a tela de consentimento com o **ID do cliente visível na barra de endereço** → os eventos aparecendo no notch e na aba Agenda → uma tarefa criada e concluída no Yata aparecendo no Google Tasks (e o contrário) → "Desconectar".
4. A justificativa de cada escopo (texto da seção 5).

Clique em **Publicar app** e depois em **Preparar para verificação**. A análise costuma levar de alguns dias a algumas semanas, e o Google responde por e-mail pedindo ajustes.

## Problemas comuns

| Mensagem no Yata | Causa |
|---|---|
| "A API do Google Agenda (ou Tasks) não está ativada no projeto" | Falta a seção 2 |
| "Marque a permissão de ver a agenda" / "de tarefas" | Na tela do Google, a pessoa desmarcou esse escopo |
| "Nenhuma permissão foi marcada no Google" | A pessoa desmarcou tudo; o acesso é revogado na hora |
| "O acesso à conta Google expirou" | App em modo Testes (7 dias), acesso revogado em myaccount.google.com ou senha trocada |
| `Erro 403: access_denied` no navegador | App em Testes e o e-mail não está na lista de usuários de teste |
| "compilada sem credenciais do Google" | Build sem as variáveis e sem `google-client.json` |
