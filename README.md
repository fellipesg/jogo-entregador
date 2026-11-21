# Jogo do Entregador

Título provisório. Jogo mobile em Unity sobre a rotina de um entregador de app em São Paulo. A fatia vertical se passa em Santo Amaro (zona sul): uma semana de trabalho, com aluguel da moto, chuva, imprevistos e a meta semanal do app.

## Versão

Unity 6000.6.10f1.

## Estrutura

```
Assets/_Jogo/
  Scripts/Core/       Regras do jogo em C# puro (sem UnityEngine): dinheiro, app, economia, imprevistos, clima, GameSession
  Tests/EditMode/     Testes NUnit das regras
Packages/             Dependências (com.unity.test-framework)
```

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
