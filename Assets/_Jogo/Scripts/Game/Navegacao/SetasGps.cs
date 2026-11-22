using System.Collections.Generic;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// As marcas 3D da próxima manobra, para ver a curva chegando em velocidade: uma seta dobrada pintada no chão do cruzamento
    /// (do trecho de chegada até a rua de saída) e, nas curvas fechadas, uma placa de chevron amarela flutuando no outro lado do cruzamento.
    /// Malhas em coordenadas do mundo, refeitas só quando a manobra muda; aparecem a partir de 180 m (placa: 220 m).
    /// </summary>
    public sealed class SetasGps
    {
        private const float Altura = 0.24f;
        private const float AparecerSeta = 180f;
        private const float AparecerPlaca = 220f;

        private GameObject _raiz;
        private MeshFilter _setaFilter;
        private MeshRenderer _setaRenderer;
        private MeshFilter _placaFilter;
        private MeshRenderer _placaRenderer;
        private Material _matSeta;
        private Material _matAmarelo;
        private Material _matPreto;
        private Instrucao _atual;

        public bool SetaVisivel => _setaRenderer != null && _setaRenderer.enabled;

        public bool PlacaVisivel => _placaRenderer != null && _placaRenderer.enabled;

        public void Esconder()
        {
            _atual = null;

            if (_setaRenderer != null)
            {
                _setaRenderer.enabled = false;
                _placaRenderer.enabled = false;
            }
        }

        public void Destruir()
        {
            if (_raiz != null)
            {
                Object.Destroy(_raiz);
            }
        }

        public void Atualizar(Instrucao inst, double distancia, Material baseMaterial)
        {
            if (inst == null || baseMaterial == null)
            {
                Esconder();

                return;
            }

            Garantir(baseMaterial);

            if (!ReferenceEquals(inst, _atual))
            {
                _atual = inst;
                Reconstruir(inst);
            }

            bool temSeta = inst.Manobra == Manobra.VireADireita || inst.Manobra == Manobra.VireAEsquerda
                || inst.Manobra == Manobra.MantenhaADireita || inst.Manobra == Manobra.MantenhaAEsquerda;
            _setaRenderer.enabled = temSeta && distancia <= AparecerSeta && distancia > -2.0;
            _placaRenderer.enabled = inst.Aguda && distancia <= AparecerPlaca && distancia > 3.0;
        }

        private void Garantir(Material baseMaterial)
        {
            if (_raiz != null)
            {
                return;
            }

            _raiz = new GameObject("GpsSetas");
            _matSeta = Clone(baseMaterial, new Color(1f, 0.86f, 0.12f));
            _matAmarelo = Clone(baseMaterial, new Color(1f, 0.8f, 0.05f));
            _matPreto = Clone(baseMaterial, new Color(0.04f, 0.04f, 0.05f));

            GameObject seta = Novo("Seta", out _setaFilter, out _setaRenderer);
            _setaRenderer.sharedMaterial = _matSeta;
            GameObject placa = Novo("Placa", out _placaFilter, out _placaRenderer);
            _placaRenderer.sharedMaterials = new[] { _matAmarelo, _matPreto };
            seta.SetActive(true);
            placa.SetActive(true);
            _setaRenderer.enabled = false;
            _placaRenderer.enabled = false;
        }

        private GameObject Novo(string nome, out MeshFilter filter, out MeshRenderer renderer)
        {
            var go = new GameObject(nome);
            go.transform.SetParent(_raiz.transform, false);
            filter = go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return go;
        }

        private static Material Clone(Material source, Color color)
        {
            var material = new Material(source) { name = "GpsSeta" };
            material.mainTexture = Texture2D.whiteTexture;
            material.mainTextureScale = Vector2.one;
            material.color = color;

            return material;
        }

        private void Reconstruir(Instrucao inst)
        {
            _setaFilter.sharedMesh = MeshSeta(inst);
            _placaFilter.sharedMesh = MeshPlaca(inst);
        }

        // ---------- Malhas ----------

        /// <summary>Seta dobrada: haste que chega ao ponto da manobra pela direção de entrada e braço com ponta pela direção de saída.</summary>
        private static Mesh MeshSeta(Instrucao inst)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 c = new Vector3((float)inst.X, Altura, (float)inst.Z);
            Vector3 e = new Vector3((float)inst.EntradaX, 0f, (float)inst.EntradaZ).normalized;
            Vector3 s = new Vector3((float)inst.SaidaX, 0f, (float)inst.SaidaZ).normalized;
            const float half = 0.7f;

            Faixa(verts, tris, c - e * 10f, c, half);
            Vector3 tip = c + s * 6f;
            Faixa(verts, tris, c, tip, half);

            // Ponta: triângulo largo no fim do braço.
            Vector3 side = new Vector3(s.z, 0f, -s.x);
            int b = verts.Count;
            verts.Add(tip + side * 1.7f);
            verts.Add(tip - side * 1.7f);
            verts.Add(tip + s * 2.6f);
            DuploTriangulo(tris, b, b + 1, b + 2);

            var mesh = new Mesh { name = "GpsSeta" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        private static void Faixa(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, float half)
        {
            Vector3 d = (b - a).normalized;
            Vector3 side = new Vector3(d.z, 0f, -d.x) * half;
            int i = verts.Count;
            verts.Add(a + side);
            verts.Add(a - side);
            verts.Add(b - side);
            verts.Add(b + side);
            DuploTriangulo(tris, i, i + 1, i + 2);
            DuploTriangulo(tris, i, i + 2, i + 3);
        }

        private static void DuploTriangulo(List<int> tris, int a, int b, int c)
        {
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
            tris.Add(a);
            tris.Add(c);
            tris.Add(b);
        }

        /// <summary>Placa de chevron (submalha 0 amarela, 1 preta), voltada para quem chega, com os chevrons apontando para o lado da curva.</summary>
        private static Mesh MeshPlaca(Instrucao inst)
        {
            Vector3 e = new Vector3((float)inst.EntradaX, 0f, (float)inst.EntradaZ).normalized;
            Vector3 right = new Vector3(e.z, 0f, -e.x);
            float turn = inst.Angulo >= 0 ? 1f : -1f;
            Vector3 center = new Vector3((float)inst.X, 0f, (float)inst.Z) + e * 7f - right * turn * 1.5f;

            var verts = new List<Vector3>();
            var yellow = new List<int>();
            var black = new List<int>();

            Vector3 P(float u, float v) => center + right * u + Vector3.up * v;

            // Poste e placa.
            Quad(verts, black, P(-0.05f, 0f), P(0.05f, 0f), P(0.05f, 1.4f), P(-0.05f, 1.4f));
            Quad(verts, yellow, P(-1.5f, 1.4f), P(1.5f, 1.4f), P(1.5f, 3.0f), P(-1.5f, 3.0f));

            // Dois chevrons: pontas no sentido da curva (para a direita do motorista se a curva é à direita).
            foreach (float cx in new[] { -0.55f, 0.55f })
            {
                float tipU = cx + turn * 0.3f;
                float backU = cx - turn * 0.3f;
                Tira(verts, black, P(backU, 2.75f) - e * 0.04f, P(tipU, 2.2f) - e * 0.04f, right * 0.16f);
                Tira(verts, black, P(backU, 1.65f) - e * 0.04f, P(tipU, 2.2f) - e * 0.04f, right * 0.16f);
            }

            var mesh = new Mesh { name = "GpsPlaca", subMeshCount = 2 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(yellow, 0);
            mesh.SetTriangles(black, 1);
            mesh.RecalculateBounds();

            return mesh;
        }

        private static void Quad(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            verts.Add(d);
            DuploTriangulo(tris, i, i + 1, i + 2);
            DuploTriangulo(tris, i, i + 2, i + 3);
        }

        private static void Tira(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 thickness)
        {
            // Um pouco à frente da placa (em direção a quem vem) para não brigar com a profundidade.
            Quad(verts, tris, a - thickness, a + thickness, b + thickness, b - thickness);
        }
    }
}
