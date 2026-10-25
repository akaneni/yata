# Yata

Um notch vivo no topo da tela do Windows, logo abaixo da câmera: mostra o que está tocando, a hora, seus lembretes, e sai do caminho quando não é necessário.

O nome vem de **Yatagarasu** (八咫烏), o corvo mensageiro e guia da mitologia japonesa — parente dos corvos Huginn e Muninn, que voam pelo mundo e voltam contando o que viram. É isso que o Yata faz: fica lá em cima e te traz as notícias.

Projeto de código aberto, sem conta e sem telemetria.

**Dados locais:** tarefas, registro de água e caminhos da prateleira ficam em `%LOCALAPPDATA%\Yata\data.json`.

**Acesso à rede:** o único acesso é a busca de letras no [LRCLIB](https://lrclib.net) (serviço aberto e gratuito), enviando apenas artista, título e duração da música. As letras ficam em cache em `%LOCALAPPDATA%\Yata\lyrics`.

## Identidade

- **Minimalista e monocromático**: notch preto, texto branco; a cor da capa da música é o único acento.
- **O Yata**: um corvo em pixel art 1-bit (16×14), branco sobre o preto do notch. Desenhado à mão em `Views/RavenSprite.cs`; o ícone do app é gerado a partir do mesmo sprite (`tools/make-icon.ps1`).

## Funcionalidades (v0.1)

- **Notch compacto**: relógio quando nada toca; capa, título e equalizador animado quando há mídia.
- **Expande ao passar o mouse** com animação de mola; recolhe ao sair.
- **Player de mídia** de qualquer app que use os controles de mídia do Windows (Spotify, YouTube no navegador, etc.): capa, título, artista, barra de progresso com busca, anterior/play/próxima.
- **Letras sincronizadas**: o notch se divide em dois (player + letra rolando), a linha atual fica em destaque e clicar numa linha pula para ela. No modo compacto aparece a linha sendo cantada.
- **Avisos do sistema** no notch compacto: volume/mudo, brilho, carregador conectado/desconectado, bateria baixa (20% e 10%), troca de saída de áudio (fone/caixa), Caps Lock, troca de idioma do teclado e conexão de rede. O teclado é lido por consulta de estado, **sem hook** — o app nunca vê o que é digitado.
- **Abas no notch aberto**: Mídia, Foco, Água, Tarefas e Prateleira.
- **Foco (Pomodoro)**: 25 min de foco / 5 min de pausa, pausa longa de 15 min a cada 4 sessões, aviso sonoro ao fim de cada fase e contagem no notch compacto.
- **Água**: meta diária, botão +250 ml, gráfico dos últimos 7 dias e lembrete de hora em hora (8h–22h) enquanto a meta não é batida.
- **Tarefas**: lista com marcar/remover; um horário no fim do texto ("Reunião 15:30") vira lembrete. O notch só aceita foco de teclado enquanto você digita e devolve o foco à janela anterior ao terminar.
- **Prateleira de arquivos**: arraste arquivos ou pastas até o notch para guardar e arraste de volta para qualquer app; clique duplo abre. Guarda só o caminho (nada é copiado) e itens apagados/movidos somem sozinhos. Imagens ganham miniatura.
- **O Yata (companheiro)**, a partir do tempo desde o último uso de mouse/teclado (`GetLastInputInfo` — só o horário, nunca o conteúdo):
  - **Modo ambiente** (2 min parado, nada tocando): o relógio dá lugar ao Yata pousado, que pisca, olha em volta e se ajeita; aos 10 min ele cochila.
  - **Com música tocando** e você longe: ele toma o lugar do equalizador e balança a cabeça.
  - **"Enquanto você esteve fora"**: ao voltar, o Yata chega voando com os avisos que você perdeu (lembretes, água, bateria, foco pausado).
  - **Pausa automática do foco** após 5 min longe (devolvendo o tempo não acompanhado), exceto se houver mídia tocando.
  - **Demonstração** pelo menu da bandeja (ou `Yata.exe --demo`) para ver todos os estados em ~20 s.
- Lembretes que chegam com o notch aberto ou em tela cheia esperam e aparecem depois, em vez de serem perdidos.
- **Some em tela cheia** (vídeos, jogos, apresentações).
- Não rouba o foco, não aparece no Alt+Tab nem na barra de tarefas. Sair pelo ícone na bandeja.

## Requisitos

- Windows 10 (2004+) ou 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) para compilar

## Compilar e rodar

```powershell
dotnet run --project src/Yata
```

## Estrutura

```
src/Yata/
  App.xaml(.cs)               inicialização, instância única, ícone na bandeja
  MainWindow.xaml(.cs)        janela do notch, formato, animação, modos compacto/expandido
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
  Services/AppStore.cs        persistência local (JSON)
  Services/FocusTimer.cs      Pomodoro
  Services/WaterTracker.cs    água e lembretes
  Services/TaskList.cs        tarefas e lembretes
  Services/Shelf.cs           prateleira de arquivos
  Helpers/                    mola das animações, cor da capa, ícones de arquivo, som, caminhos
  Interop/                    chamadas Win32 e COM (Core Audio)
  Assets/yata.ico             ícone (gerado por tools/make-icon.ps1)
```

## Roadmap

1. ~~Base do notch + mídia~~
2. ~~Letras sincronizadas (LRCLIB)~~
3. ~~Avisos do sistema: volume, brilho, bateria, fone, Caps Lock~~
4. ~~Pomodoro, lembrete de água, to-do~~
5. ~~Prateleira de arquivos (arrastar e soltar)~~
6. ~~Identidade (Yata) e companheiro~~
7. Configurações, iniciar com o Windows, instalador
