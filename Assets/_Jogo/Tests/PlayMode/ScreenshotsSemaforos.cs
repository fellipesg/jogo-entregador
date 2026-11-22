using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    public sealed partial class ScreenshotTests
    {
        /// <summary>Semáforo no vermelho de dia, no verde, ao entardecer e na chuva (lentes brilhando), e o fotossensor com a placa e o flash.</summary>
        [UnityTest]
        public IEnumerator CaptureSemaforos()
        {
            string dir = Dir;
            Camera main = Camera.main;
            var follow = main.GetComponent<FollowCamera>();
            StartDryWeek();
            Flow.Ui.SetHudVisible(false);
            Flow.Bike.SetLocked(true);
            follow.enabled = false;
            var look = Object.FindFirstObjectByType<WeatherLook>();

            SemaforoControlador plain = null;
            SemaforoControlador camera = null;

            foreach (SemaforoControlador c in SemaforoControlador.Lista)
            {
                if (c.Fotossensor && camera == null)
                {
                    camera = c;
                }
                else if (!c.Fotossensor && (plain == null || c.Aproximacoes.Length > plain.Aproximacoes.Length))
                {
                    plain = c;
                }
            }

            Assert.IsNotNull(plain);
            Assert.IsNotNull(camera);
            SemaforoAproximacao a = plain.Aproximacoes[0];
            Vector3 head = a.Vermelhos[0].transform.position;
            Vector3 eye = a.Linha - a.Direcao * 8f + a.Direita * (a.LarguraEsquerda > 1f ? 0f : a.LarguraDireita * 0.5f);
            eye.y = 1.9f;
            Flow.Bike.Teleport(new Vector3(eye.x, 0.1f, eye.z) - a.Direcao * 5f, Mathf.Atan2(a.Direcao.x, a.Direcao.z) * Mathf.Rad2Deg);

            look.Apply(LookKind.Sunny);
            plain.Fixar(a.Grupo, SemaforoEstado.Vermelho);
            yield return null;
            Look(main, eye, head);
            yield return null;
            Save(dir, "sem1_vermelho_dia.png", main, false);

            plain.Fixar(a.Grupo, SemaforoEstado.Verde);
            yield return null;
            Save(dir, "sem2_verde_dia.png", main, false);

            look.Apply(LookKind.Sunset);
            plain.Fixar(a.Grupo, SemaforoEstado.Vermelho);
            yield return null;
            Save(dir, "sem3_vermelho_entardecer.png", main, false);

            look.Apply(LookKind.Rain);
            plain.Fixar(a.Grupo, SemaforoEstado.Amarelo);
            yield return null;
            Save(dir, "sem4_amarelo_chuva.png", main, false);
            plain.Fixar(a.Grupo, SemaforoEstado.Vermelho);
            yield return null;
            Save(dir, "sem5_vermelho_chuva.png", main, false);

            // Visão de motorista do cruzamento inteiro, no vermelho do entardecer.
            look.Apply(LookKind.Sunset);
            Look(main, a.Linha - a.Direcao * 30f + Vector3.up * 2.2f + a.Direita * (a.LarguraDireita * 0.5f), a.Linha + a.Direcao * 12f + Vector3.up * 3.2f);
            yield return null;
            Save(dir, "sem6_cruzamento_entardecer.png", main, false);

            // Fotossensor: placa 35 m antes, a caixa no braço e o flash disparado.
            look.Apply(LookKind.Sunny);
            SemaforoAproximacao ca = camera.Aproximacoes[0];
            camera.Fixar(ca.Grupo, SemaforoEstado.Vermelho, 3.0);
            Transform sign = null;

            foreach (Transform t in camera.GetComponentsInChildren<Transform>())
            {
                if (t.name.EndsWith("_Placa") && t.name.StartsWith("Foco_" + camera.name.Substring("Semaforo_".Length) + "_" + System.Array.IndexOf(camera.Aproximacoes, ca)))
                {
                    sign = t;
                }
            }

            Assert.IsNotNull(sign, "A placa do fotossensor existe.");
            Look(main, sign.position - ca.Direcao * 11f - Vector3.up * 1.1f, sign.position);
            yield return null;
            Save(dir, "sem7_fotossensor_placa.png", main, false);

            Vector3 box = ca.FlashLente.transform.position;
            Look(main, ca.Linha - ca.Direcao * 6f + Vector3.up * 1.7f + ca.Direita * (ca.LarguraDireita * 0.5f), box);
            camera.Capturar(ca);
            yield return null;
            Save(dir, "sem8_fotossensor_flash.png", main, false);
            follow.enabled = true;
        }
    }
}
