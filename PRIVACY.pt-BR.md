# Política de privacidade do Yata

[English](PRIVACY.md) · **Português**

*Última atualização: 27 de outubro de 2026*

O Yata é um app de código aberto para Windows que mostra um "notch" no topo da tela. Ele não tem conta, servidor nem telemetria. Esta política explica quais dados o Yata usa e para onde eles vão.

## Dados que ficam no seu computador

Tarefas, registro de água, caminhos da prateleira e configurações ficam em `%LOCALAPPDATA%\Yata\data.json`. Esses dados nunca são enviados para fora do PC, com uma exceção que você escolhe: se ligar a sincronização com o Google Tasks, as tarefas vão para a sua lista no Google (veja abaixo).

O Yata também mantém um registro de diagnóstico em `%LOCALAPPDATA%\Yata\logs`: horário, qual parte do app agiu e os erros que aconteceram. Ele não guarda títulos de músicas, textos de tarefas, títulos de eventos, nomes de arquivos, localização nem conta; caminhos, endereços web e e-mails que apareçam numa mensagem de erro são apagados antes de gravar. Cada arquivo é guardado por 7 dias e tem no máximo 1 MB por dia. Se o Yata fechar por um erro, ele grava um `crash-*.log` ali e encerra sem acionar o Relatório de Erros do Windows. Nada disso é enviado: você decide se quer anexar a uma issue (Configurações → Abrir registro ou Copiar diagnóstico).

## Serviços externos

O Yata só acessa a internet nestes casos:

- **LRCLIB** (lrclib.net), para buscar letras de músicas: envia artista, título e duração da faixa que está tocando. Pode ser desligado nas Configurações.
- **Open-Meteo** (open-meteo.com), para o tempo, somente se você ligar: envia a latitude e a longitude da cidade escolhida, arredondadas para cerca de 1 km. Ao buscar a cidade, envia o nome digitado. Se você clicar em "Usar minha localização", o Yata pede ao Windows a posição uma única vez, arredonda para cerca de 1 km na hora e descarta o valor preciso. Ele não acompanha a localização depois disso e nunca descobre onde você está pelo IP. Guarda nas configurações só o nome escolhido e as coordenadas arredondadas.
- **GitHub** (api.github.com), para o aviso de versão nova, somente se você ligar (vem desligado): uma vez por dia, pergunta qual é a versão mais recente do Yata. O pedido leva só o nome e a versão do Yata.
- **Google Agenda** e **Google Tasks**, somente se você conectar sua conta e liberar cada um (veja abaixo).

## Google Agenda

Ao conectar, você autoriza o Yata, na tela do próprio Google, a:

- ver seu endereço de e-mail, só para mostrar qual conta está conectada;
- ver a lista das suas agendas, para você escolher quais aparecem;
- ver os eventos das agendas escolhidas (**somente leitura**).

Como os dados são usados:

- O Yata busca os eventos **de hoje e de amanhã** para mostrá-los no notch e na aba Agenda e para avisar antes de cada um. Do evento, o app usa título, horário, status de resposta e link da chamada (Meet, Zoom, Teams).
- Os eventos ficam **só na memória** e somem quando o Yata fecha. Nada é gravado em disco, exceto a lista de agendas que você escolheu.
- O acesso concedido pelo Google (*refresh token*) e seu e-mail ficam em `%LOCALAPPDATA%\Yata\google.dat`, **cifrados com a proteção de dados do Windows (DPAPI)**: só o seu usuário do Windows, neste computador, consegue ler.
- A comunicação é feita **diretamente** entre o seu PC e os servidores do Google. Nenhum dado passa por servidores do Yata ou de terceiros, porque eles não existem.
- O Yata **não** cria, altera nem apaga eventos, **não** compartilha seus dados com ninguém, **não** os usa para publicidade e **não** os usa para treinar modelos de IA.

## Google Tasks

Se você liga **Tarefas** nas Configurações, o Yata pede ao Google permissão para **ver e editar suas tarefas**. A edição é necessária porque a sincronização vale nos dois sentidos.

- O Yata lê **uma lista** escolhida por você (de início, "Minhas tarefas"): as tarefas abertas e as concluídas hoje. Usa título, situação (aberta ou concluída) e data.
- Quando você cria, conclui, reabre ou remove uma tarefa no Yata, ele faz o mesmo nessa lista. Ele **não** mexe em outras listas nem em notas e subtarefas.
- As tarefas sincronizadas ficam em `%LOCALAPPDATA%\Yata\data.json`, junto com as tarefas locais, para a aba funcionar sem internet. Ao desligar a sincronização ou desconectar a conta, as tarefas vindas do Google são apagadas desse arquivo (elas continuam no Google).
- O horário de um lembrete ("Reunião 15:30") fica só no Yata. O Google Tasks recebe apenas o dia.
- Como na agenda, a comunicação é direta entre o seu PC e o Google, e os dados não são compartilhados, usados para publicidade ou para treinar modelos de IA.

O uso e a transferência, pelo Yata, de informações recebidas das APIs do Google seguem a [Política de dados do usuário dos serviços de API do Google](https://developers.google.com/terms/api-services-user-data-policy), incluindo os requisitos de Uso Limitado (*Limited Use*).

### Como remover o acesso

- No Yata: **Configurações → Google → Desconectar**. O acesso (agenda e tarefas) é revogado no Google e o arquivo local é apagado.
- No Google: <https://myaccount.google.com/connections>, removendo o Yata.
- Desinstalar o Yata não apaga a pasta `%LOCALAPPDATA%\Yata`. Apague a pasta manualmente se quiser remover tudo.

## Este site

O [getyata.app](https://getyata.app) é uma página estática hospedada no GitHub Pages e entregue pelo Cloudflare. Não grava cookies e não tem análise de acesso nem rastreadores. As fontes vêm do Google Fonts. O GitHub e o Cloudflare, como hospedagem e entrega, podem manter registros comuns de servidor. A mesma política está no site em [inglês](https://getyata.app/privacy) e [português](https://getyata.app/pt/privacy).

## Contato

Dúvidas ou problemas: abra uma issue em <https://github.com/akaneni/yata/issues>.
