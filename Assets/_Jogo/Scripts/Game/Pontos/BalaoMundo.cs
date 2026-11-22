using TMPro;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Balão de fala curto no mundo ("Salve!"): texto TextMeshPro sobre uma placa escura, vira para a câmera, segue a cabeça de quem fala,
    /// cresce um pouco com a distância (para continuar legível) e some em fade. Um por grupo, reaproveitado.
    /// </summary>
    public sealed class BalaoMundo : MonoBehaviour
    {
        private TextMeshPro _texto;
        private Renderer _placa;
        private MaterialPropertyBlock _bloco;
        private EntregadorNpc _alvo;
        private float _restante;
        private float _duracao;
        private static Material _matPlaca;
        private static Mesh _quad;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public bool Visivel => _restante > 0f;

        public string Texto => _texto != null ? _texto.text : string.Empty;

        public EntregadorNpc Alvo => _alvo;

        private void Awake()
        {
            var go = new GameObject("Texto");
            go.transform.SetParent(transform, false);
            _texto = go.AddComponent<TextMeshPro>();
            _texto.alignment = TextAlignmentOptions.Center;
            _texto.fontSize = 3.2f;
            _texto.fontStyle = FontStyles.Bold;
            _texto.textWrappingMode = TextWrappingModes.NoWrap;
            _texto.rectTransform.sizeDelta = new Vector2(5f, 1f);
            _texto.color = Color.white;
            _texto.sortingOrder = 5;

            var placa = new GameObject("Placa");
            placa.transform.SetParent(transform, false);
            placa.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            placa.AddComponent<MeshFilter>().sharedMesh = Quad();
            _placa = placa.AddComponent<MeshRenderer>();
            _placa.sharedMaterial = MatPlaca();
            _placa.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _placa.receiveShadows = false;
            _bloco = new MaterialPropertyBlock();
            Ocultar();
        }

        private static Mesh Quad()
        {
            if (_quad == null)
            {
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _quad = temp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(temp);
            }

            return _quad;
        }

        private static Material MatPlaca()
        {
            if (_matPlaca == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                _matPlaca = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "BalaoPlaca" };
                _matPlaca.SetFloat("_Surface", 1f);
                _matPlaca.SetFloat("_Blend", 0f);
                _matPlaca.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _matPlaca.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _matPlaca.SetFloat("_ZWrite", 0f);
                _matPlaca.SetOverrideTag("RenderType", "Transparent");
                _matPlaca.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _matPlaca.renderQueue = 3000;
            }

            return _matPlaca;
        }

        /// <summary>Mostra o balão sobre a cabeça do entregador por alguns segundos.</summary>
        public void Mostrar(EntregadorNpc alvo, string texto, float segundos)
        {
            _alvo = alvo;
            _texto.text = texto;
            _texto.ForceMeshUpdate();
            Vector2 tamanho = _texto.GetPreferredValues(texto);
            _placa.transform.localScale = new Vector3(tamanho.x + 0.5f, tamanho.y + 0.25f, 1f);
            _duracao = _restante = Mathf.Max(0.5f, segundos);
            gameObject.SetActive(true);
            Seguir();
        }

        public void Ocultar()
        {
            _restante = 0f;
            _alvo = null;
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            _restante -= Time.deltaTime;

            if (_restante <= 0f || _alvo == null)
            {
                Ocultar();

                return;
            }

            Seguir();
            float alfa = Mathf.Clamp01(_restante / Mathf.Min(0.6f, _duracao));
            _texto.color = new Color(1f, 1f, 1f, alfa);
            _placa.GetPropertyBlock(_bloco);
            _bloco.SetColor(BaseColor, new Color(0.04f, 0.05f, 0.07f, 0.78f * alfa));
            _placa.SetPropertyBlock(_bloco);
        }

        private void Seguir()
        {
            Camera camera = Camera.main;
            transform.position = _alvo.PosicaoDaCabeca + Vector3.up * 0.1f;

            if (camera == null)
            {
                return;
            }

            Vector3 distancia = transform.position - camera.transform.position;
            transform.rotation = Quaternion.LookRotation(distancia.sqrMagnitude > 1e-4f ? camera.transform.forward : Vector3.forward, camera.transform.up);
            float escala = Mathf.Clamp(distancia.magnitude / 9f, 0.7f, 2.4f) * 0.5f;
            transform.localScale = Vector3.one * escala;
        }
    }
}
