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
  Materials/          Materiais URP (gerados): um por kit do Kenney (Kenney_<Kit>.mat), cores simples e a fita do GPS
  Settings/           Asset URP mobile, renderer e material de física da moto
  Tests/EditMode/     Testes NUnit das regras
  Tests/PlayMode/     Testes da cena (moto, corrida, semana, tempo das corridas, GPS, meio-fio, chuva, trânsito, reclamação do cliente, conta bloqueada, oferta que expira) e capturas de tela
Assets/ThirdParty/Kenney/   Modelos CC0 do Kenney (só o que é usado) com a License.txt de cada kit
Assets/TextMesh Pro/        TMP Essential Resources (fonte LiberationSans SDF, OFL) importados do pacote com.unity.ugui
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

Isso recria `SantoAmaro.unity` do zero (nunca edite a cena à mão), configura o URP mobile e a orientação paisagem, e põe a cena nos Build Settings. Toda criação de prédio, rua, árvore, carro e marcador passa por `CityPieces`; o layout (ruas, quarteirões, restaurantes, props, trânsito) está em `CityBuilder`.

## Arte (Kenney, CC0)

Os modelos vêm dos kits do [Kenney](https://www.kenney.nl) (licença Creative Commons Zero, CC0; o crédito é opcional, mas fica aqui): **Car Kit 3.1**, **City Kit (Commercial) 2.1**, **City Kit (Roads)** e **City Kit (Suburban) 2.0**. Em `Assets/ThirdParty/Kenney/<Kit>/` ficam só os FBX usados, o `colormap.png` de cada kit e a `License.txt` original. Nenhum arquivo dessa pasta é executado: são dados.

| Kit | Uso |
| --- | --- |
| Roads | tiles de rua (`road-straight`, `road-crossroad`), postes, semáforos nos cruzamentos da avenida, placas, caçambas, cones nos buracos |
| Commercial | prédios do quarteirão em volta da avenida e do largo (`building-a..n`, poucos arranha-céus), restaurantes (`building-k` + `detail-awning-wide` + guarda-sóis) |
| Suburban | casas dos quarteirões residenciais (`building-type-a..u`) e árvores de rua/praça |
| Car | carros estacionados, o trânsito (`TrafficLoop`) e as rodas (`wheel-default`) da moto |

Não existe moto nos kits: a moto é feita de primitivas em `BikeFactory` (corpo fino, tanque, guidão, farol, piloto com capacete, baú vermelho com tampa) com as rodas do Car Kit.

### Convenção de escala

Os FBX são importados com **escala 1** (`KenneyImportSettings`, um `AssetPostprocessor`; também desliga animação e materiais embutidos). O tamanho final é escolhido **na hora de posicionar**, em `CityPieces`, e os tamanhos dos modelos são medidos pelos `Renderer.bounds` (nunca chutados):

- kits de cidade: o tile de rua (1 unidade) vira 12 m (`CityScale = 12`); prédios comerciais usam até 12x, limitados para o fundo caber em 15 m;
- casas: `SuburbScale = 9.5`; carros: `CarScale = 1.5` (sedã de 2,55 m vira 3,8 m); rodas da moto 1,15x.
- Todos os modelos de um kit usam um único material URP/Lit com o `colormap.png` do kit (`CityMaterials.Kenney`).

Para trocar um modelo, mude só o nome em `CityBuilder`/`CityPieces` e regere a cena.

### Tempo das corridas

`GameTuning`: `WorldToGameKm = 0,0042` (3,5 km = 830 m de rua; as rotas da cidade chegam a ~1.300 m), `ReferenceSpeed = 7 m/s` (tempo previsto = metros / 7; a moto anda no máximo 13 m/s) e `MinPickupMeters = 150`. Para pedidos de 2 a 5 km, o tempo previsto fica entre cerca de 90 e 240 s; o PlayMode `PlannedRunTimes...` confere.

### Testes de PlayMode e capturas

```
JOGO_SHOTS_DIR=/tmp/shots /Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$(pwd)" -runTests -testPlatform PlayMode \
  -testResults /tmp/play.xml -logFile /tmp/play.log
```

Sem `-nographics`: o teste `ScreenshotTests` grava PNGs 1600x900 em `JOGO_SHOTS_DIR` (rua residencial com HUD e GPS, o largo com os restaurantes, a avenida com trânsito, mapa visto de cima, dia de chuva, painéis).

### Texto (TextMeshPro)

A interface usa TextMeshPro. Os "TMP Essential Resources" foram importados do pacote `com.unity.ugui` com `Entregador.Editor.TmpSetup.ImportEssentials` (`-executeMethod`, sem `-quit`); `TmpSetup.EnsureFallbackFont` liga a fonte dinâmica de reserva para ã, ç, é etc. (o `CityBuilder` chama isso). Nenhuma fonte do sistema é usada: só a LiberationSans (OFL).

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
