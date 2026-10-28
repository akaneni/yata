<h1><img src="docs/brand/yata-lockup.svg" alt="Yata" height="56"></h1>

[English](README.md) · **Português**

![O Yata, corvo em pixel art, pousado diante de um sol pontilhado](docs/yata-hero.png)

Um notch vivo no topo da tela do Windows, logo abaixo da câmera: mostra o que está tocando, a hora, seus lembretes, e sai do caminho quando não é necessário. Site: **[getyata.app](https://getyata.app)**.

O nome vem de **Yatagarasu** (八咫烏), o corvo mensageiro e guia da mitologia japonesa — parente dos corvos Huginn e Muninn, que voam pelo mundo e voltam contando o que viram. É isso que o Yata faz: fica lá em cima e te traz as notícias.

Projeto de código aberto, sem conta e sem telemetria.

**Dados locais:** tarefas, registro de água e caminhos da prateleira ficam em `%LOCALAPPDATA%\Yata\data.json`. Um registro de diagnóstico (erros e o que cada parte do app fez, sem músicas, tarefas, eventos, caminhos ou contas) fica em `%LOCALAPPDATA%\Yata\logs` por 7 dias, para você anexar numa issue se quiser.

**Acesso à rede:** só o que você liga, e nada sobre o PC é enviado:
- busca de letras no [LRCLIB](https://lrclib.net) (serviço aberto e gratuito), enviando apenas artista, título e duração da música. As letras ficam em cache em `%LOCALAPPDATA%\Yata\lyrics`. Pode ser desligada nas configurações.
- aviso de versão nova: **desligado por padrão**. Se ligado, consulta uma vez por dia a [página pública de versões](https://github.com/akaneni/yata/releases) do GitHub. **Procurar agora** (Configurações → Atualizações, ou o menu da bandeja) consulta na hora, ligado ou não. Atualizar leva um clique (**Atualizar e reiniciar** nas Configurações): o instalador vem do GitHub e é conferido com o SHA-256 publicado antes de rodar.
- **tempo** no [Open-Meteo](https://open-meteo.com) (aberto, sem conta nem chave), só se você ligar: envia as coordenadas da cidade escolhida, arredondadas para ~1 km. A busca da cidade envia só o nome digitado.
- **Google Agenda e Google Tasks**, só se você conectar sua conta e liberar cada um: leitura das agendas e dos eventos de hoje e amanhã, e sincronização de uma lista de tarefas, direto entre o PC e o Google. O acesso fica cifrado com DPAPI em `%LOCALAPPDATA%\Yata\google.dat` e os eventos ficam só na memória. Detalhes em [PRIVACY.pt-BR.md](PRIVACY.pt-BR.md).

## Identidade

- **Minimalista e monocromático**: notch preto, texto branco; a cor da capa da música é o único acento.
- **O Yata**: um corvo em pixel art 1-bit (16×14), branco sobre o preto do notch. Desenhado à mão em `Views/RavenSprite.cs`; o ícone do app é gerado a partir do mesmo sprite (`tools/make-icon.ps1`).
- **Repertório**: pisca, olha para trás, se ajeita e, de vez em quando, olha para cima, estica a asa ou dá um pulinho; dorme respirando devagar; grasna quando traz um lembrete, se sacode quando a meta de água é batida e chega com uma carta quando traz as novidades.
- **Logotipo**: "yata" em letras de pixel na mesma grade do corvo, traço de 2 px; o rabo do "y" desce no mesmo ângulo das costas do Yata, que pousa na linha de base do texto. Versões em branco, preto e uma que segue o tema claro/escuro em `docs/brand/`, geradas por `tools/make-brand.ps1` a partir do próprio sprite (junto com o favicon e a prévia de link do site).
- **Tipografia do site**: DotGothic16 nos títulos (pixel, com o 八咫烏), Geist no texto e Geist Mono nos detalhes.
- **Artes maiores** (README e instalador): o mesmo traço no dobro da resolução e com três pernas, como o Yatagarasu do mito, diante de um sol pontilhado em 1-bit. Geradas com o PhotoCraft da suíte ArtCraft (`tools/make-art.ps1`); o GIF abaixo sai do próprio sprite e é codificado no EffectCraft (`tools/make-gif.ps1`).

![O repertório do Yata no notch](docs/yata.gif)

## Funcionalidades (v0.1)

- **Português e inglês**: segue o idioma do Windows (português em qualquer Windows em português, inglês nos demais), ou escolha em Configurações → Idioma.
- **Notch compacto**: relógio quando nada toca; capa, título e equalizador animado quando há mídia.
- **Expande ao passar o mouse** com animação de mola; recolhe ao sair.
- **Player de mídia** de qualquer app que use os controles de mídia do Windows (Spotify, YouTube no navegador, etc.): capa, título, artista, barra de progresso com busca, anterior/play/próxima. Com vários players abertos, segue o que está **tocando de fato**.
- **Letras sincronizadas**: o notch se divide em dois (player + letra rolando), a linha atual fica em destaque e clicar numa linha pula para ela. No modo compacto aparece a linha sendo cantada.
- **Avisos do sistema** no notch compacto: volume/mudo, brilho, carregador conectado/desconectado, bateria baixa (20% e 10%), troca de saída de áudio (fone/caixa), Caps Lock, troca de idioma do teclado e conexão de rede. O teclado é lido por consulta de estado, **sem hook** — o app nunca vê o que é digitado.
- **Câmera e microfone em uso**: ponto laranja (microfone) ou verde (câmera) no notch fechado e o nome dos apps no notch aberto. O microfone vem das sessões de áudio do Windows (Core Audio). A câmera depende do registro que o Windows mantinha até a versão 25H2; no Windows 11 26H2 esse dado passou a exigir administrador, então lá só o microfone é detectado.
- **Atalho Ctrl+Alt+Y** abre e fecha o notch de qualquer lugar. O **Ctrl+Alt+J** (desligado por padrão) vai para a janela da atividade que precisa de você e, apertando de novo, para a próxima. Os dois usam `RegisterHotKey`: o Windows avisa só essas combinações.
- **Vários monitores**: escolha nas configurações em qual tela o notch fica; respeita a escala de cada tela.
- **Abas no notch aberto**: Mídia, Agenda, Foco, Água, Tarefas e Prateleira.
- **Agenda (Google)**: conecte com sua conta Google (OAuth, só leitura) e escolha quais agendas aparecem. A aba mostra hoje e amanhã, destaca o evento em andamento e tem o botão **Entrar** para chamadas do Meet, Zoom e Teams. O notch fechado mostra "Daily em 8 min" nos 15 minutos antes, e chega um aviso (com som) alguns minutos antes, configurável, e outro quando o evento começa. Convites recusados não aparecem. Para cadastrar o app no Google, veja [docs/google-calendar.md](docs/google-calendar.md).
- **Tempo** (desligado por padrão): você escolhe a cidade pelo nome ou, se preferir, com uma leitura única da localização do Windows (arredondada para ~1 km na hora, nunca guardada com precisão). Nada de geolocalização por IP. Ícones em pixel art 1-bit no estilo do Yata. O notch fechado mostra ícone e temperatura ao lado do relógio; aberto sem música, mostra condição, máxima/mínima e a chuva prevista. Chega um aviso quando a chance de chuva passa de 60% nas próximas 2 horas. Dados do tempo: [Open-Meteo.com](https://open-meteo.com) (CC BY 4.0).
- **Foco (Pomodoro)**: 25 min de foco / 5 min de pausa, pausa longa de 15 min a cada 4 sessões, aviso sonoro ao fim de cada fase e contagem no notch compacto. Controles explícitos por estado: **Iniciar**, **Pausar**, **Continuar**, **Parar** e **Pular pausa**; pausado, o anel apaga e o tempo pisca suave (e o notch compacto mostra ⏸).
- **Água**: meta diária, botão +250 ml, gráfico dos últimos 7 dias e lembrete de hora em hora (8h–22h) enquanto a meta não é batida.
- **Tarefas**: lista com marcar/remover (remover pode ser desfeito por alguns segundos); um horário no fim do texto ("Reunião 15:30", "ligar às 9h00", "Call Ana at 3pm") vira lembrete. Opcionalmente sincroniza, nos dois sentidos, com uma lista do **Google Tasks** (o Google guarda só o dia; o horário do lembrete fica no Yata). O notch só aceita foco de teclado enquanto você digita e devolve o foco à janela anterior ao terminar.
- **Prateleira de arquivos**: arraste arquivos ou pastas até o notch para guardar e arraste de volta para qualquer app; clique duplo abre. Guarda só o caminho (nada é copiado) e itens apagados/movidos somem sozinhos. Imagens ganham miniatura. "Limpar" também pode ser desfeito por alguns segundos.
- **O Yata (companheiro)**, a partir do tempo desde o último uso de mouse/teclado (`GetLastInputInfo` — só o horário, nunca o conteúdo):
  - **Modo ambiente** (2 min parado, nada tocando): o relógio dá lugar ao Yata pousado, que pisca, olha em volta e se ajeita; aos 10 min ele cochila.
  - **Com música tocando** e você longe: ele toma o lugar do equalizador e balança a cabeça.
  - **"Enquanto você esteve fora"**: ao voltar, o Yata chega voando com uma carta e os avisos que você perdeu (lembretes, água, bateria, foco pausado).
  - **Mensageiro**: lembretes (tarefas, água, foco, versão nova) chegam com o Yata grasnando no lugar do ícone; pequenas vitórias ganham comemoração (voo ao fim do foco, sacudida na meta de água).
  - **Pausa automática do foco** após 5 min longe (devolvendo o tempo não acompanhado), exceto se houver mídia tocando.
  - **Demonstração** pelo menu da bandeja (ou `Yata.exe --demo`) para ver todos os estados em ~30 s.
- **Configurações** (engrenagem no notch, menu ou clique duplo na bandeja): iniciar com o Windows, esconder em tela cheia, demora para abrir, cor da capa ou preto e branco, letras liga/desliga, cada tipo de aviso, tempos do Pomodoro, meta e lembretes de água, e o comportamento do Yata. Tudo aplica na hora.
- **Agora, não na próxima rodada**: **Procurar agora** busca versão nova (Configurações → Atualizações, ou o menu da bandeja, mesmo com a checagem diária desligada); o **↻** nas abas Agenda e Tarefas e o **Sincronizar agora** nas Configurações leem o Google na hora; **Atualizar agora** busca a previsão do tempo. Cada um mostra quando rodou pela última vez, e um segundo clique em menos de 15 segundos não vai à rede de novo.
- Lembretes que chegam com o notch aberto ou em tela cheia esperam e aparecem depois, um de cada vez, em vez de serem perdidos. Um aviso rápido (volume, Caps Lock) aparece na hora, e o lembrete que ele interrompeu volta logo em seguida.
- **Bolha ao lado do notch**: como a ilha do iPhone se dividindo em duas, um segundo notch pequeno leva o que não cabe no primeiro: enquanto a música ocupa o notch, a contagem do foco (um anel esvaziando em volta dos minutos que faltam) e a próxima reunião vão para lá, assim como as atividades de outros programas. Uma de cada vez, por prioridade (o que precisa de você, a reunião, o foco, as atividades); passe o mouse para ver o que é e quantas outras estão esperando. Clicar numa atividade leva você à janela de onde ela veio (o terminal em que o agente pede uma permissão, por exemplo), e você responde lá. Com o notch aberto, a bolha vira um segundo notch ao lado dele com todas as atividades, cada uma com um botão para ir à janela e outro para dispensar.
- **Atividades de outros programas** (scripts, builds, agentes de IA): podem mostrar o andamento na bolha com o `yata-cli`. Veja [Atividades](#atividades) abaixo. Pode ser desligado nas Configurações.
- **Some em tela cheia** (vídeos, jogos, apresentações).
- Não rouba o foco, não aparece no Alt+Tab nem na barra de tarefas. Sair pelo ícone na bandeja.

## Instalar

Baixe `Yata-Setup-x.y.z.exe` em [Releases](https://github.com/akaneni/yata/releases) (~3 MB). O instalador:

- instala só para o seu usuário, sem pedir administrador;
- se faltar o **.NET 10 Desktop Runtime**, baixa direto da Microsoft e instala (essa etapa pede permissão de administrador);
- oferece iniciar com o Windows e atalho na área de trabalho;
- na desinstalação, remove o app e a inicialização automática, mas **mantém seus dados** em `%LOCALAPPDATA%\Yata`.

O instalador ainda não é assinado digitalmente, então o Windows SmartScreen pode avisar na primeira execução: clique em **Mais informações** e depois em **Executar assim mesmo**. Para conferir o arquivo antes:

- **Hash:** no PowerShell, `Get-FileHash .\Yata-Setup-x.y.z.exe` tem que bater com a linha do `SHA256SUMS.txt` da release.
- **Origem:** todo instalador é gerado pelo GitHub Actions a partir deste repositório e traz uma comprovação de origem (build provenance attestation). Com o [GitHub CLI](https://cli.github.com), `gh attestation verify Yata-Setup-x.y.z.exe --repo akaneni/yata` confirma qual execução do workflow e qual commit o geraram.

A SignPath Foundation, que assina de graça projetos de código aberto, recusou o Yata em outubro de 2026 por falta de visibilidade pública; o Yata vai pedir de novo conforme crescer. Veja a [política de assinatura de código](https://getyata.app/code-signing) e [docs/code-signing.md](docs/code-signing.md).

## Requisitos

- Windows 10 (2004+) ou 11, x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) para compilar

## Compilar e rodar

```powershell
dotnet run --project src/Yata
```

A agenda e as tarefas do Google precisam de um cliente OAuth próprio, que não fica no repositório. Sem ele, o resto do app funciona normalmente. Veja [docs/google-calendar.md](docs/google-calendar.md).

Testes automáticos (Pomodoro, letras, tarefas, água, presença, escolha do player, aviso de versão, agenda e traduções, com relógio simulado e sem rede). Rodam também no GitHub Actions a cada push:

```powershell
dotnet test
```

### Atividades

Qualquer script ou programa rodando como você neste PC pode pôr uma atividade ao lado do notch com o `yata-cli.exe`, instalado ao lado do `Yata.exe` (`%LOCALAPPDATA%\Programs\Yata`). O instalador pode pôr essa pasta no seu PATH (uma opção, desligada por padrão), para o `yata-cli` funcionar em qualquer terminal:

```powershell
yata-cli activity set build --title "Build" --text "compilando" --progress 40% --icon code
yata-cli activity set build --title "Build" --progress 0.8
yata-cli activity done build --title "Build" --text "ok"     # um ✓, e depois some
yata-cli activity attention agent --title "Claude Code" --text "esperando você" --icon agent
yata-cli activity end agent
```

É um programa de console: PowerShell, cmd, bash e agentes de IA esperam por ele e recebem o código de saída e as mensagens de erro. O próprio `Yata.exe` não recebe comandos. `yata-cli --help` lista tudo.

- Ações: `set` (em andamento), `attention` (precisa de você: cor de alerta e um aviso que espera ser visto), `done`, `fail` (falhou: cor de alerta), `end`.
- **Mande o `--title` sempre.** Uma atividade em andamento que ninguém atualiza some depois de 10 minutos (`--ttl` muda isso, até 24 h), e um id que sumiu precisa do título para voltar. `attention` e `fail` ficam até um `end` (no máximo 1 h); `done` fica 12 s. `end` num id que já sumiu não dá erro.
- Ícones: `activity`, `code`, `build`, `download`, `upload`, `sync`, `agent`, `bell`, `timer`. Um conjunto fixo: programas não mandam imagens nem glifos quaisquer.
- O texto é uma linha simples (títulos até 60 caracteres, texto até 120); caracteres de controle, invisíveis e de mudança de direção são removidos.
- No máximo 8 atividades ao mesmo tempo, e 30 pedidos a cada 10 s. Nada é impresso quando dá certo.
- Códigos de saída: 0 mostrou, 1 argumentos errados (opção, ícone ou valor desconhecido), 2 o Yata não está aberto, 3 recusado pelo Yata (o motivo é impresso).
- Como funciona: um named pipe que só o seu usuário do Windows abre e que recusa acesso pela rede. As atividades ficam só na memória; o texto delas não vai para o log.

### Agentes de IA: Claude Code, Cursor e Codex

O Yata pode mostrar o que o seu agente de programação está fazendo — **trabalhando**, **esperando você** (precisa de uma permissão ou da sua resposta) e **concluído** — uma bolha por sessão, com o nome do agente e da pasta do projeto ("Claude Code · yata"). Os hooks do agente chamam `yata-cli hook <agente>`; só o estado vai para o notch, nunca os seus prompts, os comandos ou as respostas do agente. Se o Yata não estiver aberto, o hook volta na hora, e ele nunca bloqueia nem muda o agente (sempre sai com 0 e só imprime a resposta neutra que alguns eventos esperam).

**O jeito fácil: Configurações → Agentes de IA.** Um interruptor para cada agente encontrado no PC (Claude Code, Cursor, Codex). O Yata põe os hooks no arquivo de configuração do próprio agente, junto do que já existe, guarda uma cópia do arquivo antes de cada mudança (`.yata-backup`), e desligar tira só o que ele pôs. As sessões novas já pegam. Também ali:
- **Claude Code no WSL → Escolher…** lista as distros do WSL que têm o Claude Code; marque as que quiser. Cada uma recebe os mesmos hooks, chamando o `yata-cli.exe` pelo `/mnt/c`, e um link `yata-cli` em `~/.local/bin` para a skill. Uma distro sem acesso ao Windows (sem acesso ao `/mnt/c`) aparece na lista, mas não dá para ligar.
- **yata-cli no PATH**, igual à opção do instalador (que a atualização silenciosa não mostra), para scripts e para o Codex.
- Desinstalar o Yata tira os hooks dele do Claude Code, do Cursor e do Codex no Windows, para nenhum ficar apontando para um `yata-cli.exe` apagado. As distros do WSL ficam com os delas: desligue nas Configurações antes.

**À mão**, com o `yata-cli` no PATH:

- **Claude Code**: instale o plugin, que traz os hooks e a skill abaixo:
  ```
  /plugin marketplace add akaneni/yata
  /plugin install yata@yata
  ```
  Ou configure os hooks você mesmo: `yata-cli hook setup claude` imprime o trecho para o `%USERPROFILE%\.claude\settings.json`. Com o plugin ligado, as Configurações mostram o Claude Code como ligado por ele e não mexem.
- **WSL à mão**: na distro, `ln -s /mnt/c/Users/<você>/AppData/Local/Programs/Yata/yata-cli.exe ~/.local/bin/yata-cli`, depois instale o plugin no Claude Code daquela distro.
- **Cursor**: `yata-cli hook setup cursor` imprime o `%USERPROFILE%\.cursor\hooks.json`. O Cursor não tem evento para "esperando sua aprovação", então mostra trabalhando e concluído. O Yata só usa os eventos do Cursor que não bloqueiam nada, nunca os que decidem permissões.
- **Codex**: `yata-cli hook setup codex` imprime o `%USERPROFILE%\.codex\hooks.json` (revise e confie nele com `/hooks`). Codex antigo, sem hooks: `notify = ["yata-cli", "hook", "codex"]` no `config.toml` mostra quando uma rodada termina.

**Skill**: para o agente pôr as tarefas longas dele (builds, testes, deploys) no notch por conta própria, `yata-cli skill install` copia um `SKILL.md` para `%USERPROFILE%\.claude\skills` (Claude Code, Cursor) e `%USERPROFILE%\.agents\skills` (Codex, Cursor). `yata-cli skill` mostra o arquivo. O plugin já inclui a skill para o Claude Code.

Abrir o notch numa aba sem usar o mouse, por N segundos (prévias, capturas e testes de interface):

```powershell
Yata.exe --show foco 30     # foco | agua | tarefas | prateleira | media
```

Medir CPU, despertares, memória e tempo de abertura em estados fixos (nada tocando, música com letra, notch aberto e Pomodoro). Cada rodada usa um perfil descartável, uma música simulada e nenhuma rede, então seus dados e o que você faz no PC durante a medição não entram nos números. Um Yata aberto é fechado e reaberto no fim:

```powershell
./tools/measure.ps1 -Phases             # -States parado,musica -Rounds 3 -Seconds 20 -OutFile medicao.csv
```

Gerar o instalador localmente (precisa do [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```powershell
./tools/build-installer.ps1 -Version 0.1.0   # sai em dist/
```

Releases são gerados pelo GitHub Actions ao enviar uma tag de versão:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

Os textos da interface ficam em `src/Yata/Strings.resx` (inglês, o padrão) e `src/Yata/Strings.pt-BR.resx` (português): `Strings.Chave` no C# e `{x:Static y:Strings.Chave}` no XAML. Um teste falha se um idioma tiver chave faltando ou sobrando, ou `{0}` diferentes. As mensagens do registro continuam em português, porque são para quem está depurando.

O site ([getyata.app](https://getyata.app)) fica em `site/`: a página em inglês é `site/index.html`, e a versão em português sai das traduções em `site/pt.json`. A política de privacidade do site espelha o `PRIVACY.md` (inglês) e o `PRIVACY.pt-BR.md`. Montar e ver localmente (publicado no GitHub Pages a cada push na `main` que mexa no site):

```powershell
./tools/make-site.ps1                        # sai em _site/
python -m http.server 8000 -d _site          # http://localhost:8000
```

## Estrutura

```
src/Yata/
  App.xaml(.cs)               inicialização, instância única, ícone na bandeja
  MainWindow.xaml(.cs)        janela do notch, formato, animação, modos compacto/expandido
  MainWindow.*.cs             partes por assunto: mídia, letra, avisos, Yata, privacidade, atalho...
  Views/RavenSprite.cs        o Yata: quadros em pixel art e renderização nítida
  Views/RavenAnimator.cs      comportamento do Yata (piscar, olhar, dormir, voar)
  Views/                      páginas Foco, Água, Tarefas e Prateleira
  Services/PresenceTracker.cs ausência/retorno e o resumo "enquanto você esteve fora"
  Services/MediaService.cs    sessão de mídia do Windows (SMTC)
  Services/LyricsService.cs   busca/cache de letras (LRCLIB) e parser LRC
  Services/AudioWatcher.cs    volume e dispositivo de saída (Core Audio)
  Services/BrightnessWatcher.cs  brilho (WMI)
  Services/PowerWatcher.cs    carregador e bateria
  Services/NetworkWatcher.cs  conexão de rede
  Services/KeyboardWatcher.cs Caps Lock e idioma do teclado (sem hook)
  Services/FullscreenWatcher.cs  detecta app em tela cheia e mantém o notch no topo
  Services/PrivacyWatcher.cs  câmera e microfone em uso (MicrophoneMonitor.cs: Core Audio)
  Services/UpdateChecker.cs   aviso opcional de versão nova (releases do GitHub)
  Services/UpdateInstaller.cs "Atualizar e reiniciar": download, conferência do SHA-256, instalação silenciosa
  Services/HudQueue.cs        qual aviso aparece, e os lembretes esperando a vez
  Services/Activities.cs      atividades de outros programas: pedido, limpeza, o quadro
  Services/ActivityPipe.cs    o pipe local (só deste usuário); ActivityClient.cs, o lado que envia
src/Yata.Cli/                 yata-cli.exe: o lado de console, para scripts e agentes (compila os arquivos de atividade do Yata)
  Services/AgentHook.cs       (em src/Yata) eventos dos hooks do Claude Code, Cursor e Codex → atividades
integrations/claude-code/     plugin do Claude Code: hooks e a skill yata-notch (listado em .claude-plugin/marketplace.json)
  Services/AppStore.cs        persistência local (JSON)
  Services/FocusTimer.cs      Pomodoro
  Services/WaterTracker.cs    água e lembretes
  Services/TaskList.cs        tarefas e lembretes
  Services/GoogleTasks.cs     sincronização com o Google Tasks (GoogleAccount.cs: conta compartilhada com a agenda)
  Services/Shelf.cs           prateleira de arquivos
  Helpers/                    mola das animações, cor da capa, ícones de arquivo, som, caminhos
  Interop/                    chamadas Win32 e COM (Core Audio)
  Assets/yata.ico             ícone (gerado por tools/make-icon.ps1)
  SettingsWindow.xaml(.cs)    janela de configurações
  Strings.resx                textos da interface em inglês (Strings.pt-BR.resx: português; Helpers/Lang.cs escolhe o idioma)
installer/yata.iss            instalador (Inno Setup) e suas imagens (wizard-*.png)
docs/                         imagens do README, cadastro no Google e assinatura de código
.signpath/                    configurações de artefato do SignPath (cópia do que está no projeto de lá)
tools/                        gerar ícone, artes (PhotoCraft), GIF (EffectCraft), instalador e medição
tests/Yata.Tests/             testes (xUnit)
.github/workflows/ci.yml      build e testes a cada push
.github/workflows/release.yml release automático por tag
```

## Roadmap

1. ~~Base do notch + mídia~~
2. ~~Letras sincronizadas (LRCLIB)~~
3. ~~Avisos do sistema: volume, brilho, bateria, fone, Caps Lock~~
4. ~~Pomodoro, lembrete de água, to-do~~
5. ~~Prateleira de arquivos (arrastar e soltar)~~
6. ~~Identidade (Yata) e companheiro~~
7. ~~Configurações, iniciar com o Windows, instalador~~

8. ~~Câmera/microfone em uso, vários monitores, atalho global, aviso de versão~~
9. ~~Inglês e português~~

10. ~~Fila de avisos, bolha ao lado do notch e atividades de outros programas~~

Próximas ideias: [docs/roadmap.md](docs/roadmap.md) — assinatura digital (em andamento, veja [docs/code-signing.md](docs/code-signing.md)), winget, integração com o Claude Code, bateria Bluetooth, controles de chamada e mais.
