# Política de privacidade do Yata

*Última atualização: 26 de outubro de 2026*

O Yata é um app de código aberto para Windows que mostra um "notch" no topo da tela. Ele não tem conta, servidor nem telemetria. Esta política explica quais dados o Yata usa e para onde eles vão.

## Dados que ficam no seu computador

Tarefas, registro de água, caminhos da prateleira e configurações ficam em `%LOCALAPPDATA%\Yata\data.json`. Esses dados nunca são enviados para fora do PC.

## Serviços externos

O Yata só acessa a internet nestes casos:

- **LRCLIB** (lrclib.net), para buscar letras de músicas: envia artista, título e duração da faixa que está tocando. Pode ser desligado nas Configurações.
- **Open-Meteo** (open-meteo.com), para o tempo, somente se você ligar: envia a latitude e a longitude da cidade escolhida, arredondadas para cerca de 1 km. Ao buscar a cidade, envia o nome digitado. Se você clicar em "Usar minha localização", o Yata pede ao Windows a posição uma única vez, arredonda para cerca de 1 km na hora e descarta o valor preciso. Ele não acompanha a localização depois disso e nunca descobre onde você está pelo IP. Guarda nas configurações só o nome escolhido e as coordenadas arredondadas.
- **Google Agenda**, somente se você conectar sua conta (veja abaixo).

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

O uso e a transferência, pelo Yata, de informações recebidas das APIs do Google seguem a [Política de dados do usuário dos serviços de API do Google](https://developers.google.com/terms/api-services-user-data-policy), incluindo os requisitos de Uso Limitado (*Limited Use*).

### Como remover o acesso

- No Yata: **Configurações → Agenda → Desconectar**. O acesso é revogado no Google e o arquivo local é apagado.
- No Google: <https://myaccount.google.com/connections>, removendo o Yata.
- Desinstalar o Yata não apaga a pasta `%LOCALAPPDATA%\Yata`. Apague a pasta manualmente se quiser remover tudo.

## Contato

Dúvidas ou problemas: abra uma issue em <https://github.com/akaneni/yata/issues>.
