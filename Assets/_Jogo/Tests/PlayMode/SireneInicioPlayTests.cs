using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>
    /// O jogo aberto como o jogador abre (menu principal, Novo jogo, exploração livre, moto parada): nenhuma sirene pode soar, e nenhuma fonte
    /// de som pode produzir um tom de sirene sem uma viatura em perseguição.
    /// </summary>
    public sealed class SireneInicioPlayTests
    {
        private bool _setupAntes;

        [TearDown]
        public void TearDown()
        {
            GameFlow.ShowSetupOnStart = _setupAntes;
            Time.timeScale = 1f;
        }

        private static string Caminho(Transform t)
        {
            var sb = new StringBuilder(t.name);

            for (Transform p = t.parent; p != null; p = p.parent)
            {
                sb.Insert(0, p.name + "/");
            }

            return sb.ToString();
        }

        private static List<string> Amostra(float t, string fase, out bool sirene)
        {
            sirene = false;
            var lines = new List<string>();

            foreach (AudioSource s in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!s.isPlaying)
                {
                    continue;
                }

                lines.Add($"[{fase} t={t:F1}] TOCANDO {Caminho(s.transform)} clip={(s.clip != null ? s.clip.name : "sem clipe")} vol={s.volume:F3}");

                if (s.GetComponent<SirenAudio>() != null && s.volume > 0.001f)
                {
                    sirene = true;
                }
            }

            foreach (PoliceUnit unit in Object.FindObjectsByType<PoliceUnit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                SirenAudio audio = unit.GetComponentInChildren<SirenAudio>(true);
                lines.Add($"[{fase} t={t:F1}] POLICIA {unit.name} ativa={unit.gameObject.activeInHierarchy} modo={unit.Mode} sirene={unit.SirenActive} luz={unit.LightsOn} audio.tocando={(audio != null && audio.Tocando)} vol={(audio != null ? audio.Volume : 0f):F3}");

                if (unit.SirenActive || (audio != null && audio.Tocando))
                {
                    sirene = true;
                }
            }

            return lines;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator NoPrimeiroMinutoDeJogoNenhumaSireneSoa()
        {
            _setupAntes = GameFlow.ShowSetupOnStart;
            GameFlow.ShowSetupOnStart = true;
            SceneFixture.Silenciar();
            SceneManager.LoadScene("SantoAmaro");
            yield return null;
            yield return null;

            GameFlow flow = Object.FindFirstObjectByType<GameFlow>();
            Assert.IsNotNull(flow);

            for (int i = 0; i < 60 && flow.State != FlowState.MainMenu; i++)
            {
                yield return null;
            }

            Assert.AreEqual(FlowState.MainMenu, flow.State, "O jogo abre no menu principal.");

            float t = 0f;
            string fase = "menu";
            bool jogou = false;
            bool comecou = false;
            var all = new List<string>();
            bool algumaSirene = false;

            while (t < 60f)
            {
                if (!jogou && t >= 15f)
                {
                    jogou = true;
                    fase = "setup";
                    Assert.IsTrue(flow.Ui.ClickModalButton("Jogar"));
                }

                if (!comecou && t >= 22f)
                {
                    comecou = true;
                    fase = "livre";
                    Assert.IsTrue(flow.Ui.ClickModalButton("Começar"));
                    Assert.AreEqual(FlowState.FreeRoam, flow.State);
                }

                List<string> lines = Amostra(t, fase, out bool sirene);
                all.AddRange(lines);
                algumaSirene |= sirene;
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
            }

            Debug.Log("SIRENE-PROBE\n" + string.Join("\n", all));
            Assert.IsFalse(algumaSirene, "Sem viatura em perseguição, nenhuma sirene pode soar nem estar ativa.");
        }
    }
}
