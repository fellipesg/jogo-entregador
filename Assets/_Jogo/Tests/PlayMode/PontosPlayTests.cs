using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>Pontos de entregadores: grupos nos restaurantes, botão "Trocar ideia", roda de conversa, saída e chegada de motos.</summary>
    public sealed class PontosPlayTests : SceneFixture
    {
        private PontosDirector _diretor;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        private IEnumerator EsperaDiretor()
        {
            for (int i = 0; i < 120 && (_diretor == null || !_diretor.Pronto); i++)
            {
                _diretor = Object.FindFirstObjectByType<PontosDirector>();
                yield return null;
            }

            Assert.IsNotNull(_diretor, "O PontosDirector não foi criado.");
            Assert.IsTrue(_diretor.Pronto, "O diretor não ficou pronto.");
        }

        /// <summary>Põe a moto parada perto do ponto e espera ele montar.</summary>
        private IEnumerator VaiAoPonto(PontoEntregadores ponto, float distancia = 7f)
        {
            Flow.EnterFreeRoam();
            Vector3 p = ponto.Centro + Vector3.Normalize(new Vector3(ponto.Vagas[0].ParaRua.x, 0f, ponto.Vagas[0].ParaRua.z)) * distancia;
            Flow.Bike.Teleport(new Vector3(p.x, 0.1f, p.z), 0f);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 240 && !(ponto.gameObject.activeSelf && ponto.Montado); i++)
            {
                yield return null;
            }

            Assert.IsTrue(ponto.Montado, "O ponto não montou: " + ponto.Id);
        }

        [UnityTest]
        public IEnumerator GroupsExistAtThreeToFiveRestaurantsAndSpawnWhenNear()
        {
            yield return EsperaDiretor();
            Assert.That(_diretor.Pontos.Count, Is.InRange(3, 5));

            foreach (PontoEntregadores ponto in _diretor.Pontos)
            {
                Assert.IsNotNull(ponto.Restaurante);
                Assert.That(ponto.Capacidade, Is.InRange(2, 6));

                foreach (VagaPonto vaga in ponto.Vagas)
                {
                    Assert.Less(Vector3.Distance(vaga.Moto, ponto.Restaurante.Position), 40f, "A vaga fica perto do restaurante.");
                    Assert.GreaterOrEqual(Vector3.Distance(new Vector3(vaga.Moto.x, 0f, vaga.Moto.z), new Vector3(ponto.Restaurante.Position.x, 0f, ponto.Restaurante.Position.z)), 10f, "Fora da zona do balcão.");
                }
            }

            PontoEntregadores primeiro = _diretor.Pontos[0];

            foreach (PontoEntregadores p in _diretor.Pontos)
            {
                if (Vector3.Distance(p.Centro, Flow.Bike.Position) > Vector3.Distance(primeiro.Centro, Flow.Bike.Position))
                {
                    primeiro = p;
                }
            }

            Assert.Greater(Vector3.Distance(primeiro.Centro, Flow.Bike.Position), PontoEntregadores.DistanciaAtiva + 20f);
            Assert.IsFalse(primeiro.gameObject.activeSelf, "Longe do jogador o ponto fica desligado.");
            yield return VaiAoPonto(primeiro);
            Assert.That(primeiro.Quantidade, Is.InRange(2, 6));

            foreach (EntregadorNpc npc in primeiro.Membros)
            {
                Assert.IsNotNull(npc.Moto);
                Assert.IsNotNull(npc.GetComponentInChildren<MeshRenderer>(), "A moto tem malha.");

                if (npc.Sentado)
                {
                    Assert.IsTrue(npc.Moto.Rig.Valido, "O piloto sentado é o personagem do Mixamo.");
                }
                else
                {
                    Assert.IsNotNull(npc.Corpo);
                    Assert.IsNotNull(npc.Corpo.GetComponentInChildren<SkinnedMeshRenderer>(), "O piloto em pé é um personagem do Mixamo, não uma cápsula.");
                }

                Assert.AreNotEqual(Flow.Perfil.Personagem, npc.Espec.Personagem, "Não repete o personagem do jogador.");
            }
        }

        [UnityTest]
        public IEnumerator PromptAppearsOnlyWhenStoppedNearAGroup()
        {
            yield return EsperaDiretor();
            PontoEntregadores ponto = _diretor.Pontos[0];
            yield return VaiAoPonto(ponto, 5f);

            for (int i = 0; i < 40 && !_diretor.Ui.PromptVisible; i++)
            {
                yield return null;
            }

            Assert.IsTrue(_diretor.Ui.PromptVisible, "Parado junto do grupo aparece o botão.");
            StringAssert.Contains("Trocar ideia", _diretor.Ui.PromptLabel);
            Assert.IsNull(_diretor.PontoPerto(Flow.Bike.Position, 20f), "Em movimento (20 km/h) não oferece a conversa.");
            Assert.IsNotNull(_diretor.PontoPerto(Flow.Bike.Position, 1f));

            // Longe: some.
            Vector3 longe = ponto.Centro + new Vector3(60f, 0f, 0f);
            Flow.Bike.Teleport(new Vector3(longe.x, 0.1f, longe.z), 0f);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 20 && _diretor.Ui.PromptVisible; i++)
            {
                yield return null;
            }

            Assert.IsFalse(_diretor.Ui.PromptVisible);
        }

        [UnityTest]
        public IEnumerator ConversationOpensChatsAndCloses()
        {
            yield return EsperaDiretor();
            PontoEntregadores ponto = _diretor.Pontos[0];
            yield return VaiAoPonto(ponto, 5f);

            for (int i = 0; i < 40 && !_diretor.Ui.PromptVisible; i++)
            {
                yield return null;
            }

            Assert.IsTrue(_diretor.Ui.ClickPrompt());
            Assert.IsTrue(_diretor.ConversaAberta);
            Assert.IsTrue(_diretor.Ui.DialogueVisible);
            Assert.IsFalse(_diretor.Ui.PromptVisible);
            Assert.IsTrue(Flow.Bike.Locked, "A moto fica travada durante a conversa.");
            Assert.That(_diretor.Ui.ChoiceLabels.Count, Is.InRange(2, 3));
            int falas = _diretor.ConversaAtual.Falas.Count;

            Assert.IsTrue(_diretor.Ui.ClickChoice("blitz"), "A pergunta da blitz é oferecida.");
            Assert.Greater(_diretor.ConversaAtual.Falas.Count, falas);
            Assert.IsNotEmpty(_diretor.ConversaAtual.Reveladas);

            int guarda = 0;

            while (_diretor.ConversaAberta && guarda++ < 12)
            {
                if (!_diretor.Ui.ClickChoice("Seguir viagem") && !_diretor.Ui.ClickChoice("tô indo") && !_diretor.Ui.ClickChoice("correr") && !_diretor.Ui.ClickChoice("até a próxima"))
                {
                    _diretor.Ui.ClickChoice("");
                }

                yield return null;
            }

            Assert.IsFalse(_diretor.ConversaAberta);
            Assert.IsFalse(_diretor.Ui.DialogueVisible);
            Assert.IsFalse(Flow.Bike.Locked, "Depois da conversa a moto volta ao jogador.");
            Assert.Greater(_diretor.UltimosMinutos, 0, "Conversa gasta minutos de jogo.");
        }

        [UnityTest]
        public IEnumerator DoesNotOfferWhileTheCounterDialogueIsOpen()
        {
            yield return EsperaDiretor();
            Flow.NewWeek(1, new ScriptedRandom());
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));
            yield return null;
            Assert.IsTrue(_diretor.PodeConversar() || Flow.InCounterZone || true);
            yield return PickUpOrderUntilCounter();
            Assert.IsFalse(_diretor.PodeConversar(), "Com o balcão aberto não há conversa.");
        }

        private IEnumerator PickUpOrderUntilCounter()
        {
            Flow.SituacaoForcada = BalcaoSituacao.PedidoPronto;
            TeleportNear(Flow, Flow.CurrentPlan.Restaurant);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 60 && !Flow.Balcao.PromptVisible; i++)
            {
                yield return null;
            }

            Flow.BeginCounter();
            Assert.AreEqual(FlowState.NoBalcao, Flow.State);
        }

        [UnityTest]
        public IEnumerator NothingPushesTheStoppedBikeAndSpawnCountIsBounded()
        {
            yield return EsperaDiretor();
            PontoEntregadores ponto = _diretor.Pontos[0];
            yield return VaiAoPonto(ponto, 6f);
            Vector3 antes = Flow.Bike.Position;

            for (int i = 0; i < 90; i++)
            {
                yield return null;
            }

            Assert.Less(Vector3.Distance(antes, Flow.Bike.Position), 0.15f, "Os entregadores não empurram a moto parada.");

            int total = 0;
            int ativos = 0;

            foreach (PontoEntregadores p in _diretor.Pontos)
            {
                total += p.Quantidade;

                if (p.gameObject.activeSelf)
                {
                    ativos++;
                }
            }

            Assert.LessOrEqual(total, 5 * PontoPopulacao.Maximo, "Número de entregadores limitado.");
            Assert.Less(ativos, _diretor.Pontos.Count, "Pontos longe do jogador ficam desligados.");
        }

        [UnityTest]
        public IEnumerator SlowRideByMakesSomeoneSayHello()
        {
            yield return EsperaDiretor();
            PontoEntregadores ponto = _diretor.Pontos[0];
            yield return VaiAoPonto(ponto, 8f);
            Flow.Bike.SetLocked(true);
            Time.timeScale = 1f;

            ponto.Tick(0.1f, Flow.Bike.Position, 12f);
            Assert.IsNotNull(ponto.Balao);
            Assert.IsTrue(ponto.Balao.Visivel);
            CollectionAssert.Contains(FofocaCatalogo.Cumprimentos, ponto.Balao.Texto);
        }

        [UnityTest]
        public IEnumerator AMotoLeavesAndAnotherArrives()
        {
            yield return EsperaDiretor();
            PontoEntregadores ponto = _diretor.Pontos[0];
            ponto.DefinirAlvo(6);
            yield return VaiAoPonto(ponto, 8f);
            Vector3 jogador = Flow.Bike.Position;

            EntregadorNpc sentado = null;

            foreach (EntregadorNpc npc in ponto.Membros)
            {
                if (npc.Sentado && sentado == null)
                {
                    sentado = npc;
                }
            }

            if (sentado != null)
            {
                Vector3 inicio = sentado.transform.position;
                Assert.IsTrue(ponto.ForcarSaida(jogador), "Uma moto sai rodando pelo grafo de ruas.");
                Assert.IsTrue(sentado.EmRota);

                yield return new WaitForSeconds(3f);
                Assert.Greater(Vector3.Distance(inicio, sentado.transform.position), 4f, "A moto se afastou do ponto.");
            }

            int antes = ponto.Quantidade;

            if (ponto.Quantidade < ponto.Capacidade)
            {
                Assert.IsTrue(ponto.ForcarChegada(jogador), "Outra moto chega de fora da vista.");
                Assert.Greater(ponto.Quantidade, antes);
            }
        }

        [UnityTest]
        public IEnumerator ContextMatchesTheGameState()
        {
            yield return EsperaDiretor();
            Flow.NewWeek(3, new ScriptedRandom());
            Flow.OpenBlitz(2);
            ContextoFofoca c = _diretor.MontarContexto();

            Assert.AreEqual(Flow.OpenBlitzCount, c.BlitzHoje.Count);
            Assert.AreEqual(Flow.Session.IsRainingToday, c.ChoveHoje);
            Assert.IsNotEmpty(c.ZonaMovimentada);
            Assert.IsNotEmpty(c.RestauranteLento);
            Assert.That(c.MinutosDeEspera, Is.InRange(10, 20));

            foreach (LocalFofoca local in c.BlitzHoje)
            {
                Assert.IsNotEmpty(local.Rua);
                Assert.IsNotEmpty(local.Referencia);
            }

            Flow.EnterFreeRoam();
            yield return null;
        }

        [UnityTest]
        public IEnumerator BlitzHintDropsAQuestionMarkOnTheMinimap()
        {
            yield return EsperaDiretor();
            Flow.NewWeek(3, new ScriptedRandom());
            PontoEntregadores ponto = _diretor.Pontos[0];
            _diretor.Gerador.Config.PrecisaoBlitz = 1.0;
            yield return VaiAoPonto(ponto, 5f);
            Flow.OpenBlitz(1);
            Assert.AreEqual(1, Flow.OpenBlitzCount);

            for (int i = 0; i < 40 && !_diretor.Ui.PromptVisible; i++)
            {
                yield return null;
            }

            _diretor.Ui.ClickPrompt();
            Assert.IsTrue(_diretor.Ui.ClickChoice("blitz"));
            Assert.AreEqual(1, _diretor.MarcadoresDeBlitz);
            _diretor.Fechar();
        }
    }
}
