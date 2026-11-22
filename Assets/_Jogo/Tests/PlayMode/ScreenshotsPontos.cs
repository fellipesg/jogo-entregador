using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    public sealed partial class ScreenshotTests
    {
        private IEnumerator MontaPonto(PontosDirector diretor, PontoEntregadores ponto)
        {
            Flow.EnterFreeRoam();
            Vector3 p = ponto.Centro + ponto.Vagas[0].ParaRua * 9f;
            Flow.Bike.Teleport(new Vector3(p.x, 0.1f, p.z), 0f);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 300 && !(ponto.gameObject.activeSelf && ponto.Montado); i++)
            {
                yield return null;
            }

            Assert.IsTrue(ponto.Montado);

            for (int i = 0; i < 60; i++)
            {
                yield return null;
            }
        }

        /// <summary>Câmera na rua, de frente para o ponto, olhando a fileira de motos.</summary>
        private static void OlhaPonto(Camera main, PontoEntregadores ponto, float afastamento, float altura, float lateral)
        {
            VagaPonto v = ponto.Vagas[0];
            Vector3 tangente = Vector3.Cross(Vector3.up, v.ParaRua);
            Vector3 centro = ponto.Centro + Vector3.up * 0.9f;
            Look(main, ponto.Centro + v.ParaRua * afastamento + Vector3.up * altura + tangente * lateral, centro);
        }

        /// <summary>O ponto de entregadores de dia, de perto e de longe; o mesmo na chuva e no fim de tarde; a roda de conversa; o "Salve!".</summary>
        [UnityTest]
        public IEnumerator CapturePontosDeEntregadores()
        {
            string dir = Dir;
            Camera main = Camera.main;
            var follow = main.GetComponent<FollowCamera>();
            var diretor = Object.FindFirstObjectByType<PontosDirector>();

            for (int i = 0; i < 120 && (diretor == null || !diretor.Pronto); i++)
            {
                diretor = Object.FindFirstObjectByType<PontosDirector>();
                yield return null;
            }

            Assert.IsTrue(diretor.Pronto);
            StartDryWeek();
            var look = Object.FindFirstObjectByType<WeatherLook>();
            look.Apply(LookKind.Sunny);

            for (int n = 0; n < diretor.Pontos.Count; n++)
            {
                PontoEntregadores ponto = diretor.Pontos[n];
                diretor.Pontos[n].DefinirAlvo(5);
                yield return MontaPonto(diretor, ponto);
                follow.enabled = false;
                main.fieldOfView = 55f;
                OlhaPonto(main, ponto, 11f, 2.4f, -2f);
                yield return null;
                Save(dir, "p" + n + "_dia_rua.png", main, false);

                OlhaPonto(main, ponto, 5.5f, 1.7f, 1f);
                main.fieldOfView = 60f;
                yield return null;
                Save(dir, "p" + n + "_dia_perto.png", main, false);
                follow.enabled = true;
            }

            // Chuva e fim de tarde no primeiro ponto.
            PontoEntregadores um = diretor.Pontos[0];
            yield return MontaPonto(diretor, um);
            follow.enabled = false;
            for (int seed = 1; seed < 80; seed++)
            {
                Flow.NewWeek(seed);

                if (Flow.Session.IsRainingToday)
                {
                    break;
                }
            }

            Flow.EnterFreeRoam();
            look.Apply(LookKind.Rain);
            um.gameObject.SetActive(true);
            yield return new WaitForSeconds(0.5f);
            OlhaPonto(main, um, 9f, 2.2f, -1.5f);
            main.fieldOfView = 58f;
            yield return null;
            Save(dir, "p_chuva.png", main, false);

            look.Apply(LookKind.Sunset);
            yield return null;
            Save(dir, "p_fim_de_tarde.png", main, false);
            look.Apply(LookKind.Sunny);

            // "Salve!" quando o jogador passa devagar.
            OlhaPonto(main, um, 6f, 1.9f, 0.5f);
            um.ForcarSalve();
            yield return null;
            yield return null;
            Save(dir, "p_salve.png", main, false);

            // A roda de conversa aberta (com a pergunta da blitz respondida).
            Flow.NewWeek(5, new ScriptedRandom());
            diretor.Gerador.Config.PrecisaoBlitz = 1.0;
            yield return MontaPonto(diretor, um);
            Flow.OpenBlitz(1);
            follow.enabled = true;
            follow.Snap();
            main.fieldOfView = 62f;

            for (int i = 0; i < 40 && !diretor.Ui.PromptVisible; i++)
            {
                yield return null;
            }

            SaveAll(dir, "p_prompt.png", main);
            diretor.Ui.ClickPrompt();
            yield return null;
            diretor.Ui.ClickChoice("blitz");
            yield return null;
            yield return null;
            SaveAll(dir, "p_conversa.png", main);
            diretor.Ui.ClickChoice("");
            yield return null;
            SaveAll(dir, "p_conversa2.png", main);
            diretor.Fechar();
        }
    }
}
