# Roadmap: ideias para as próximas versões

Registro das melhorias levantadas na análise de outubro de 2026 (código do Yata, a Dynamic Island da Apple e o mercado de apps de notch no Mac e no Windows). A fila de avisos, a bolha ao lado do notch e as atividades de outros programas (`yata-cli activity`) já foram feitas. O resto fica aqui, em ordem aproximada de valor.

## Contexto do mercado (out/2026)

- **Mac**: Boring Notch (GPL, ~11 mil estrelas) e Atoll (GPL, ~5 mil) são os open source de referência; NotchNook (US$ 25), Alcove e DynamicLake os pagos. A queixa mais comum é consumo de CPU e bateria. No macOS 26 Tahoe, as Live Activities do iPhone aparecem na barra de menus.
- **Windows**: quase nenhum open source ativo e de qualidade (o DynamicWin está parado); os novos são fechados ou freemium (Notchify, Isle Notch, NotchPixel).
- **Tendências**: agentes de IA no notch viraram uma categoria no Mac em 2026 (status de Claude Code/Codex e aprovação de permissões); notificações de chamadas; "local, sem telemetria" como argumento de venda.

## Próximos passos

### Ir para a aba certa
Clicar numa atividade já leva à janela de onde ela veio (o terminal do agente que pede permissão), e você responde lá: o Yata nunca decide pelo agente. Decidimos não aprovar nem negar pelo notch, porque um clique no notch autorizaria a execução de código, vendo só um pedaço do comando. Falta a precisão de aba: o Windows Terminal (`WT_SESSION`) e o Orca (`ORCA_TAB_ID`) não têm uma forma pública de focar uma aba. No VS Code e no Cursor, com várias janelas do mesmo processo, vale a que foi usada por último; falta testar.

### Prova pública de desempenho
Publicar no site, a cada versão, a CPU e a memória medidas pelo `tools/measure.ps1` (parado, música, aberto, foco). Ataca a queixa nº 1 do mercado e dá prova ao "o notch em que você pode confiar".

### Bateria de dispositivos Bluetooth
Aviso ao conectar fones e mouse, com o nível da bateria, e a lista no notch aberto. O DynamicWin já fazia.

### Controles de chamada
Silenciar e encerrar Teams/Zoom/Meet/Discord ao lado do ponto laranja do microfone. Verificar o que o Windows 11 expõe (o mute de chamada da barra de tarefas, Win+Alt+K) antes de depender de cada app.

### Lista de atividades no notch aberto
Hoje a bolha mostra uma atividade (a mais urgente) e o aviso ao passar o mouse diz "+N" quando há outras. Falta um lugar no notch aberto para ver todas, com o andamento de cada uma.

### Teclado dentro do notch aberto
Tab e setas entre as abas e os controles (hoje só o Ctrl+Alt+Y e o campo de tarefas usam o teclado).

### Encaminhar comandos para o Yata aberto
Uma segunda execução (`Yata.exe --show tasks`, `--settings`, ou abrir o atalho de novo) hoje só encerra. Com o pipe, pode pedir ao Yata aberto para mostrar a aba ou as configurações.

### Vários monitores
Um notch em cada monitor, ou o notch seguindo o monitor do cursor.

### Barra de tarefas no topo (Windows 11 26H1/26H2)
O Windows está voltando a permitir a barra de tarefas no topo da tela. Verificar se colide com o notch e, se sim, descer o notch para baixo da barra.

## Com ressalvas

### Espelhar as notificações do Windows
O `UserNotificationListener` exige identidade de pacote (MSIX ou pacote esparso), o que traz junto a distribuição pela Microsoft Store e a assinatura. É a função mais sensível à privacidade: só opt-in, filtrada por app e sem guardar o conteúdo.

### Progresso de downloads e cópias
Não há API limpa para o navegador nem para o Explorer. O caminho viável é pelas próprias atividades (um script, ou uma extensão de navegador que chame o `yata-cli`).

### Histórico da área de transferência e cotas de IA
Há demanda no Mac (Atoll, Notchpoint), mas o histórico da área de transferência guarda senhas e dados pessoais; só faria sentido em memória, opt-in e com exclusões.

### Microsoft Store e winget
O winget espera o instalador estável e assinado (veja [code-signing.md](code-signing.md)); a Store resolveria a assinatura e a identidade de pacote juntas.

## Decidido não fazer

- **Aprovar ou negar permissões dos agentes pelo notch**: o clique leva ao terminal, e a decisão fica na própria ferramenta, com o comando inteiro à vista.
- **Plugins com código de terceiros** dentro do Yata: contradizem a proposta de confiança. O pipe de atividades cobre a maior parte do valor sem carregar código de fora.
