using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    public sealed partial class ScreenshotTests
    {
        /// <summary>Radar com a placa, blitz vista da aproximação, painel de parada, tela de novo jogo e o HUD sem os botões antigos.</summary>
        [UnityTest]
        public IEnumerator CaptureFiscalizacao()
        {
            string dir = Dir;
            Camera main = Camera.main;
            var follow = main.GetComponent<FollowCamera>();

            // Tela de novo jogo, com a moto do perfil atrás (personagem trocado para ver a mudança).
            StartDryWeek();
            Flow.CycleCharacter();
            yield return new WaitForSeconds(0.3f);
            Save(dir, "s1_novo_jogo.png", main);
            Flow.ToggleOrigin();
            yield return null;
            Save(dir, "s2_novo_jogo_bob.png", main);
            Flow.ToggleOrigin();

            // Turno de um dia seco com blitz (o Core sorteia: procura uma semana cujo dia 1 tenha).
            for (int seed = 1; seed < 200; seed++)
            {
                Flow.NewWeek(seed);

                if (Flow.Session.IsRainingToday)
                {
                    continue;
                }

                Flow.BeginShift();

                if (Flow.Session.BlitzHoje)
                {
                    break;
                }
            }

            Assert.IsTrue(Flow.Session.BlitzHoje, "Nenhuma semana com blitz no dia 1.");
            Flow.AcceptOffer();
            yield return null;

            // Radar e placa na avenida: de longe e perto.
            SpeedCamera[] radars = Object.FindObjectsByType<SpeedCamera>(FindObjectsSortMode.None);
            System.Array.Sort(radars, (a, b) => string.CompareOrdinal(a.transform.parent.name, b.transform.parent.name));
            SpeedCamera radar = radars[0];
            string suffix = radar.transform.parent.name.Split('_')[1];
            ViewPoint radarView = View("radar" + suffix);
            yield return PlaceFacing(radarView.Position, radarView.Position + Heading(radarView.Yaw) * 50f);
            Flow.Bike.SetLocked(true);
            yield return new WaitForSeconds(0.4f);
            Save(dir, "r1_radar_placa_longe.png", main);
            Vector3 forward = radar.transform.forward;
            Vector3 nearSign = radar.transform.position - forward * (60f + 4f);
            yield return PlaceFacing(new Vector3(nearSign.x, 0.1f, nearSign.z), nearSign + forward * 40f);
            yield return new WaitForSeconds(0.3f);
            Save(dir, "r2_radar_placa_perto.png", main);
            // A caixa da câmera no poste, vista de baixo, e o flash (câmera solta, de frente para o radar).
            follow.enabled = false;
            Transform box = radar.transform.parent.Find("CaixaCamera");
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            main.fieldOfView = 50f;

            // A copa das árvores não tem collider: tira uma foto da caixa de cada radar (de perto, de baixo) para escolher a melhor.
            Vector3 camAt = box.position - forward * 8f - Vector3.up * 1.2f;

            for (int i = 0; i < radars.Length; i++)
            {
                Transform other = radars[i].transform.parent.Find("CaixaCamera");
                Vector3 f = radars[i].transform.forward;
                Look(main, other.position - f * 9f - Vector3.up * 1.0f + Vector3.Cross(Vector3.up, f) * 0.5f, other.position);
                yield return null;
                Save(dir, "r3_radar_caixa_" + i + ".png", main, false);
            }

            Look(main, camAt, box.position);
            yield return null;

            Flow.Bike.SetLocked(false);
            var body = Flow.Bike.GetComponent<Rigidbody>();
            Flow.Bike.Teleport(new Vector3(radar.transform.position.x, 0.1f, radar.transform.position.z) - forward * 3f, Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
            body.linearVelocity = forward * 22f;
            yield return new WaitForFixedUpdate();
            radar.Measure(Flow.Bike);
            Assert.AreEqual(1, radar.Captures, "O radar deveria capturar a 79 km/h.");
            Look(main, camAt, box.position);
            yield return null;
            Save(dir, "r4_radar_flash.png", main, false);
            follow.enabled = true;
            main.fieldOfView = 62f;
            Flow.Bike.SetLocked(true);

            // Blitz vista da aproximação (120 m antes não cabe no alcance de desenho; a 85 m já aparece o funil e a luz).
            BlitzSite site = null;

            foreach (BlitzSite candidate in Flow.BlitzSites)
            {
                if (candidate.IsOpen)
                {
                    site = candidate;
                }
            }

            Assert.IsNotNull(site);
            ViewPoint blitzView = View("blitz" + site.name.Split('_')[1]);
            yield return PlaceFacing(blitzView.Position, blitzView.Position + Heading(blitzView.Yaw) * 40f);
            yield return new WaitForSeconds(0.4f);
            Save(dir, "b1_blitz_aproximacao_85m.png", main);

            Vector3 closer = site.Position - site.Direction * 35f;
            yield return PlaceFacing(new Vector3(closer.x, 0.1f, closer.z), site.Position);
            yield return new WaitForSeconds(0.4f);
            Save(dir, "b2_blitz_35m.png", main);

            // Entra: a moto para e aparece o painel Parar / Fugir.
            Flow.Bike.SetLocked(false);
            Vector3 inside = site.Position - site.Direction * 3f;
            Flow.Bike.Teleport(new Vector3(inside.x, 0.1f, inside.z), Mathf.Atan2(site.Direction.x, site.Direction.z) * Mathf.Rad2Deg);
            follow.Snap();

            for (int i = 0; i < 400 && !Flow.AbordagemPanelShown; i++)
            {
                yield return null;
            }

            Assert.IsTrue(Flow.AbordagemPanelShown);
            Save(dir, "b3_painel_parar_fugir.png", main);
            Flow.ChooseAbordagem(Entregador.Core.AbordagemChoice.Parar);
            yield return null;
            Save(dir, "b4_resultado_abordagem.png", main);

            // HUD em movimento: sem ACELERAR / FREAR, com a placa do limite e a marcha.
            Flow.ResumeAfterAbordagem();
            Flow.Bike.SetLocked(false);
            Flow.Bike.GetComponent<BikeInput>().SetVirtual(0f, 1f, 0f);
            yield return new WaitForSeconds(2.5f);
            Save(dir, "h1_hud_sem_botoes.png", main);
            Flow.Bike.GetComponent<BikeInput>().ClearAll();
        }
    }
}
