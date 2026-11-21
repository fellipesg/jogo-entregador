# Jogo do Entregador

Título provisório. Jogo mobile em Unity sobre a rotina de um entregador de app em São Paulo. A fatia vertical se passa em Santo Amaro (zona sul): uma semana de trabalho, com aluguel da moto, chuva, imprevistos e a meta semanal do app.

## Versão

Unity 6000.6.10f1.

## Estrutura

```
Assets/_Jogo/
  Scripts/Core/       Regras do jogo em C# puro (sem UnityEngine): dinheiro, app, economia, imprevistos, clima, GameSession
  Scripts/Game/       Camada Unity (assembly Entregador.Game): moto, câmera, entrada, GPS, interface e GameFlow
  Scripts/Editor/     Gerador da cidade e configuração de URP/paisagem (assembly Entregador.Editor, só no Editor)
  Scenes/             SantoAmaro.unity (gerada pelo CityBuilder)
  Materials/          Materiais URP do greybox (gerados)
  Settings/           Asset URP mobile, renderer e material de física da moto
  Tests/EditMode/     Testes NUnit das regras
  Tests/PlayMode/     Testes de fumaça da cena (moto, corrida completa, pular, semana inteira) e capturas de tela
Packages/             Dependências (URP, Input System, uGUI, test-framework)
```

## Primeira cena jogável

Abra `Assets/_Jogo/Scenes/SantoAmaro.unity` e aperte Play. O jogo roda uma semana inteira (7 dias, até 3 pedidos por turno) em cima da `GameSession`.

Controles:

| Ação | Teclado (Editor) | Celular |
| --- | --- | --- |
| Acelerar | W ou seta para cima | botão ACELERAR (direita) |
| Frear / ré | S, seta para baixo ou espaço | botão FREAR (direita) |
| Virar | A/D ou setas | botões < e > (esquerda) |

Fluxo: tela do dia (Começar turno, Folgar, Comprar capa) → oferta de pedido (15 s) → dirigir até o restaurante seguindo a linha azul do GPS → entrar no marcador → levar ao cliente → Fotografar e Entregar → resultado → próximo pedido → resumo do turno → próximo dia → relatório da semana. O botão "Pular corrida" faz a entrega automática. O pneu pode furar na metade da corrida.

Constantes de distância e tempo (km por metro, velocidade de referência, segundos por minuto de jogo, tempo da oferta) ficam no componente `GameTuning`, no objeto `GameRoot` da cena.

### Regerar a cidade

Menu `Jogo > Gerar cidade (Santo Amaro)` ou, pela linha de comando (editor fechado):

```
/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit -projectPath "$(pwd)" \
  -executeMethod Entregador.Editor.CityBuilder.BuildFromCommandLine -logFile /tmp/cidade.log
```

Isso recria `SantoAmaro.unity` do zero, configura o URP mobile e a orientação paisagem, e põe a cena nos Build Settings. Toda criação de prédio, árvore e marcador está em `CityPieces`: é lá que entram os modelos do Kenney City Kit no lugar das caixas.

### Testes de PlayMode e capturas

```
JOGO_SHOTS_DIR=/tmp/shots /Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$(pwd)" -runTests -testPlatform PlayMode \
  -testResults /tmp/play.xml -logFile /tmp/play.log
```

Sem `-nographics`: o teste `ScreenshotTests` grava PNGs 1600x900 em `JOGO_SHOTS_DIR` (direção em terceira pessoa com HUD, mapa visto de cima com a rota, painéis).

O assembly `Entregador.Core` não referencia a Unity (`noEngineReferences`), então as regras podem ser testadas sem abrir cenas. Os valores de economia ficam em `EconomyConfig` e `AppConfig`.

## Rodando os testes

No editor: Window → General → Test Runner → aba EditMode → Run All.

Pela linha de comando (com o editor fechado neste projeto; não use `-quit`):

```
/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath "$(pwd)" \
  -runTests -testPlatform EditMode \
  -testResults /tmp/test-results.xml \
  -logFile /tmp/unity-test.log
```

O resultado fica no XML (atributos `total`, `passed` e `failed` em `test-run`).
