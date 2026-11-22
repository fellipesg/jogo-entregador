using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// A atendente do balcão de um restaurante: um personagem do Mixamo (prefab de Assets/_Jogo/Personagens) sem capacete, com cabeça,
    /// cabelo ou boné, olhos e avental procedurais na cor do restaurante. Sem Animator: a cada quadro põe os ossos numa pose de pé com os
    /// braços para baixo (a pose de referência é a T) e uma leve oscilação de respiração e de peso.
    /// </summary>
    public sealed class AtendenteRestaurante : MonoBehaviour
    {
        [SerializeField] private GameObject _modelo;
        [SerializeField] private string _restaurante = string.Empty;
        [SerializeField] private Color _avental = Color.white;
        [SerializeField] private Color _pele = new Color(0.62f, 0.44f, 0.33f);
        [SerializeField] private Color _cabelo = new Color(0.08f, 0.05f, 0.04f);
        [SerializeField] private bool _bone = true;
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

        public string Restaurante => _restaurante;

        public Transform Corpo => _corpo;

        /// <summary>Usado pelo gerador da cidade.</summary>
        public void Configurar(GameObject modelo, string restaurante, Color avental, Color pele, Color cabelo, bool bone, float fase)
        {
            _modelo = modelo;
            _restaurante = restaurante;
            _avental = avental;
            _pele = pele;
            _cabelo = cabelo;
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

        // ---------- Cabeça, boné e avental ----------

        private void Enfeita()
        {
            if (_cabeca == null)
            {
                return;
            }

            Transform top = null;

            foreach (Transform t in _corpo.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.EndsWith("HeadTop_End"))
                {
                    top = t;
                }
            }

            float headLength = top != null ? Vector3.Distance(_cabeca.position, top.position) : 0.2f;
            Vector3 up = _corpo.up;
            Vector3 forward = _corpo.forward;
            Vector3 center = _cabeca.position + up * (headLength * 0.52f);

            Material skin = Material("AtendentePele", _pele, 0.25f);
            Material hair = Material("AtendenteCabelo", _cabelo, 0.3f);
            Material cloth = Material("AtendenteAvental", _avental, 0.1f);
            Material dark = Material("AtendenteOlho", new Color(0.04f, 0.03f, 0.03f), 0.5f);

            // Cabeça um pouco mais alta que larga.
            Peca(PrimitiveType.Sphere, "Cabeca", _cabeca, center, new Vector3(0.155f, 0.2f, 0.175f), skin);
            Peca(PrimitiveType.Cylinder, "Pescoco", _cabeca, _cabeca.position + up * (headLength * 0.1f), new Vector3(0.085f, 0.07f, 0.085f), skin);
            Peca(PrimitiveType.Sphere, "Cabelo", _cabeca, center + up * 0.035f - forward * 0.018f, new Vector3(0.168f, 0.18f, 0.182f), hair);

            if (_bone)
            {
                Peca(PrimitiveType.Cylinder, "Bone", _cabeca, center + up * 0.105f, new Vector3(0.172f, 0.022f, 0.172f), cloth);
                Peca(PrimitiveType.Cube, "AbaDoBone", _cabeca, center + up * 0.085f + forward * 0.1f, new Vector3(0.13f, 0.012f, 0.07f), cloth);
            }

            Peca(PrimitiveType.Sphere, "OlhoE", _cabeca, center + forward * 0.078f + _corpo.right * 0.038f + up * 0.012f, Vector3.one * 0.022f, dark);
            Peca(PrimitiveType.Sphere, "OlhoD", _cabeca, center + forward * 0.078f - _corpo.right * 0.038f + up * 0.012f, Vector3.one * 0.022f, dark);

            // Avental: peitilho e cintura sobre o tronco.
            if (_coluna2 != null && _coluna != null)
            {
                Vector3 chest = Vector3.Lerp(_coluna.position, _coluna2.position, 0.5f);
                Peca(PrimitiveType.Cube, "Avental", _coluna, chest - up * 0.14f + forward * 0.105f, new Vector3(0.3f, 0.56f, 0.03f), cloth);
                Peca(PrimitiveType.Cube, "AventalFaixa", _coluna, chest - up * 0.05f + forward * 0.002f, new Vector3(0.36f, 0.05f, 0.24f), cloth);
            }
        }

        private static Material Material(string nome, Color cor, float suavidade)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard")) { name = nome, color = cor };
            material.SetFloat("_Smoothness", suavidade);

            return material;
        }

        /// <summary>Peça procedural presa a um osso, com tamanho em metros do mundo (desfaz a escala herdada).</summary>
        private static void Peca(PrimitiveType tipo, string nome, Transform osso, Vector3 mundo, Vector3 tamanho, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(tipo);
            go.name = nome;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.transform.SetParent(osso, true);
            go.transform.position = mundo;
            go.transform.rotation = osso.root.rotation;
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
