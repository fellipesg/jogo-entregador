# Jogo do Entregador

Título provisório. Jogo mobile em Unity sobre a rotina de um entregador de app em São Paulo. A fatia vertical se passa em Santo Amaro (zona sul), num mapa montado de dados reais do OpenStreetMap: uma semana de trabalho, com aluguel da moto, chuva, imprevistos e a meta semanal do app.

## Versão

Unity 6000.6.10f1.

## Estrutura

```
Assets/_Jogo/
  Scripts/Core/       Regras do jogo em C# puro (sem UnityEngine): dinheiro, app, economia, imprevistos, clima, GameSession
  Scripts/Game/       Camada Unity (assembly Entregador.Game): moto, câmera, entrada, GPS, interface e GameFlow
  Scripts/Editor/     Gerador da cidade (CityBuilder*, leitor do OSM em Osm/) e configuração de URP/paisagem (assembly Entregador.Editor, só no Editor)
  Data/OSM/           Extrato do OpenStreetMap do centro de Santo Amaro (santo_amaro.json) e a licença ODbL
  Scenes/             SantoAmaro.unity (gerada pelo CityBuilder)
  Materials/          Materiais URP (gerados): um por kit do Kenney (Kenney_<Kit>.mat), cores simples e a fita do GPS
  Settings/           Asset URP mobile, renderer e material de física da moto
  Tests/EditMode/     Testes NUnit das regras
  Tests/PlayMode/     Testes da cena (moto, corrida, semana, tempo das corridas, GPS, corredor de ônibus, meio-fio, chuva, trânsito, reclamação do cliente, conta bloqueada, oferta que expira), do grafo de ruas (sentido único) e capturas de tela
Assets/ThirdParty/Kenney/   Modelos CC0 do Kenney (só os carros, semáforos antigos não são mais usados) com a License.txt de cada kit
Assets/ThirdParty/PolyHaven/ Texturas PBR e HDRIs CC0 do Poly Haven (ver "Arte fotográfica" abaixo)
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

Isso recria `SantoAmaro.unity` do zero (nunca edite a cena à mão), configura o URP mobile e a orientação paisagem, e põe a cena nos Build Settings. Toda criação de prédio, rua, árvore, carro e marcador passa por `CityPieces`/`MeshBatch`; o layout vem do OpenStreetMap (seção abaixo). Com a variável de ambiente `JOGO_DEBUG_DIR` definida, o gerador grava `grade.png` (ruas por classe, quarteirões, largo, parques e a ocupação dos lotes) para conferir o layout sem abrir o jogo.

## Mapa do OpenStreetMap

A cidade é o centro de Santo Amaro (em volta do Largo Treze de Maio) montado a partir de dados reais do OpenStreetMap: `Assets/_Jogo/Data/OSM/santo_amaro.json` (resposta da Overpass API com `out geom`, caixa de lat -23,6578 a -23,6488 e lon -46,7112 a -46,7014, cerca de 1 km²). **Os dados são © OpenStreetMap contributors, licença ODbL** (`Data/OSM/LICENSE.txt`); o jogo mostra o crédito "© OpenStreetMap contributors" no canto de baixo do minimapa.

Pipeline (tudo no assembly de Editor; a cena gerada já carrega o resultado):

1. **Leitura e projeção** (`Osm/OsmJson`, `Osm/OsmMap`): leitor mínimo de JSON; projeção equiretangular em torno do centro da caixa, x = leste, z = norte, 1 unidade = 1 m, sem altitude. Tudo é recortado pela caixa. A área jogável fica 25 m para dentro (`CityBuilder.PlayableHalf` = 475 m): paredes invisíveis e, em cada rua que cruza a borda, uma barreira de obra com a placa OBRA; a cidade (prédios, ruas) continua visível até a caixa.
2. **Ruas** (`CityBuilder.Network`): `primary/secondary` = principal, `tertiary`, `residential/living_street/unclassified` e `service` (só ruas de verdade, sem estacionamento nem passagem) viram fitas com largura por tipo (principal 14 m, terciária 10 m, residencial 7,5 m, serviço 5 m; sentido único usa menos), ou por `lanes` (3,0 a 3,4 m por faixa) e `width`. Faixas de ônibus (`psv:lanes=designated`) ficam fora das faixas comuns e saem vermelhas; `highway=busway` é todo vermelho; `pedestrian` é calçadão de piso hexagonal. Pistas duplas mapeadas como dois ways lado a lado têm a largura dividida na proporção para não se sobrepor.
3. **Quarteirões**: as ruas são pintadas numa grade de 0,5 m (com uma margem de 8 m de "rua virtual" fora da caixa). O que sobra entre as ruas são os quarteirões: uma abertura morfológica de 3,5 m arredonda as esquinas, as regiões conexas são rotuladas e o contorno de cada uma é traçado e simplificado (Douglas-Peucker, 0,85 m) num polígono anti-horário. O asfalto é um chão único; cada quarteirão é uma calçada elevada (15 cm) com guia por cima, então cruzamentos nunca têm vão nem z-fighting. Faixas e travessias ficam sobre o asfalto (pequenos deslocamentos em y).
4. **Prédios e fachada**: as pegadas do OSM (`building=*`, menos `roof`) viram prédios procedurais (`CityBuildings`): a fachada olha para a aresta de quarteirão mais próxima, o retângulo alinhado a ela cobre a pegada, e os andares vêm de `building:levels`/`height` (sem eles, por contexto: avenida e largo = comércio com loja no térreo e 2 a 4 andares, alguns prédios de 5 a 12; rua residencial = sobrado). O resto da frente de cada quarteirão é preenchido com lotes voltados para a rua (largura de 8 a 14 m, fundo de 15 m, reduzidos se esbarrarem na esquina ou num vizinho); só as paredes laterais expostas (esquina, vão) ganham janelas. Os fundos recebem galpões e casinhas baixos onde sobrou espaço (a pegada de cada prédio fica numa grade de ocupação, que também vira o collider das paredes). Parques/jardins, cemitério e estacionamentos viram grama ou piso, o largo é um disco de piso hexagonal com fonte, bancos e árvores.
5. **Grafo e GPS** (`RoadGraph`): cada vértice das ruas dirigíveis é um nó (nós do OSM compartilhados são os cruzamentos); `oneway=yes` vira aresta com direção; o GPS **respeita a mão**, mas andar contra ela custa 2,2x a distância (só compensa quando o desvio legal é bem maior; comprimentos são sempre em metros reais). **Corredores de ônibus e ruas de pedestres não entram no grafo** (ilegais para moto): o `CityMap` guarda as faixas de ônibus e o teste `GpsRoutesNeverDriveAlongTheBusCorridor` confere que nenhuma rota anda ao longo delas.
6. **Jogo**: 4 restaurantes (mesmos nomes, marcadores e fachadas) em frentes de rua reais perto do largo e das avenidas, 20 endereços de cliente nas ruas residenciais e terciárias de perto a longe, 14 buracos em ruas residenciais, 8 carros de trânsito em circuitos fechados reais (nós disjuntos, carros do mesmo circuito mantêm distância e quem cruza cede por ordem), carros estacionados rente à guia, pontos de ônibus nos `bus_stop` do OSM, árvores do OSM mais árvores de calçada, postes com fios, semáforos nos `traffic_signals`, faixas de pedestre nos `crossing`, placas de PARE e lombadas.

### Trocar de bairro

1. Baixe outro extrato com a Overpass API (`out geom`: ways e nós de `highway`, `building`, `leisure`, `landuse`, `amenity=parking`, `natural=tree`, `public_transport`/`bus_stop`) e substitua `Data/OSM/santo_amaro.json`.
2. Ajuste a caixa em `OsmMap` (`BoxSouth/North/West/East`), o centro e o raio do largo em `CityBuilder.Blocks` (`LargoCenter`, `LargoRadius`; use o ponto central de uma praça do novo extrato) e, se quiser, os nomes de rua de reserva em `CityBuilder.Places`.
3. Regere a cena (`BuildFromCommandLine`), rode os testes e, se os pedidos saírem curtos ou longos demais, ajuste `GameTuning.WorldToGameKm` (o teste `PlannedRunTimes...` imprime a tabela).

## Arte (Kenney, CC0)

Os modelos vêm dos kits do [Kenney](https://www.kenney.nl) (licença Creative Commons Zero, CC0; o crédito é opcional, mas fica aqui): **Car Kit 3.1**, **City Kit (Commercial) 2.1**, **City Kit (Roads)** e **City Kit (Suburban) 2.0**. Em `Assets/ThirdParty/Kenney/<Kit>/` ficam só os FBX usados, o `colormap.png` de cada kit e a `License.txt` original. Nenhum arquivo dessa pasta é executado: são dados.

| Kit | Uso |
| --- | --- |
| Roads | só os cones de obra nos buracos (ruas, postes, semáforos, placas e caçambas agora são geradas por código) |
| Commercial | não é mais usado (prédios gerados por `CityBuildings`) |
| Suburban | não é mais usado (sobrados e árvores gerados por código) |
| Car | carros estacionados, o trânsito (`TrafficLoop`) e as rodas (`wheel-default`) da moto |

Não existe moto nos kits: a moto e o piloto são procedurais (seção "Moto e piloto" abaixo); o Car Kit só fornece os carros.

## Moto e piloto (procedurais)

Moto e entregador são malhas lisas geradas por código (`Assets/_Jogo/Scripts/Game/Moto/`), sem primitivas do Unity: `MotoMeshKit` (loft de superelipses, tubos de ponta arredondada, revolução de perfis), `MotoModel` (moto), `RiderModel` (piloto), `MotoCatalogo` (enums e parâmetros) e `MotoVisual` (montagem e animação). O `BikeFactory` só cria Rigidbody + collider (inalterados) e o `MotoVisual`; as malhas são salvas em `Scenes/SantoAmaro_MotoMalhas.asset`.

- Modelos (`MotoModelo`, nomes fictícios): `Titan` = "Titã 160" (street clássica: farol redondo, tanque reto, banco longo, escape cromado) e `Twister` = "Tornado 250" (naked: carenagem angular com mini para-brisa, tanque com abas, banco escalonado, escape preto sob o motor). Cores: `MotoCor` (Vermelha, Preta, Azul, Branca).
- Personagens (`Personagem`): Jefinho (padrão), Robsao, Marcao, SeuValdir, Camila; corpo e cores em `MotoCatalogo.Config`.
- Trocar em jogo: `GetComponentInChildren<MotoVisual>().Configurar(MotoModelo.Twister, MotoCor.Azul, Personagem.Camila)` (remonta as malhas); `DefinirApp(cor, nome)` muda cor e texto do baú.
- Materiais: 4 (`Moto_Paleta` e `Moto_Metal` leem cor e suavidade de uma paleta 64x1; `Moto_Luzes` com emissão por MaterialPropertyBlock; `Moto_Bau`). Cerca de 7,9 mil triângulos moto + piloto.
- Animação (sem Animator): rodas giram, garfo/guidão esterçam no eixo inclinado, moto inclina pela velocidade x direção (só visual; piloto 35% a mais), parada tomba 5 graus e o pé esquerdo desce ao chão, lanterna acende ao frear. Braços e pernas usam IK de dois ossos até as manoplas e pedaleiras. O `ArcadeBikeController` só ganhou propriedades de leitura (`SteerInput`, `BrakeInput`, `Lean`).

## Arte semi-realista (Poly Haven, CC0)

A direção de arte é semi-realista: texturas fotográficas PBR, proporções de bairro paulistano, céu HDRI e pós-processamento. As texturas e os céus vêm do [Poly Haven](https://polyhaven.com) (licença CC0, em `Assets/ThirdParty/PolyHaven/LICENSE.txt`): asphalt_02, concrete_pavement, hexagonal_concrete_paving, concrete_floor_02, painted_plaster_wall, white_plaster_02, beige_wall_001, cracked_concrete_wall, concrete_wall_003, grey_plaster, red_brick_03, brick_4, clay_roof_tiles_02, corrugated_iron, rusty_metal_02, green_metal_rust, sparse_grass e os HDRIs kloofendal_48d_partly_cloudy, kloofendal_overcast e belfast_sunset. Os carros continuam sendo do Kenney (pintados em cores realistas por `CarPaint`).

### Pipeline de materiais

- `PbrMaterials` (menu `Jogo > Processar texturas PBR`): converte as normais EXR em PNG (e apaga os EXR), gera por conjunto o `<nome>_ms.png` (R = metal, G = AO, A = suavidade = 1 - rugosidade; o URP Lit usa a mesma textura em `_MetallicGlossMap` e `_OcclusionMap`) e a variante `_ms_wet.png` (suavidade alta) para asfalto, calçada e piso do largo. Importa tudo para celular (máx. 1024; 2048 para asfalto e calçada; ASTC 6x6 em Android/iOS; normais como Normal Map). Os `*_ao_*.jpg` e `*_arm_*.jpg` originais ficam (o ARM é a fonte do mapa de máscara).
- UVs em metros do mundo: `MeshBatch` gera os UVs de cada face em metros (u = posição · direção da face). O tiling do material é `1 / metros por repetição` (tabela `PbrMaterials.Sets`: reboco 2,5 m, tijolo 2 m, asfalto 6 m, calçada 3 m, telha 2 m). Nada é esticado, em qualquer tamanho de prédio.
- Poucos materiais: reboco são 12 cores muted x 2 texturas (`CityStyle`), mais metal, vidro, tintas e toldos; a cidade inteira usa dezenas de materiais, nunca um por prédio.

### Geração da cidade

Tudo vira malhas fundidas por pedaço do mapa de 100 m (`MeshBatch`, um submesh por material): um grupo para o chão (calçadas, faixas, pisos; aparece no minimapa) e outro para prédios e mobiliário alto (camada 8, que o minimapa não desenha). As malhas são salvas em binário em `Assets/_Jogo/Scenes/SantoAmaro_MalhasNN.asset` (arquivos de até ~50 MB; são GERADOS, mas a cena os referencia por GUID: faça commit deles junto com a cena ou regere). Peças:

- `CityBuildings`: o único gerador de prédios (determinístico por semente): sobrados (muro com portões, laje com parapeito ou telhado cerâmico/fibrocimento, andar de tijolo aparente, grades, caixa d'água, ar-condicionado), comércio com sobreloja (porta de enrolar aberta/fechada, vitrine, toldo e letreiro em TextMeshPro), prédios médios de 5 a 12 andares (janelas e varandas em geometria, casa de máquinas, caixa d'água) e os 4 restaurantes (porta aberta, toldo na cor, letreiro, mesas na calçada). Lotes a mais de 250 m do largo usam "detalhe reduzido" (`BuildingContext.Lite`) para caber no orçamento de triângulos. Os letreiros usam nomes inventados (nunca as marcas do OSM); há uma "Borracharia 24h".
- `CityBuilder.Network/Blocks/Lots/Furniture/Places` (partials do CityBuilder): ruas, faixas e travessias, quarteirões e colliders, prédios e lotes, mobiliário (postes de concreto com fios em catenária, luminárias, lixeiras laranja, pontos de ônibus, árvores, semáforos, placas de PARE, caçambas, lombadas, bueiros), restaurantes, endereços, buracos, o largo e os pontos de vista das capturas.
- Colliders: uma laje por quarteirão (malha com rampa de 0,8 m sobre a rua para a moto subir a guia), paredes dos prédios extrudadas do contorno da ocupação, cápsulas finas nos postes e troncos; nada por detalhe.

### Luz e atmosfera

`LightingSetup` (editor) cria sol, Volume global, ReflectionProbe e o componente `WeatherLook` (runtime, `Assets/_Jogo/Scripts/Game/WeatherLook.cs`), que o `GameFlow` chama a cada dia:

| Visual | Céu | Sol | Pós-processamento |
| --- | --- | --- | --- |
| Sol | HDRI partly cloudy; a direção do sol é lida do próprio HDRI | quente, sombra suave (1 cascata, 60 m) | ACES, exposição -0,35, saturação +8, bloco quente, bloom 0,22, vinheta 0,2 |
| Chuva | HDRI overcast | fraco, SEM sombras | ACES, dessaturado, vinheta 0,28; asfalto/calçada trocam para a versão molhada (mais escura e lisa); chuva de partículas mantida |
| Tarde | HDRI belfast sunset | laranja e baixo | ACES, saturação +12 (disponível: `WeatherLook.Apply(LookKind.Sunset)`, o jogo ainda não usa) |

Ambiente vem do skybox; neblina com a cor do horizonte do HDRI. SSAO existe como renderer feature DESLIGADO (`WeatherLook.UseSsao`, padrão false). URP: HDR ligado, MSAA 2x, sombras suaves de qualidade baixa.

### Regerar tudo

```
U=/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity
$U -batchmode -nographics -quit -projectPath "$(pwd)" -executeMethod Entregador.Editor.PbrMaterials.ProcessFromCommandLine   # só se mudar texturas
$U -batchmode -nographics -quit -projectPath "$(pwd)" -executeMethod Entregador.Editor.CityBuilder.BuildFromCommandLine     # cena + malhas
```

As malhas `SantoAmaro_Malhas*.asset` (~140 MB) ficam fora do git. Depois de clonar, rode o `BuildFromCommandLine` acima antes de abrir a cena.

Capturas: o `ScreenshotTests` (PlayMode, sem `-nographics`, `JOGO_SHOTS_DIR`) grava os PNGs e `stats.txt` com drawCalls, SetPass e triângulos (UnityStats) da vista de direção.

### Convenção de escala

Os FBX são importados com **escala 1** (`KenneyImportSettings`, um `AssetPostprocessor`; também desliga animação e materiais embutidos). O tamanho final é escolhido **na hora de posicionar**, em `CityPieces`, e os tamanhos dos modelos são medidos pelos `Renderer.bounds` (nunca chutados):

- kits de cidade: o tile de rua (1 unidade) vira 12 m (`CityScale = 12`); prédios comerciais usam até 12x, limitados para o fundo caber em 15 m;
- casas: `SuburbScale = 9.5`; carros: `CarScale = 1.5` (sedã de 2,55 m vira 3,8 m); rodas da moto 1,15x.
- Todos os modelos de um kit usam um único material URP/Lit com o `colormap.png` do kit (`CityMaterials.Kenney`).

Para trocar um modelo, mude só o nome em `CityBuilder`/`CityPieces` e regere a cena.

### Tempo das corridas

`GameTuning`: `WorldToGameKm = 0,0042` (3,5 km = 830 m de rua; no mapa do OSM as rotas entre restaurantes e endereços chegam a ~1.200 m, então 1,5 a 5,5 km cabem), `ReferenceSpeed = 7 m/s` (tempo previsto = metros / 7; a moto anda no máximo 13 m/s) e `MinPickupMeters = 150`. O `RunPlanner` junta restaurante e endereço cuja rota combina com a distância e, entre os pares quase igualmente bons, prefere o restaurante mais perto da moto (acima do mínimo de busca). Para pedidos de 2 a 5 km, o tempo previsto fica entre cerca de 100 e 235 s; o PlayMode `PlannedRunTimes...` confere.

### Testes de PlayMode e capturas

```
JOGO_SHOTS_DIR=/tmp/shots /Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$(pwd)" -runTests -testPlatform PlayMode \
  -testResults /tmp/play.xml -logFile /tmp/play.log
```

Sem `-nographics`: o teste `ScreenshotTests` grava PNGs 1600x900 em `JOGO_SHOTS_DIR` (avenida com corredor de ônibus e trânsito, rua residencial com HUD, minimapa e GPS, rua com postes e fios, a borracharia, o largo, os 4 restaurantes, um cruzamento com semáforos e faixas, uma curva diagonal, a borda do mapa, o mapa visto de cima com e sem prédios, dia de chuva, painéis), mais `stats.txt` com drawCalls, SetPass e triângulos. Os pontos de vista (`residencial`, `avenida`, `largo`, `cruzamento`, `curva`, `restauranteN`, `borda`) vêm do `CityMap.ViewPoints`, gerados pelo `CityBuilder`.

### Texto (TextMeshPro)

A interface usa TextMeshPro. Os "TMP Essential Resources" foram importados do pacote `com.unity.ugui` com `Entregador.Editor.TmpSetup.ImportEssentials` (`-executeMethod`, sem `-quit`); `TmpSetup.EnsureFallbackFont` liga a fonte dinâmica de reserva para ã, ç, é etc. (o `CityBuilder` chama isso). Nenhuma fonte do sistema é usada: só a LiberationSans (OFL).

O assembly `Entregador.Core` não referencia a Unity (`noEngineReferences`), então as regras podem ser testadas sem abrir cenas. Os valores de economia ficam em `EconomyConfig` e `AppConfig`.

## Regras: moto, CNH e trânsito (Core)

Perfil inicial (`PerfilInicial`, passado ao `GameSession`): moto **alugada** (caução + aluguel semanal, placa legal) ou moto **bob** (entrada R$ 150 + parcela informal de R$ 100/semana por 8 semanas, sem documentos) e se o jogador **tem CNH** categoria A. Padrão: alugada com CNH. Números em `EconomyConfig.Transito` (`TransitoConfig`); valores e pontos das infrações em `CodigoTransito` (tabela do CTB: velocidade até 20% R$ 130,16/4 pts, 20-50% R$ 195,23/5 pts, acima de 50% R$ 880,41/7 pts + suspensão, contramão, faixa de ônibus e sinal vermelho R$ 293,47/7 pts, sem CNH R$ 880,41, moto sem licenciamento R$ 293,47). Radar com tolerância do CTB (7 km/h até 100 km/h, 7% acima).

- CNH: suspensa aos 40 pontos (sem gravíssima), 30 (uma) ou 20 (duas ou mais); janela simplificada para o save inteiro. CNH suspensa conta como sem CNH.
- Radar: a cena põe as infrações em `RunReport.Capturas` (ou `AddRadar(medida, limite)`). Na moto alugada a multa chega de 2 a 5 dias depois, com taxa de indicação de R$ 20; na bob nunca chega.
- Abordagem: `SortearBlitzDoDia()` (35%, 50% nos dias 6 e 7), `IniciarBlitz()` ou `ReportarFlagrante(infracao)` devolvem uma `Abordagem`; `ResolverAbordagem(abordagem, Parar | Fugir)` devolve o `AbordagemOutcome`. Sem CNH: multa + moto no pátio (alugada) ou apreendida (bob). Bob: sempre apreendida, parcelas canceladas. Fuga: 45% de escapar; pega, soma tudo mais a desobediência e a moto é removida.
- Pátio: `CustoPatio` (guincho R$ 180 + R$ 45 por dia), `RetirarDoPatio()` (perde também o dia da retirada). Moto apreendida: `EscolherNovaMoto(origem)`; sem moto no fim da semana o resultado é `WeekResult.SemMoto`.
- Relatório: `WeekReport.Transito` (`TransitoReport`) com multas pagas e pendentes, pontos, status da CNH e da moto e as blitz.

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
