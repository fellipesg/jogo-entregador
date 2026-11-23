using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    public sealed partial class ScreenshotTests
    {
        /// <summary>Moto inclinada numa curva em alta velocidade: câmera de perseguição (com rolagem e campo de visão aberto) e câmera lateral.</summary>
        [UnityTest]
        public IEnumerator CapturePilotagemCurvaRapida()
        {
            string dir = Dir;
            Camera main = Camera.main;
            var follow = main.GetComponent<FollowCamera>();
            StartDryWeek();
            Flow.Ui.SetHudVisible(false);
            ViewPoint avenida = View("avenida");
            Vector3 heading = Heading(avenida.Yaw);
            Vector3 spot = ClearSpot(avenida);
            ArcadeBikeController bike = Flow.Bike;
            var input = bike.GetComponent<BikeInput>();
            var body = bike.GetComponent<Rigidbody>();
            bike.SetLocked(false);
            bike.Teleport(new Vector3(spot.x, 0.1f, spot.z), avenida.Yaw);
            follow.Snap();
            body.linearVelocity = heading * 27f;
            input.SetVirtual(0f, 0.7f, 0f);
            yield return new WaitForSeconds(1.5f);

            input.SetVirtual(0.8f, 0.5f, 0f);
            yield return new WaitForSeconds(1.2f);
            Save(dir, "p1_curva_rapida_perseguicao.png", main, false);

            follow.enabled = false;
            Transform bt = bike.transform;
            Look(main, bt.position + bt.forward * 3.4f + bt.right * 2.6f + Vector3.up * 1.2f, bt.position + Vector3.up * 0.9f);
            yield return null;
            Save(dir, "p2_curva_rapida_frente.png", main, false);
            Look(main, bt.position - bt.forward * 3.2f + bt.right * 1.4f + Vector3.up * 1.5f, bt.position + Vector3.up * 0.9f);
            yield return null;
            Save(dir, "p3_curva_rapida_tras.png", main, false);

            input.ClearAll();
            bike.SetLocked(true);
            follow.enabled = true;
            yield return null;
        }
    }
}
