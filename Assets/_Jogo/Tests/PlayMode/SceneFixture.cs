using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>Carrega a cena SantoAmaro e entrega o GameFlow pronto. Base dos testes de PlayMode.</summary>
    public abstract class SceneFixture
    {
        protected GameFlow Flow;

        /// <summary>Os testes rodam mudos (o áudio é checado pelo estado das fontes, não pelo alto-falante). JOGO_TESTE_SOM=1 liga o som.</summary>
        public static void Silenciar()
        {
            AudioListener.volume = System.Environment.GetEnvironmentVariable("JOGO_TESTE_SOM") == "1" ? 1f : 0f;
        }

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            RadioPlayer.NaoCarregarInternet = true; // os testes de cena não dependem da rede nem do cache do rádio
            Silenciar();
            SceneManager.LoadScene("SantoAmaro");
            yield return null;
            yield return null;

            Flow = Object.FindFirstObjectByType<GameFlow>();
            Assert.IsNotNull(Flow, "GameFlow não encontrado na cena SantoAmaro.");

            // Espera o Start do GameFlow montar a interface e abrir a semana.
            int guard = 0;

            while (Flow.Session == null && guard++ < 60)
            {
                yield return null;
            }

            Assert.IsNotNull(Flow.Session, "A sessão não foi criada.");

            // O AudioDirector (e o RadioPlayer dentro dele) sobrevive às trocas de cena: cada teste começa com o rádio de fábrica.
            if (RadioPlayer.Instance != null)
            {
                RadioPlayer.Instance.ReiniciarParaTestes();
                RadioPlayer.Instance.Stream.UsarBackend(null);
            }

            // Os testes de fluxo antigos esperam a oferta logo depois do turno (sem espera nem ofertas extras sozinhas).
            // Os testes do ritmo de pedidos desligam isto (Flow.OfertasImediatas = false).
            Flow.OfertasImediatas = true;
        }

        protected static Place Restaurant(GameFlow flow) => flow.CurrentPlan.Restaurant;

        /// <summary>Ponto de vista nomeado gerado pelo CityBuilder (residencial, avenida, largo, cruzamento, curva, restauranteN).</summary>
        protected ViewPoint View(string name)
        {
            ViewPoint point = Flow.Map.FindViewPoint(name);
            Assert.IsNotNull(point, "Ponto de vista não gerado: " + name);

            return point;
        }

        /// <summary>Direção de avanço (plano) de um yaw em graus.</summary>
        protected static Vector3 Heading(float yaw) => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        /// <summary>
        /// Faz a retirada de verdade: para a moto na zona do restaurante, espera o botão "Retirar pedido", abre o balcão e pega o pedido
        /// (situação "pedido pronto"). Termina em DrivingToCustomer.
        /// </summary>
        protected IEnumerator PickUpOrder(GameFlow flow, BalcaoSituacao situacao = BalcaoSituacao.PedidoPronto)
        {
            flow.SituacaoForcada = situacao;
            TeleportNear(flow, flow.CurrentPlan.Restaurant);
            flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 60 && !flow.Balcao.PromptVisible; i++)
            {
                yield return null;
            }

            Assert.IsTrue(flow.Balcao.PromptVisible, "Parado na zona do restaurante o botão Retirar pedido deveria aparecer.");
            Assert.AreEqual(FlowState.DrivingToRestaurant, flow.State, "Só entrar na zona não retira o pedido.");
            Assert.IsTrue(flow.Balcao.ClickPrompt());
            Assert.AreEqual(FlowState.NoBalcao, flow.State);
            Assert.IsTrue(flow.Balcao.ClickChoice("Pegar o pedido"));
            Assert.AreEqual(FlowState.DrivingToCustomer, flow.State, "Depois do diálogo o pedido é retirado.");
            yield return null;
        }

        /// <summary>
        /// Chega ao cliente de verdade: para a moto na zona do endereço, espera o botão "Entregar pedido" e clica. Termina em AtDoor
        /// (com o diálogo da entrega aberto, na situação pedida; a padrão é "cliente desce").
        /// </summary>
        protected IEnumerator ArriveAtCustomer(GameFlow flow, EntregaSituacao situacao = EntregaSituacao.ClienteDesce)
        {
            flow.EntregaForcada = situacao;
            TeleportNear(flow, flow.CurrentPlan.Customer);
            flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 60 && !flow.Balcao.PromptVisible; i++)
            {
                yield return null;
            }

            Assert.IsTrue(flow.Balcao.PromptVisible, "Parado na zona do cliente o botão Entregar pedido deveria aparecer.");
            Assert.AreEqual(FlowState.DrivingToCustomer, flow.State, "Só entrar na zona não entrega o pedido.");
            Assert.IsTrue(flow.Balcao.ClickPrompt());
            Assert.AreEqual(FlowState.AtDoor, flow.State);
        }

        protected static void TeleportNear(GameFlow flow, Place place)
        {
            flow.Bike.Teleport(new Vector3(place.Position.x, 0.1f, place.Position.z), 0f);
        }
    }
}
