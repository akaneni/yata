# Notch

Um notch vivo no topo da tela do Windows, logo abaixo da câmera: mostra o que está tocando, a hora, e sai do caminho quando não é necessário.

Projeto de código aberto, sem conta e sem telemetria.

**Dados locais:** tarefas, registro de água e caminhos da prateleira ficam em `%LOCALAPPDATA%\Notch\data.json`.

**Acesso à rede:** o único acesso é a busca de letras no [LRCLIB](https://lrclib.net) (serviço aberto e gratuito), enviando apenas artista, título e duração da música. As letras ficam em cache em `%LOCALAPPDATA%\Notch\lyrics`.

## Funcionalidades (v0.1)

- **Notch compacto**: relógio quando nada toca; capa, título e equalizador animado quando há mídia.
- **Expande ao passar o mouse** com animação de mola; recolhe ao sair.
- **Player de mídia** de qualquer app que use os controles de mídia do Windows (Spotify, YouTube no navegador, etc.): capa, título, artista, barra de progresso com busca, anterior/play/próxima.
- **Letras sincronizadas**: o notch se divide em dois (player + letra rolando), a linha atual fica em destaque e clicar numa linha pula para ela. No modo compacto aparece a linha sendo cantada.
- **Avisos do sistema** no notch compacto: volume/mudo, brilho, carregador conectado/desconectado, bateria baixa (20% e 10%), troca de saída de áudio (fone/caixa), Caps Lock, troca de idioma do teclado e conexão de rede. O teclado é lido por consulta de estado, **sem hook** — o app nunca vê o que é digitado.
- **Abas no notch aberto**: Mídia, Foco, Água e Tarefas.
- **Foco (Pomodoro)**: 25 min de foco / 5 min de pausa, pausa longa de 15 min a cada 4 sessões, aviso sonoro ao fim de cada fase e contagem no notch compacto.
- **Água**: meta diária, botão +250 ml, gráfico dos últimos 7 dias e lembrete de hora em hora (8h–22h) enquanto a meta não é batida.
- **Tarefas**: lista com marcar/remover; um horário no fim do texto ("Reunião 15:30") vira lembrete. O notch só aceita foco de teclado enquanto você digita e devolve o foco à janela anterior ao terminar.
- **Prateleira de arquivos**: arraste arquivos ou pastas até o notch para guardar e arraste de volta para qualquer app; clique duplo abre. Guarda só o caminho (nada é copiado) e itens apagados/movidos somem sozinhos. Imagens ganham miniatura.
- Lembretes que chegam com o notch aberto ou em tela cheia esperam e aparecem depois, em vez de serem perdidos.
- **Cor de destaque** extraída automaticamente da capa.
- **Some em tela cheia** (vídeos, jogos, apresentações).
- Não rouba o foco, não aparece no Alt+Tab nem na barra de tarefas. Sair pelo ícone na bandeja.

## Requisitos

- Windows 10 (2004+) ou 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) para compilar

## Compilar e rodar

```powershell
dotnet run --project src/Notch
```

## Estrutura

```
src/Notch/
  App.xaml(.cs)               inicialização, instância única, ícone na bandeja
  MainWindow.xaml(.cs)        janela do notch, formato, animação, modos compacto/expandido
  Services/MediaService.cs    sessão de mídia do Windows (SMTC)
  Services/LyricsService.cs   busca/cache de letras (LRCLIB) e parser LRC
  Services/AudioWatcher.cs    volume e dispositivo de saída (Core Audio)
  Services/BrightnessWatcher.cs  brilho (WMI)
  Services/PowerWatcher.cs    carregador e bateria
  Services/NetworkWatcher.cs  conexão de rede
  Services/KeyboardWatcher.cs Caps Lock e idioma do teclado (sem hook)
  Interop/CoreAudio.cs        declarações COM do Core Audio
  Services/AppStore.cs        persistência local (JSON)
  Services/FocusTimer.cs      Pomodoro
  Services/WaterTracker.cs    água e lembretes
  Services/TaskList.cs        tarefas e lembretes
  Services/Shelf.cs           prateleira de arquivos
  Views/                      páginas Foco, Água, Tarefas e Prateleira
  Helpers/FileIcons.cs        miniaturas e ícones de arquivo
  Helpers/Chime.cs            som de aviso gerado em memória
  Services/FullscreenWatcher.cs  detecta app em tela cheia e mantém o notch no topo
  Helpers/Spring.cs           física de mola das animações
  Helpers/ColorExtractor.cs   cor de destaque a partir da capa
  Interop/NativeMethods.cs    chamadas Win32
```

## Roadmap

1. ~~Base do notch + mídia~~
2. ~~Letras sincronizadas (LRCLIB)~~
3. ~~Avisos do sistema: volume, brilho, bateria, fone, Caps Lock~~
4. ~~Pomodoro, lembrete de água, to-do~~
5. ~~Prateleira de arquivos (arrastar e soltar)~~
6. Companheiro ocioso (olhinhos)
7. Configurações, iniciar com o Windows, instalador
