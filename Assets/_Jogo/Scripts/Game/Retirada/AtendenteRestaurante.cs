using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// A atendente do balcão de um restaurante: um personagem do Mixamo inteiro (prefab "_Atendente" de Assets/_Jogo/Personagens, com a
    /// cabeça e o cabelo do próprio modelo, sem capacete), com uma variação de tom por restaurante, avental de peitilho liso na cor do
    /// restaurante (peitilho, saia, alças no pescoço e faixa na cintura) e, às vezes, boné. As medidas do avental vêm do corpo em pose de
    /// referência (T). Sem Animator: a cada quadro põe os ossos numa pose de pé com os braços para baixo e uma leve oscilação de respiração.
    /// </summary>
    public sealed class AtendenteRestaurante : MonoBehaviour
    {
        [SerializeField] private GameObject _modelo;
        [SerializeField] private string _restaurante = string.Empty;
        [SerializeField] private Color _avental = Color.white;
        [SerializeField] private Color _tom = Color.white;
        [SerializeField] private bool _bone;
        [SerializeField] private float _fase;

        private Transform _corpo;
        private Transform _quadril;
        private Transform _coluna;
        private Transform _coluna2;
        private Transform _pescoco;
        private Transform _cabeca;
        private Transform _bracoE;
        private Transform _bracoD;
        private Transform _antebracoE;
        private Transform _antebracoD;
        private Transform _maoE;
        private Transform _maoD;
        private Quaternion _rQuadril;
        private Quaternion _rColuna;
        private Quaternion _rColuna2;
        private Quaternion _rPescoco;
        private Quaternion _rCabeca;
        private Quaternion _rBracoE;
        private Quaternion _rBracoD;
        private Quaternion _rAntebracoE;
        private Quaternion _rAntebracoD;
        private Quaternion _rMaoE;
        private Quaternion _rMaoD;
        private Vector3[] _vertices = System.Array.Empty<Vector3>();

        public string Restaurante => _restaurante;

        public Transform Corpo => _corpo;

        /// <summary>Usado pelo gerador da cidade.</summary>
        public void Configurar(GameObject modelo, string restaurante, Color avental, Color tom, bool bone, float fase)
        {
            _modelo = modelo;
            _restaurante = restaurante;
            _avental = avental;
            _tom = tom;
            _bone = bone;
            _fase = fase;
        }

        private void Awake()
        {
            if (_modelo == null)
            {
                return;
            }

            GameObject body = Instantiate(_modelo, transform);
            body.name = "Corpo";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one * 0.99f;
            _corpo = body.transform;
            var animator = body.GetComponent<Animator>();

            if (animator != null)
            {
                animator.enabled = false;
            }

            foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.updateWhenOffscreen = true;
            }

            Acha(body.transform);
            Tinge(body);
            Enfeita();
        }

        private void Acha(Transform raiz)
        {
            Transform Find(string nome)
            {
                foreach (Transform t in raiz.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == nome || t.name.EndsWith(":" + nome))
                    {
                        return t;
                    }
                }

                return null;
            }

            _quadril = Find("Hips");
            _coluna = Find("Spine1");
            _coluna2 = Find("Spine2");
            _pescoco = Find("Neck");
            _cabeca = Find("Head");
            _bracoE = Find("LeftArm");
            _bracoD = Find("RightArm");
            _antebracoE = Find("LeftForeArm");
            _antebracoD = Find("RightForeArm");
            _maoE = Find("LeftHand");
            _maoD = Find("RightHand");

            Quaternion Ref(Transform t) => t == null ? Quaternion.identity : Quaternion.Inverse(raiz.rotation) * t.rotation;
            _rQuadril = Ref(_quadril);
            _rColuna = Ref(_coluna);
            _rColuna2 = Ref(_coluna2);
            _rPescoco = Ref(_pescoco);
            _rCabeca = Ref(_cabeca);
            _rBracoE = Ref(_bracoE);
            _rBracoD = Ref(_bracoD);
            _rAntebracoE = Ref(_antebracoE);
            _rAntebracoD = Ref(_antebracoD);
            _rMaoE = Ref(_maoE);
            _rMaoD = Ref(_maoD);
        }

        // ---------- Tom, avental e boné ----------

        /// <summary>Material próprio por atendente (o tom multiplica a textura do personagem).</summary>
        private void Tinge(GameObject body)
        {
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.materials;

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && materials[i].HasProperty("_BaseMap") && materials[i].GetTexture("_BaseMap") != null)
                    {
                        materials[i].SetColor("_BaseColor", _tom);
                    }
                }

                renderer.materials = materials;
            }
        }

        /// <summary>Os vértices do corpo na pose de referência, no espaço local do corpo (para medir o tronco).</summary>
        private void MedeCorpo()
        {
            var list = new System.Collections.Generic.List<Vector3>();
            var baked = new Mesh();

            foreach (SkinnedMeshRenderer renderer in _corpo.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (renderer.name.Contains("Hair") || renderer.name.Contains("Eyelash"))
                {
                    continue;
                }

                renderer.BakeMesh(baked);
                Matrix4x4 toLocal = _corpo.worldToLocalMatrix * renderer.transform.localToWorldMatrix;

                foreach (Vector3 v in baked.vertices)
                {
                    list.Add(toLocal.MultiplyPoint3x4(v));
                }
            }

            Destroy(baked);
            _vertices = list.ToArray();
        }

        /// <summary>Largura (meia) e extensão em z do tronco numa faixa de altura local (só perto do centro, sem os braços).</summary>
        private void Perfil(float y, out float meiaLargura, out float frente, out float costas)
        {
            meiaLargura = 0f;
            frente = float.MinValue;
            costas = float.MaxValue;

            foreach (Vector3 v in _vertices)
            {
                if (Mathf.Abs(v.y - y) > 0.03f || Mathf.Abs(v.x) > 0.3f)
                {
                    continue;
                }

                meiaLargura = Mathf.Max(meiaLargura, Mathf.Abs(v.x));
                frente = Mathf.Max(frente, v.z);
                costas = Mathf.Min(costas, v.z);
            }

            if (frente == float.MinValue)
            {
                meiaLargura = 0.17f;
                frente = 0.1f;
                costas = -0.1f;
            }
        }

        private void Enfeita()
        {
            if (_coluna == null || _coluna2 == null || _quadril == null || _pescoco == null)
            {
                return;
            }

            MedeCorpo();
            Material cloth = Material("AtendenteAvental", _avental, 0.12f);
            Material band = Material("AtendenteFaixa", Color.Lerp(_avental, Color.black, 0.35f), 0.12f);
            Transform root = _corpo;
            float hips = root.InverseTransformPoint(_quadril.position).y;
            float chest = root.InverseTransformPoint(_coluna2.position).y;
            float neck = root.InverseTransformPoint(_pescoco.position).y;
            float waist = hips + (chest - hips) * 0.45f;
            float top = chest + (neck - chest) * 0.3f;
            float hem = hips - 0.3f;

            Perfil(chest - 0.06f, out float chestHalf, out float chestFront, out _);
            Perfil(waist, out float waistHalf, out float waistFront, out float waistBack);
            Perfil(hips - 0.1f, out float hipHalf, out float hipFront, out _);
            Perfil(hem + 0.04f, out _, out float legFront, out _);
            float skirtFront = Mathf.Max(waistFront, hipFront, legFront) + 0.012f;
            float bibFront = Mathf.Max(chestFront, waistFront) + 0.012f;

            // Peitilho (da cintura ao peito) e saia (da cintura até a coxa), duas tábuas lisas na frente do corpo.
            float bibWidth = Mathf.Clamp(chestHalf * 2f * 0.66f, 0.2f, 0.34f);
            float skirtWidth = bibWidth * 1.16f;
            Peca(PrimitiveType.Cube, "AventalPeitilho", _coluna, root, new Vector3(0f, (top + waist) * 0.5f, bibFront), Quaternion.identity, new Vector3(bibWidth, top - waist, 0.016f), cloth);
            Peca(PrimitiveType.Cube, "AventalSaia", _coluna, root, new Vector3(0f, (waist + hem) * 0.5f, skirtFront), Quaternion.identity, new Vector3(skirtWidth, waist - hem, 0.016f), cloth);

            // Faixa na cintura em volta do tronco e alças sobre os ombros.
            float depth = waistFront - waistBack + 0.025f;
            Peca(PrimitiveType.Cube, "AventalCintura", _coluna, root, new Vector3(0f, waist, (waistFront + waistBack) * 0.5f), Quaternion.identity, new Vector3(waistHalf * 2f + 0.02f, 0.04f, depth), band);

            for (int side = -1; side <= 1; side += 2)
            {
                var from = new Vector3(side * bibWidth * 0.4f, top, bibFront);
                var to = new Vector3(side * 0.075f, neck - 0.015f, chestFront * 0.4f - 0.02f);
                Alca(_coluna2, root, from, to, band);
                Alca(_coluna2, root, to, new Vector3(side * 0.075f, neck - 0.07f, -0.08f), band);
            }

            if (_bone && _cabeca != null)
            {
                Bone(cloth);
            }
        }

        private static void Alca(Transform bone, Transform root, Vector3 from, Vector3 to, Material material)
        {
            Vector3 direction = to - from;
            float length = direction.magnitude;

            if (length < 1e-4f)
            {
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
            Peca(PrimitiveType.Cube, "AventalAlca", bone, root, (from + to) * 0.5f, rotation, new Vector3(0.026f, 0.01f, length), material);
        }

        /// <summary>Boné: uma cúpula procedural sobre a cabeça do modelo (por cima do cabelo) e a aba.</summary>
        private void Bone(Material cloth)
        {
            Transform top = null;

            foreach (Transform t in _corpo.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.EndsWith("HeadTop_End"))
                {
                    top = t;
                }
            }

            float headLength = top != null ? Vector3.Distance(_cabeca.position, top.position) : 0.2f;
            Vector3 headLocal = _corpo.InverseTransformPoint(_cabeca.position);
            var center = new Vector3(headLocal.x, headLocal.y + headLength * 0.52f, headLocal.z + 0.004f);

            var dome = new GameObject("Bone");
            dome.AddComponent<MeshFilter>().sharedMesh = Domo();
            dome.AddComponent<MeshRenderer>().sharedMaterial = cloth;
            dome.transform.SetParent(_cabeca, true);
            dome.transform.position = _corpo.TransformPoint(center + Vector3.up * 0.026f);
            dome.transform.rotation = _corpo.rotation;
            dome.transform.localScale = Vector3.one;
            Vector3 lossy = dome.transform.lossyScale;
            var size = new Vector3(0.098f, 0.075f, 0.108f);
            dome.transform.localScale = new Vector3(size.x / Mathf.Max(lossy.x, 1e-5f), size.y / Mathf.Max(lossy.y, 1e-5f), size.z / Mathf.Max(lossy.z, 1e-5f));

            Peca(PrimitiveType.Cube, "AbaDoBone", _cabeca, _corpo, center + new Vector3(0f, 0.022f, 0.118f), Quaternion.Euler(-12f, 0f, 0f), new Vector3(0.14f, 0.012f, 0.1f), cloth);
        }

        private static Mesh _domoMesh;

        /// <summary>Meia esfera de raio 1 (a parte de cima), com 20 fatias e 6 anéis.</summary>
        private static Mesh Domo()
        {
            if (_domoMesh != null)
            {
                return _domoMesh;
            }

            const int slices = 20;
            const int rings = 6;
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();

            for (int r = 0; r <= rings; r++)
            {
                float lat = r / (float)rings * Mathf.PI * 0.5f;

                for (int s = 0; s < slices; s++)
                {
                    float lon = s / (float)slices * Mathf.PI * 2f;
                    vertices.Add(new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon)));
                }
            }

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < slices; s++)
                {
                    int a = r * slices + s;
                    int b = r * slices + (s + 1) % slices;
                    int c = (r + 1) * slices + s;
                    int d = (r + 1) * slices + (s + 1) % slices;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            }

            _domoMesh = new Mesh { name = "BoneDomo" };
            _domoMesh.SetVertices(vertices);
            _domoMesh.SetTriangles(triangles, 0);
            _domoMesh.RecalculateNormals();
            _domoMesh.RecalculateBounds();

            return _domoMesh;
        }

        private static Material Material(string nome, Color cor, float suavidade)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard")) { name = nome, color = cor };
            material.SetFloat("_Smoothness", suavidade);

            return material;
        }

        /// <summary>Peça procedural presa a um osso. Posição e rotação no espaço local do corpo; tamanho em metros do mundo (desfaz a escala herdada).</summary>
        private static void Peca(PrimitiveType tipo, string nome, Transform osso, Transform corpo, Vector3 local, Quaternion rotacao, Vector3 tamanho, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(tipo);
            go.name = nome;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.transform.SetParent(osso, true);
            go.transform.position = corpo.TransformPoint(local);
            go.transform.rotation = corpo.rotation * rotacao;
            go.transform.localScale = Vector3.one;
            Vector3 lossy = go.transform.lossyScale;
            go.transform.localScale = new Vector3(tamanho.x / Mathf.Max(lossy.x, 1e-5f), tamanho.y / Mathf.Max(lossy.y, 1e-5f), tamanho.z / Mathf.Max(lossy.z, 1e-5f));
        }

        // ---------- Pose ----------

        private void LateUpdate()
        {
            if (_corpo == null || _quadril == null)
            {
                return;
            }

            float t = Time.time + _fase;
            float breath = Mathf.Sin(t * 1.6f);
            float sway = Mathf.Sin(t * 0.55f);
            Quaternion root = _corpo.rotation;
            Vector3 forward = _corpo.forward;
            Vector3 right = _corpo.right;

            // Pose absoluta em relação à raiz: pai antes de filho (cada osso usa a rotação de referência própria).
            _quadril.rotation = root * Quaternion.AngleAxis(sway * 1.2f, Vector3.forward) * _rQuadril;
            if (_coluna != null) _coluna.rotation = root * Quaternion.AngleAxis(-sway * 1.6f, Vector3.forward) * Quaternion.AngleAxis(2f + breath * 0.8f, Vector3.right) * _rColuna;
            if (_coluna2 != null) _coluna2.rotation = root * Quaternion.AngleAxis(-sway * 1.2f, Vector3.forward) * Quaternion.AngleAxis(breath * 0.8f, Vector3.right) * _rColuna2;
            if (_pescoco != null) _pescoco.rotation = root * Quaternion.AngleAxis(Mathf.Sin(t * 0.37f) * 3f, Vector3.up) * _rPescoco;
            if (_cabeca != null) _cabeca.rotation = root * Quaternion.AngleAxis(Mathf.Sin(t * 0.37f) * 5f, Vector3.up) * Quaternion.AngleAxis(sway * 1.5f, Vector3.forward) * _rCabeca;

            // Braços para baixo: gira em torno do eixo à frente (a T aponta os braços para os lados), com um pouco de cotovelo dobrado.
            float down = 72f + breath * 1.2f;
            Pose(_bracoE, down, _rBracoE, root);
            Pose(_antebracoE, down - 14f, _rAntebracoE, root);
            Pose(_maoE, down - 14f, _rMaoE, root);
            Pose(_bracoD, -down, _rBracoD, root);
            Pose(_antebracoD, -down + 14f, _rAntebracoD, root);
            Pose(_maoD, -down + 14f, _rMaoD, root);
        }

        private static void Pose(Transform bone, float angle, Quaternion reference, Quaternion root)
        {
            if (bone != null)
            {
                bone.rotation = root * Quaternion.AngleAxis(angle, Vector3.forward) * reference;
            }
        }
    }
}
