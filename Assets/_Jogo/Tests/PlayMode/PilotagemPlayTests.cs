using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>
    /// Pilotagem em alta velocidade: mede a moto reta a 30/60/90/110 km/h com um degrau de direção e um toque curto
    /// (guinada, aceleração lateral, inclinação, raio, ultrapassagem, atraso da câmera) e confere o comportamento esperado.
    /// A medição só registra no log (prefixo [PILOTAGEM]); as demais provas têm limites.
    /// </summary>
    public sealed class PilotagemPlayTests : SceneFixture
    {
        private sealed class Amostra
        {
            public float T;
            public float Guinada;       // graus/s do corpo da moto
            public float Trajetoria;    // graus/s da direção da velocidade
            public float AcelLateral;   // m/s²
            public float Inclinacao;    // graus (valor absoluto)
            public float Velocidade;    // m/s
            public float AtrasoCamera;  // graus
            public float Derrapagem;    // graus entre a moto e a velocidade
            public float Escape;        // Derrapagem da moto (0 a 1)
        }

        private GameObject _moto;
        private GameObject _camera;

        [TearDown]
        public void Limpar()
        {
            Time.timeScale = 1f;

            if (_moto != null)
            {
                UnityEngine.Object.Destroy(_moto);
            }

            if (_camera != null)
            {
                UnityEngine.Object.Destroy(_camera);
            }
        }

        private ArcadeBikeController NovaMoto(MotoModelo modelo, out BikeInput input, out Rigidbody corpo, bool comCamera)
        {
            _moto = new GameObject("MotoPilotagem");
            _moto.transform.position = new Vector3(0f, 5000f, 0f);
            corpo = _moto.AddComponent<Rigidbody>();
            corpo.useGravity = false;
            corpo.mass = 180f;
            input = _moto.AddComponent<BikeInput>();
            var moto = _moto.AddComponent<ArcadeBikeController>();
            moto.SetModelo(modelo);
            moto.SetLocked(false);

            if (comCamera)
            {
                _camera = new GameObject("CameraPilotagem");
                _camera.AddComponent<Camera>().enabled = false;
                _camera.AddComponent<FollowCamera>().Configure(moto);
            }

            return moto;
        }

        /// <summary>Segura a velocidade com o acelerador (controle simples) e registra amostras a cada passo de física.</summary>
        private IEnumerator Rodar(ArcadeBikeController moto, BikeInput input, Rigidbody corpo, float alvoKmh, float duracao, Func<float, float> direcao, List<Amostra> saida, float freio = 0f)
        {
            float t = 0f;
            float yawAnterior = moto.Yaw;
            Vector3 v0 = corpo.linearVelocity;
            float rumoAnterior = Mathf.Atan2(v0.x, v0.z) * Mathf.Rad2Deg;

            while (t < duracao)
            {
                float gas = freio > 0f ? 0f : Mathf.Clamp01(0.3f + 0.12f * (alvoKmh / 3.6f - moto.Speed));
                input.SetVirtual(direcao(t), gas, freio);
                yield return new WaitForFixedUpdate();
                float dt = Time.fixedDeltaTime;
                t += dt;

                Vector3 v = corpo.linearVelocity;
                float rumo = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                float guinada = Mathf.DeltaAngle(yawAnterior, moto.Yaw) / dt;
                float trajetoria = Mathf.DeltaAngle(rumoAnterior, rumo) / dt;
                yawAnterior = moto.Yaw;
                rumoAnterior = rumo;

                float atraso = 0f;

                if (_camera != null)
                {
                    atraso = Mathf.Abs(Mathf.DeltaAngle(_camera.transform.eulerAngles.y, moto.Yaw));
                }

                saida.Add(new Amostra
                {
                    T = t,
                    Guinada = guinada,
                    Trajetoria = trajetoria,
                    AcelLateral = moto.Speed * trajetoria * Mathf.Deg2Rad,
                    Inclinacao = Mathf.Abs(moto.Lean),
                    Velocidade = moto.Speed,
                    AtrasoCamera = atraso,
                    Derrapagem = Mathf.Abs(Mathf.DeltaAngle(moto.Yaw, rumo)),
                    Escape = moto.Derrapagem,
                });
            }
        }

        private IEnumerator Preparar(ArcadeBikeController moto, BikeInput input, Rigidbody corpo, float kmh, float segundos = 1.8f)
        {
            moto.Teleport(new Vector3(0f, 5000f, 0f), 0f);
            corpo.linearVelocity = Vector3.forward * (kmh / 3.6f);
            var lixo = new List<Amostra>();
            yield return Rodar(moto, input, corpo, kmh, segundos, _ => 0f, lixo);
        }

        private static float Media(List<Amostra> a, float de, float ate, Func<Amostra, float> f)
        {
            float soma = 0f;
            int n = 0;

            foreach (Amostra s in a)
            {
                if (s.T >= de && s.T < ate)
                {
                    soma += f(s);
                    n++;
                }
            }

            return n > 0 ? soma / n : 0f;
        }

        private static float Maximo(List<Amostra> a, float de, float ate, Func<Amostra, float> f)
        {
            float max = float.MinValue;

            foreach (Amostra s in a)
            {
                if (s.T >= de && s.T < ate)
                {
                    max = Mathf.Max(max, f(s));
                }
            }

            return max;
        }

        /// <summary>Degrau: direção a fundo por 3 s e solta; devolve as métricas.</summary>
        private sealed class Degrau
        {
            public float GuinadaEstavel;
            public float Pico;
            public float UltrapassagemPct;
            public float Tempo90;
            public float AcelLateral;
            public float Inclinacao;
            public float Raio;
            public float AtrasoCamera;
            public float DerrapagemMax;
            public float VoltaReta;
            public float Oscilacao;
        }

        private IEnumerator MedirDegrau(MotoModelo modelo, float kmh, Action<Degrau> resultado, bool molhado = false)
        {
            ArcadeBikeController moto = NovaMoto(modelo, out BikeInput input, out Rigidbody corpo, true);
            moto.SetWet(molhado);
            yield return null;
            yield return Preparar(moto, input, corpo, kmh);

            var a = new List<Amostra>();
            yield return Rodar(moto, input, corpo, kmh, 3.5f, t => 1f, a);
            var b = new List<Amostra>();
            yield return Rodar(moto, input, corpo, kmh, 3.5f, t => 0f, b);

            var d = new Degrau();
            d.GuinadaEstavel = Media(a, 2.7f, 3.5f, s => s.Guinada);
            d.Pico = Maximo(a, 0f, 3.5f, s => s.Guinada);
            d.UltrapassagemPct = d.GuinadaEstavel > 0.5f ? Mathf.Max(0f, (d.Pico - d.GuinadaEstavel) / d.GuinadaEstavel * 100f) : 0f;
            d.Tempo90 = 3.5f;

            foreach (Amostra s in a)
            {
                if (s.Guinada >= 0.9f * d.GuinadaEstavel)
                {
                    d.Tempo90 = s.T;

                    break;
                }
            }

            d.AcelLateral = Media(a, 2.7f, 3.5f, s => s.AcelLateral);
            d.Inclinacao = Media(a, 2.7f, 3.5f, s => s.Inclinacao);
            float trajetoria = Media(a, 2.7f, 3.5f, s => s.Trajetoria);
            d.Raio = Mathf.Abs(trajetoria) > 0.1f ? moto.Speed / (trajetoria * Mathf.Deg2Rad) : float.PositiveInfinity;
            d.AtrasoCamera = Media(a, 2.7f, 3.5f, s => s.AtrasoCamera);
            d.DerrapagemMax = Maximo(a, 0f, 3.5f, s => s.Derrapagem);

            // Oscilação: quantas vezes a guinada cruza o valor estável (com folga de 8%) depois de atingi-lo.
            int cruzes = 0;
            int lado = 0;

            foreach (Amostra s in a)
            {
                if (s.T < d.Tempo90)
                {
                    continue;
                }

                float desvio = s.Guinada - d.GuinadaEstavel;
                int novo = desvio > 0.08f * d.GuinadaEstavel ? 1 : (desvio < -0.08f * d.GuinadaEstavel ? -1 : 0);

                if (novo != 0 && lado != 0 && novo != lado)
                {
                    cruzes++;
                }

                if (novo != 0)
                {
                    lado = novo;
                }
            }

            d.Oscilacao = cruzes;

            // Soltou: tempo até a inclinação e a guinada sumirem.
            d.VoltaReta = 3.5f;

            foreach (Amostra s in b)
            {
                if (s.Inclinacao < 2f && Mathf.Abs(s.Guinada) < 2f)
                {
                    d.VoltaReta = s.T;

                    break;
                }
            }

            resultado(d);
        }

        private IEnumerator MedirToque(MotoModelo modelo, float kmh, Action<string> resultado)
        {
            ArcadeBikeController moto = NovaMoto(modelo, out BikeInput input, out Rigidbody corpo, true);
            yield return null;
            yield return Preparar(moto, input, corpo, kmh);
            float yaw0 = moto.Yaw;
            var a = new List<Amostra>();
            yield return Rodar(moto, input, corpo, kmh, 4f, t => t < 0.3f ? 1f : 0f, a);
            float pico = Maximo(a, 0f, 4f, s => s.Guinada);
            float giro = Mathf.DeltaAngle(yaw0, moto.Yaw);
            float picoLean = Maximo(a, 0f, 4f, s => s.Inclinacao);
            float parou = 4f;

            for (int i = a.Count - 1; i >= 0; i--)
            {
                if (a[i].Inclinacao > 2f)
                {
                    parou = a[i].T;

                    break;
                }
            }

            resultado(string.Format(CultureInfo.InvariantCulture, "toque0.3s: picoGuinada={0:F1}deg/s giroTotal={1:F1}deg picoInclinacao={2:F1}deg retaEm={3:F2}s", pico, giro, picoLean, parou));
        }

        private static string Texto(Degrau d)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "guinadaEstavel={0:F1}deg/s pico={1:F1} ultrapassagem={2:F0}% t90={3:F2}s acelLat={4:F2}m/s2 ({5:F2}g) inclinacao={6:F1}deg raio={7:F0}m atrasoCamera={8:F1}deg derrapagemMax={9:F1}deg voltaReta={10:F2}s cruzes={11:F0}",
                d.GuinadaEstavel, d.Pico, d.UltrapassagemPct, d.Tempo90, d.AcelLateral, d.AcelLateral / 9.81f, d.Inclinacao, d.Raio, d.AtrasoCamera, d.DerrapagemMax, d.VoltaReta, d.Oscilacao);
        }

        // ---------- Medição (só registra) ----------

        [UnityTest]
        public IEnumerator Diagnostico_Pilotagem()
        {
            float[] velocidades = { 30f, 60f, 90f, 110f };
            var sb = new StringBuilder();

            foreach (float kmh in velocidades)
            {
                Degrau d = null;
                yield return MedirDegrau(MotoModelo.Titan, kmh, r => d = r);
                sb.AppendLine("[PILOTAGEM] Titan " + kmh + " km/h degrau: " + Texto(d));
                UnityEngine.Object.Destroy(_moto);
                UnityEngine.Object.Destroy(_camera);
                yield return null;

                string toque = null;
                yield return MedirToque(MotoModelo.Titan, kmh, r => toque = r);
                sb.AppendLine("[PILOTAGEM] Titan " + kmh + " km/h " + toque);
                UnityEngine.Object.Destroy(_moto);
                UnityEngine.Object.Destroy(_camera);
                yield return null;
            }

            Debug.Log("\n" + sb);
            Assert.Pass();
        }

        // ---------- Provas ----------

        [UnityTest]
        public IEnumerator A100kmhDirecaoACheioFazArcoEstavelSemOscilar()
        {
            Degrau d = null;
            yield return MedirDegrau(MotoModelo.Titan, 100f, r => d = r);
            Debug.Log("[PILOTAGEM] prova 100 km/h: " + Texto(d));

            Assert.Greater(d.GuinadaEstavel, 8f, "A 100 km/h a direção a fundo ainda faz a curva.");
            Assert.Less(d.UltrapassagemPct, 10f, "A guinada se estabiliza sem passar do valor final.");
            Assert.LessOrEqual(d.Oscilacao, 0f, "Sem oscilação em torno do valor final.");
            Assert.Greater(d.Raio, 40f, "A 100 km/h a curva é aberta (raio grande).");
            Assert.Greater(d.Tempo90, 0.4f, "A guinada não aparece de repente: a inclinação leva tempo para subir.");
            Assert.Less(d.AcelLateral, 10.5f, "Aceleração lateral limitada pela aderência.");
        }

        [UnityTest]
        public IEnumerator SoltarADirecaoEmAltaVelocidadeEndireitaEmMenosDe1Ponto5Segundo()
        {
            Degrau d = null;
            yield return MedirDegrau(MotoModelo.Titan, 100f, r => d = r);

            Assert.Less(d.VoltaReta, 1.5f, "Soltando a direção a moto se levanta e volta a reta em até ~1,5 s.");
        }

        [UnityTest]
        public IEnumerator RetornoDeCurvaApertadaABaixaVelocidadeRaioMenorQue4m()
        {
            ArcadeBikeController moto = NovaMoto(MotoModelo.Titan, out BikeInput input, out Rigidbody corpo, false);
            yield return null;
            yield return Preparar(moto, input, corpo, 10f, 1.2f);
            var a = new List<Amostra>();
            yield return Rodar(moto, input, corpo, 10f, 2.5f, t => 1f, a);
            float trajetoria = Media(a, 1f, 2.5f, s => s.Trajetoria);
            float v = Media(a, 1f, 2.5f, s => s.Velocidade);
            float raio = v / (trajetoria * Mathf.Deg2Rad);
            Debug.Log("[PILOTAGEM] retorno 10 km/h: raio=" + raio.ToString("F2", CultureInfo.InvariantCulture) + " m v=" + (v * 3.6f).ToString("F1", CultureInfo.InvariantCulture) + " km/h");

            Assert.Greater(raio, 0f);
            Assert.Less(raio, 4f, "Retorno a 10 km/h cabe numa rua estreita (raio < 4 m).");
        }

        [UnityTest]
        public IEnumerator ChuvaReduzAAceleracaoLateralMaxima()
        {
            Degrau seco = null;
            Degrau molhado = null;
            yield return MedirDegrau(MotoModelo.Titan, 50f, r => seco = r);
            UnityEngine.Object.Destroy(_moto);
            UnityEngine.Object.Destroy(_camera);
            yield return null;
            yield return MedirDegrau(MotoModelo.Titan, 50f, r => molhado = r, true);
            Debug.Log("[PILOTAGEM] 50 km/h seco: " + Texto(seco) + "\n[PILOTAGEM] 50 km/h molhado: " + Texto(molhado));

            Assert.Less(molhado.AcelLateral, seco.AcelLateral * 0.8f, "Na chuva a aceleração lateral máxima cai (aderência ~0,6x).");
            Assert.Greater(molhado.AcelLateral, 2f, "Mas ainda faz curva.");
        }

        [UnityTest]
        public IEnumerator FreadaACheioDe60kmhParaEm15a18m()
        {
            ArcadeBikeController moto = NovaMoto(MotoModelo.Titan, out BikeInput input, out Rigidbody corpo, false);
            yield return null;
            yield return Preparar(moto, input, corpo, 60f);
            Vector3 inicio = moto.Position;
            float t = 0f;
            input.SetVirtual(0f, 0f, 1f);

            while (moto.ForwardSpeed > 0.3f && t < 6f)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
            }

            float distancia = Vector3.Distance(inicio, moto.Position);
            Debug.Log("[PILOTAGEM] frenagem 60 km/h: " + distancia.ToString("F1", CultureInfo.InvariantCulture) + " m em " + t.ToString("F2", CultureInfo.InvariantCulture) + " s");

            Assert.GreaterOrEqual(distancia, 13.5f);
            Assert.LessOrEqual(distancia, 18.5f, "Titã a 60 km/h com freio total para em ~15-18 m.");
        }

        [UnityTest]
        public IEnumerator FrearInclinadoAlargaALinhaEDerrapa()
        {
            ArcadeBikeController moto = NovaMoto(MotoModelo.Titan, out BikeInput input, out Rigidbody corpo, false);
            yield return null;
            yield return Preparar(moto, input, corpo, 80f);
            var a = new List<Amostra>();
            yield return Rodar(moto, input, corpo, 80f, 2f, t => 1f, a);
            Assert.IsFalse(moto.Derrapando, "Curva firme sem freio não derrapa.");
            Assert.AreEqual(0f, Maximo(a, 0f, 2f, s => s.Escape), 0.01f);
            float raioAntes = moto.Speed / (Media(a, 1f, 2f, s => s.Trajetoria) * Mathf.Deg2Rad);

            var b = new List<Amostra>();
            bool derrapou = false;
            yield return Rodar(moto, input, corpo, 0f, 1.2f, t => 1f, b, 1f);

            foreach (Amostra s in b)
            {
                derrapou |= s.Escape > 0.3f;
            }

            float raioFreando = moto.Speed / (Mathf.Max(0.01f, Media(b, 0.5f, 1.2f, s => s.Trajetoria)) * Mathf.Deg2Rad);
            Debug.Log("[PILOTAGEM] freando inclinado: raioAntes=" + raioAntes.ToString("F0", CultureInfo.InvariantCulture) + " raioFreando=" + raioFreando.ToString("F0", CultureInfo.InvariantCulture) + " derrapou=" + derrapou);

            Assert.IsTrue(derrapou, "Freio forte inclinado faz a frente escapar (derrapagem).");
        }
    }
}
