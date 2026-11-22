using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Marcador discreto do destino atual: anel tracejado fino e brilhante no nível da guia (raio de 3 m, pulsando devagar) e um pino
    /// flutuando sobre a porta. Aparece só para o alvo (CityMap.Highlight). Criado em runtime; os discos grandes da cena antiga ficam apagados.
    /// </summary>
    public sealed class AlvoMarcador : MonoBehaviour
    {
        public const float Raio = 3f;
        private const int Tracos = 28;

        private static readonly Color CorApp = new Color(1f, 0.55f, 0.1f);

        private Material _anel;
        private Material _pino;
        private Transform _pinoT;
        private Transform _anelT;
        private float _base;

        public bool Visivel => gameObject.activeSelf;

        public Vector3 Centro { get; private set; }

        public static AlvoMarcador Criar(Transform pai)
        {
            var go = new GameObject("AlvoMarcador");
            go.transform.SetParent(pai, false);
            var m = go.AddComponent<AlvoMarcador>();
            m.Montar();
            go.SetActive(false);

            return m;
        }

        public void Mostrar(Vector3 centro, Vector3 porta)
        {
            Centro = centro;
            transform.position = new Vector3(centro.x, 0f, centro.z);
            _anelT.position = new Vector3(centro.x, centro.y + 0.12f, centro.z);
            _base = porta.y + 3.4f;
            _pinoT.position = new Vector3(porta.x, _base, porta.z);
            gameObject.SetActive(true);
        }

        public void Esconder()
        {
            gameObject.SetActive(false);
        }

        private void Montar()
        {
            _anel = Material(CorApp);
            _pino = Material(Color.white);
            _pino.mainTexture = TexturaPino();

            var anelGo = new GameObject("Anel");
            anelGo.transform.SetParent(transform, false);
            _anelT = anelGo.transform;
            anelGo.AddComponent<MeshFilter>().sharedMesh = MalhaAnel();
            Renderer(anelGo, _anel);

            var pinoGo = new GameObject("Pino");
            pinoGo.transform.SetParent(transform, false);
            _pinoT = pinoGo.transform;
            pinoGo.AddComponent<MeshFilter>().sharedMesh = Quad(0.9f);
            Renderer(pinoGo, _pino);
            pinoGo.AddComponent<FaceCamera>();
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            float pulso = 0.5f + 0.5f * Mathf.Sin(t * 2.2f);
            float escala = 1f + 0.05f * pulso;
            _anelT.localScale = new Vector3(escala, 1f, escala);
            _anelT.Rotate(0f, 6f * Time.unscaledDeltaTime, 0f);
            Color c = CorApp * (0.9f + 0.6f * pulso);
            c.a = 0.55f + 0.35f * pulso;
            _anel.SetColor("_BaseColor", c);
            _pinoT.position = new Vector3(_pinoT.position.x, _base + 0.18f * Mathf.Sin(t * 2f), _pinoT.position.z);
        }

        private static void Renderer(GameObject go, Material material)
        {
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private static Material Material(Color cor)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "AlvoMarcador" };
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", 5f);
            m.SetFloat("_DstBlend", 10f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            m.SetColor("_BaseColor", cor);

            return m;
        }

        /// <summary>Anel tracejado: 28 arcos finos (0,16 m de largura) deitados no plano.</summary>
        private static Mesh MalhaAnel()
        {
            var v = new List<Vector3>();
            var tri = new List<int>();
            const int passos = 4;
            float largura = 0.16f;

            for (int i = 0; i < Tracos; i++)
            {
                float a0 = i * Mathf.PI * 2f / Tracos;
                float a1 = a0 + Mathf.PI * 2f / Tracos * 0.62f;

                for (int s = 0; s <= passos; s++)
                {
                    float a = Mathf.Lerp(a0, a1, s / (float)passos);
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(d * (Raio - largura * 0.5f));
                    v.Add(d * (Raio + largura * 0.5f));

                    if (s > 0)
                    {
                        int b = v.Count - 4;
                        tri.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
                    }
                }
            }

            var mesh = new Mesh { name = "AnelTracejado" };
            mesh.SetVertices(v);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        private static Mesh Quad(float meio)
        {
            var mesh = new Mesh { name = "PinoQuad" };
            mesh.SetVertices(new List<Vector3> { new Vector3(-meio, -meio, 0), new Vector3(meio, -meio, 0), new Vector3(-meio, meio, 0), new Vector3(meio, meio, 0) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>Pino em gota na cor do app, com furo branco no meio.</summary>
        private static Texture2D TexturaPino()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "PinoAlvo" };
            var px = new Color[n * n];
            Vector2 cabeca = new Vector2(n * 0.5f, n * 0.62f);

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Vector2.Distance(p, cabeca);
                    float ponta = Mathf.InverseLerp(n * 0.62f, n * 0.08f, p.y);
                    float larg = Mathf.Lerp(n * 0.26f, 0f, ponta);
                    bool corpo = d < n * 0.26f || (p.y < n * 0.62f && p.y > n * 0.08f && Mathf.Abs(p.x - n * 0.5f) < larg);
                    Color c = Color.clear;

                    if (corpo)
                    {
                        c = d < n * 0.1f ? Color.white : new Color(1f, 0.6f, 0.15f, 1f);
                    }

                    px[y * n + x] = c;
                }
            }

            tex.SetPixels(px);
            tex.Apply();

            return tex;
        }
    }
}
