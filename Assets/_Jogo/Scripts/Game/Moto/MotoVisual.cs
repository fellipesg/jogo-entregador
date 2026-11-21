using TMPro;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Visual da moto e do piloto: monta a moto procedural (MotoModel), o personagem do Mixamo (PersonagensCatalogo) e o capacete
    /// procedural (RiderModel), e anima sem Animator: rodas giram com a velocidade, garfo e guidão esterçam, o piloto inclina um
    /// pouco mais que a moto, parada a moto tomba alguns graus e o pé esquerdo desce ao chão (RiderRig: tronco inclinado e IK de
    /// dois ossos nos braços e pernas). Lanterna acende ao frear.
    /// A física não é tocada: só lê o estado do ArcadeBikeController.
    /// </summary>
    public sealed class MotoVisual : MonoBehaviour
    {
        /// <summary>Altura da raiz da física abaixo do chão visual (o collider da moto começa 5 cm acima da origem do Rigidbody).</summary>
        public const float AlturaDoChao = 0.05f;

        /// <summary>Altura do osso do quadril (centro da pélvis do Mixamo) acima do ponto Quadril da moto.</summary>
        private const float PilotoAltura = 0.04f;

        /// <summary>Quanto o centro do capacete avança em relação ao meio da cabeça, em fração do comprimento da cabeça.</summary>
        private const float CapaceteFrente = 0.1f;

        [SerializeField] private MotoModelo _modelo = MotoModelo.Titan;
        [SerializeField] private MotoCor _cor = MotoCor.Vermelha;
        [SerializeField] private Personagem _personagem = Personagem.Jefinho;
        [SerializeField] private Color _corDoBau = new Color(0.92f, 0.1f, 0.12f);
        [SerializeField] private string _nomeDoApp = "RANGÔ";
        [SerializeField] private ArcadeBikeController _moto;

        [SerializeField] private Material _matPaleta;
        [SerializeField] private Material _matMetal;
        [SerializeField] private Material _matLuzes;
        [SerializeField] private Material _matBau;

        [SerializeField] private Transform _raizModelo;
        [SerializeField] private Transform _garfo;
        [SerializeField] private Transform _rodaFrente;
        [SerializeField] private Transform _rodaTras;
        [SerializeField] private PersonagensCatalogo _catalogo;
        [SerializeField] private Transform _piloto;
        [SerializeField] private Transform _capacete;
        [SerializeField] private RiderRig _rig = new RiderRig();
        [SerializeField] private Transform _manopolaEsq;
        [SerializeField] private Transform _manopolaDir;
        [SerializeField] private Renderer _farolRenderer;
        [SerializeField] private Renderer _lanternaRenderer;
        [SerializeField] private Renderer _bauRenderer;
        [SerializeField] private float _raioRodaFrente = 0.31f;
        [SerializeField] private float _raioRodaTras = 0.31f;
        [SerializeField] private Vector3 _eixoDirecao = Vector3.up;
        [SerializeField] private Vector3 _pedaleiraEsq;
        [SerializeField] private Vector3 _pedaleiraDir;
        [SerializeField] private int _triangulos;
        [SerializeField] private int _triangulosPiloto;
        [SerializeField] private float _inclinacaoDoTronco = 14f;
        [SerializeField] private float _inclinacaoDaCabeca;

        [Header("Animação")]
        [SerializeField] private float _esterceMaximo = 24f;
        [SerializeField] private float _tombamentoParada = 5f;
        [SerializeField] private float _inclinacaoExtraDoPiloto = 0.35f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private float _anguloRodaFrente;
        private float _anguloRodaTras;
        private float _esterce;
        private float _parado = 1f;
        private float _pesoFarol = -1f;
        private float _pesoLanterna = -1f;
        private MaterialPropertyBlock _bloco;

        public MotoModelo Modelo => _modelo;
        public MotoCor Cor => _cor;
        public Personagem Personagem => _personagem;
        public int Triangulos => _triangulos;

        /// <summary>Triângulos só do piloto (personagem do Mixamo, já decimado, mais o capacete).</summary>
        public int TriangulosPiloto => _triangulosPiloto;

        public RiderRig Rig => _rig;

        /// <summary>Liga o visual à moto de física (lê velocidade, direção, inclinação e freio).</summary>
        public void Ligar(ArcadeBikeController moto)
        {
            _moto = moto;
        }

        /// <summary>Entrega o catálogo dos modelos dos personagens (BikeFactory; a troca em jogo usa o mesmo).</summary>
        public void DefinirCatalogo(PersonagensCatalogo catalogo)
        {
            _catalogo = catalogo;
        }

        /// <summary>Entrega os materiais-base (usados como molde nas remontagens em jogo).</summary>
        public void DefinirMateriais(Material paleta, Material metal, Material luzes, Material bau)
        {
            _matPaleta = paleta;
            _matMetal = metal;
            _matLuzes = luzes;
            _matBau = bau;
        }

        public void CopiarMateriaisDe(MotoVisual outro)
        {
            DefinirMateriais(outro._matPaleta, outro._matMetal, outro._matLuzes, outro._matBau);
            _catalogo = outro._catalogo;
        }

        /// <summary>Troca moto, cor e personagem e remonta as malhas. Para a tela de seleção (rodada futura).</summary>
        public void Configurar(MotoModelo modelo, MotoCor cor, Personagem personagem)
        {
            _modelo = modelo;
            _cor = cor;
            _personagem = personagem;
            Montar();
        }

        public void DefinirPersonagem(Personagem personagem) => Configurar(_modelo, _cor, personagem);

        /// <summary>Cor do baú e nome do app (cada app tem a sua cor).</summary>
        public void DefinirApp(Color cor, string nome)
        {
            _corDoBau = cor;
            _nomeDoApp = nome;

            if (_bauRenderer != null)
            {
                _bloco ??= new MaterialPropertyBlock();
                _bauRenderer.GetPropertyBlock(_bloco);
                _bloco.SetColor(BaseColor, cor);
                _bauRenderer.SetPropertyBlock(_bloco);

                foreach (TextMeshPro text in _raizModelo.GetComponentsInChildren<TextMeshPro>())
                {
                    if (!text.name.StartsWith("Logo"))
                    {
                        continue;
                    }

                    text.text = nome;
                    text.color = Color.Lerp(cor, Color.black, 0.55f);
                }
            }
        }

        private void Awake()
        {
            _parado = 1f;
        }

        private void Start()
        {
            // O MaterialPropertyBlock não é serializado na cena: reaplica a cor do baú e as luzes.
            if (_raizModelo != null)
            {
                _pesoFarol = _pesoLanterna = -1f;
                DefinirApp(_corDoBau, _nomeDoApp);
            }
        }

        // ---------- Montagem ----------

        /// <summary>(Re)gera todas as malhas e a hierarquia. Chamado pelo editor (BikeFactory) e pela remontagem em jogo.</summary>
        public void Montar()
        {
            if (_raizModelo != null)
            {
                Apagar(_raizModelo.gameObject);
            }

            var pal = new MotoPalette();
            MotoParts m = MotoModel.Build(_modelo, _cor, pal);
            PersonagemConfig c = MotoCatalogo.Config(_personagem);
            _inclinacaoDoTronco = c.Inclinacao;
            _inclinacaoDaCabeca = c.CabecaFrente * 160f;

            _raizModelo = new GameObject("Modelo").transform;
            _raizModelo.SetParent(transform, false);
            _raizModelo.localPosition = new Vector3(0f, AlturaDoChao, 0f);
            _raioRodaFrente = m.RaioFrente;
            _raioRodaTras = m.RaioTras;
            _eixoDirecao = m.EixoDirecao;
            _pedaleiraEsq = m.PedaleiraEsq;
            _pedaleiraDir = m.PedaleiraDir;

            // Piloto (personagem do Mixamo + capacete), antes dos materiais porque o capacete usa a mesma paleta da moto.
            MontarPiloto(m, c, pal);

            Texture2D texture = pal.BuildTexture();
            Material paleta = _matPaleta;
            Material metal = _matMetal;

            if (Application.isPlaying)
            {
                paleta = new Material(_matPaleta);
                metal = new Material(_matMetal);
            }

            paleta.SetTexture("_BaseMap", texture);
            metal.SetTexture("_BaseMap", texture);
            Material[] dois = { paleta, metal };

            if (_capacete != null)
            {
                _capacete.GetComponent<MeshRenderer>().sharedMaterials = new[] { paleta };
            }

            Parte("Quadro", _raizModelo, m.Quadro, dois, Vector3.zero);
            _rodaTras = Parte("RodaTras", _raizModelo, m.RodaTras, dois, m.EixoTras).transform;
            _garfo = Parte("Garfo", _raizModelo, m.Garfo, dois, m.EixoFrente).transform;
            _rodaFrente = Parte("RodaFrente", _garfo, m.RodaFrente, dois, Vector3.zero).transform;
            _farolRenderer = Parte("FarolLente", _garfo, m.FarolLente, new[] { _matLuzes }, Vector3.zero).GetComponent<Renderer>();
            _lanternaRenderer = Parte("LanternaLente", _raizModelo, m.LanternaLente, new[] { _matLuzes }, Vector3.zero).GetComponent<Renderer>();
            _bauRenderer = Parte("Bau", _raizModelo, m.Bau, new[] { _matBau }, Vector3.zero).GetComponent<Renderer>();

            _manopolaEsq = Vazio("ManopolaEsq", _garfo, m.ManopolaEsq);
            _manopolaDir = Vazio("ManopolaDir", _garfo, m.ManopolaDir);

            // Logo do baú e placa em TextMeshPro.
            float half = m.BauTamanho.x * 0.5f;
            float badgeY = m.BauCentro.y - 0.03f;
            Texto("LogoEsq", _raizModelo, new Vector3(-half - 0.0045f, badgeY, m.BauCentro.z), Vector3.left, 0.27f, 0.13f, _nomeDoApp);
            Texto("LogoDir", _raizModelo, new Vector3(half + 0.0045f, badgeY, m.BauCentro.z), Vector3.right, 0.27f, 0.13f, _nomeDoApp);
            Texto("LogoTras", _raizModelo, new Vector3(0f, badgeY, m.BauCentro.z - half - 0.0045f), Vector3.back, 0.27f, 0.13f, _nomeDoApp);
            Texto("Placa", _raizModelo, m.PlacaCentro + new Vector3(0f, -0.004f, -0.0015f), Vector3.back, 0.37f, 0.095f, "BRA2E19");

            _pesoFarol = _pesoLanterna = -1f;
            DefinirApp(_corDoBau, _nomeDoApp);
            Posicionar(0f);
        }

        /// <summary>Instancia o personagem pelo pivô no quadril, monta o esqueleto (RiderRig) e prende o capacete à cabeça.</summary>
        private void MontarPiloto(MotoParts m, PersonagemConfig c, MotoPalette pal)
        {
            _piloto = Vazio("Piloto", _raizModelo, m.Quadril + new Vector3(0f, PilotoAltura, c.QuadrilZ));
            _capacete = null;
            _rig = new RiderRig();
            _triangulosPiloto = 0;
            GameObject prefab = _catalogo != null ? _catalogo.Modelo(_personagem) : null;

            if (prefab == null)
            {
                _triangulos = m.Triangulos;

                return;
            }

            GameObject body = Instantiate(prefab, _piloto);
            body.name = "Corpo";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one;
            var animator = body.GetComponent<Animator>();

            if (animator != null)
            {
                animator.enabled = false;
            }

            _rig = RiderRig.Montar(body.transform);
            Vector3 hipsLocal = body.transform.InverseTransformPoint(_rig.Pegar(Osso.Hips).position);
            body.transform.localScale = Vector3.one * c.Escala;
            body.transform.localPosition = -hipsLocal * c.Escala;
            int triangles = 0;

            foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                triangles += renderer.sharedMesh.triangles.Length / 3;
                renderer.updateWhenOffscreen = true;
            }

            // Capacete: no tamanho da cabeça do personagem, preso ao osso da cabeça (gira junto, sempre de frente).
            Transform head = _rig.Cabeca;
            Transform top = _rig.Pegar(Osso.HeadTop);
            float headLength = Vector3.Distance(head.position, top.position);
            float helmetScale = Mathf.Clamp(headLength / 0.25f, 0.78f, 1.1f);
            Mesh helmet = RiderModel.BuildHelmet(c, pal, helmetScale, out int helmetTriangles);
            _capacete = Vazio("Capacete", _raizModelo, Vector3.zero);
            _capacete.position = Vector3.Lerp(head.position, top.position, 0.5f) + body.transform.forward * (headLength * CapaceteFrente);
            _capacete.rotation = body.transform.rotation;
            _capacete.gameObject.AddComponent<MeshFilter>().sharedMesh = helmet;
            _capacete.gameObject.AddComponent<MeshRenderer>();
            _capacete.SetParent(head, true);

            // O capacete herda a escala do corpo pelo osso; desfaz para ficar no tamanho calculado.
            _capacete.localScale = Vector3.one;
            _capacete.localScale = new Vector3(1f / _capacete.lossyScale.x, 1f / _capacete.lossyScale.y, 1f / _capacete.lossyScale.z);
            _triangulosPiloto = triangles + helmetTriangles;
            _triangulos = m.Triangulos + _triangulosPiloto;
        }

        private static void Apagar(GameObject go)
        {
            if (Application.isPlaying)
            {
                Destroy(go);
                go.SetActive(false);
            }
            else
            {
                DestroyImmediate(go);
            }
        }

        private static GameObject Parte(string nome, Transform pai, Mesh mesh, Material[] materiais, Vector3 posicao)
        {
            var go = new GameObject(nome);
            go.transform.SetParent(pai, false);
            go.transform.localPosition = posicao;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materiais;

            return go;
        }

        private static Transform Vazio(string nome, Transform pai, Vector3 posicao)
        {
            var go = new GameObject(nome);
            go.transform.SetParent(pai, false);
            go.transform.localPosition = posicao;

            return go.transform;
        }

        private static void Texto(string nome, Transform pai, Vector3 posicaoNaRaiz, Vector3 normalExterna, float largura, float altura, string texto)
        {
            var go = new GameObject(nome);
            go.transform.SetParent(pai, false);
            go.transform.localPosition = posicaoNaRaiz;
            go.transform.localRotation = Quaternion.LookRotation(-normalExterna, Vector3.up);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = new Vector2(largura, altura);
            tmp.text = texto;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.3f;
            tmp.fontSizeMax = 4f;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = new Color(0.25f, 0.03f, 0.03f);
            tmp.fontSize = 2f;
        }

        // ---------- Animação ----------

        private void LateUpdate()
        {
            if (_raizModelo == null)
            {
                return;
            }

            Posicionar(Time.deltaTime);
        }

        /// <summary>Atualiza o visual (também chamado com dt = 0 logo depois de montar, e por testes).</summary>
        public void Posicionar(float dt)
        {
            float speed = _moto != null ? _moto.ForwardSpeed : 0f;
            float topSpeed = _moto != null ? _moto.TopSpeed : 13f;
            float steer = _moto != null ? _moto.SteerInput : 0f;
            float brake = _moto != null ? _moto.BrakeInput : 0f;
            float lean = _moto != null ? _moto.Lean : 0f;
            float speedRatio = Mathf.Clamp01(Mathf.Abs(speed) / topSpeed);

            // Rodas.
            _anguloRodaFrente = Mathf.Repeat(_anguloRodaFrente + speed / _raioRodaFrente * Mathf.Rad2Deg * dt, 360f);
            _anguloRodaTras = Mathf.Repeat(_anguloRodaTras + speed / _raioRodaTras * Mathf.Rad2Deg * dt, 360f);
            _rodaFrente.localRotation = Quaternion.Euler(_anguloRodaFrente, 0f, 0f);
            _rodaTras.localRotation = Quaternion.Euler(_anguloRodaTras, 0f, 0f);

            // Direção: gira em volta do eixo inclinado do garfo.
            float targetSteer = steer * _esterceMaximo * (1f - 0.55f * speedRatio);
            _esterce = dt <= 0f ? targetSteer : Mathf.Lerp(_esterce, targetSteer, 1f - Mathf.Exp(-12f * dt));
            _garfo.localRotation = Quaternion.AngleAxis(_esterce, _eixoDirecao);

            // Parada: tomba para a esquerda (perna esquerda no chão).
            float targetStop = Mathf.Abs(speed) < 0.6f ? 1f : 0f;
            _parado = dt <= 0f ? targetStop : Mathf.MoveTowards(_parado, targetStop, dt * 2.5f);
            float stopEase = _parado * _parado * (3f - 2f * _parado);
            _raizModelo.localRotation = Quaternion.Euler(0f, 0f, _tombamentoParada * stopEase);

            // O piloto inclina um pouco mais que a moto.
            _piloto.localRotation = Quaternion.Euler(0f, 0f, lean * _inclinacaoExtraDoPiloto);

            // Luzes: farol sempre aceso, lanterna mais forte ao frear.
            float headOn = 1f;
            float tailOn = brake > 0.05f ? 1f : 0f;
            AtualizarLuz(_farolRenderer, ref _pesoFarol, headOn, new Color(1f, 0.96f, 0.82f), new Color(1f, 0.93f, 0.7f) * 2.2f, new Color(1f, 0.96f, 0.82f));
            AtualizarLuz(_lanternaRenderer, ref _pesoLanterna, tailOn, new Color(0.45f, 0.02f, 0.02f), new Color(1f, 0.05f, 0.03f) * 3.5f, new Color(0.55f, 0.02f, 0.02f), new Color(1f, 0.1f, 0.05f) * 0.5f);

            // Braços e pernas (IK no esqueleto do personagem).
            Transform root = _raizModelo;
            Vector3 groundL = root.TransformPoint(new Vector3(_pedaleiraEsq.x - 0.1f, 0f, _pedaleiraEsq.z + 0.02f));
            groundL.y = root.position.y;
            _rig.Posicionar(
                root.rotation,
                _inclinacaoDoTronco,
                _inclinacaoDaCabeca,
                _manopolaEsq.position,
                _manopolaDir.position,
                root.TransformPoint(_pedaleiraEsq),
                root.TransformPoint(_pedaleiraDir),
                groundL,
                stopEase);
        }

        private void AtualizarLuz(Renderer renderer, ref float cache, float on, Color baseOff, Color emissionOn, Color baseOn, Color? emissionOff = null)
        {
            if (renderer == null || Mathf.Approximately(cache, on))
            {
                return;
            }

            cache = on;
            _bloco ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_bloco);
            _bloco.SetColor(BaseColor, on > 0.5f ? baseOn : baseOff);
            _bloco.SetColor(EmissionColor, on > 0.5f ? emissionOn : (emissionOff ?? Color.black));
            renderer.SetPropertyBlock(_bloco);
        }

        /// <summary>Cria uma moto só visual (sem física) para vitrines e capturas; copia os materiais de uma moto existente.</summary>
        public static MotoVisual CriarPrevia(MotoVisual molde, Vector3 posicao, float yaw, MotoModelo modelo, MotoCor cor, Personagem personagem)
        {
            var go = new GameObject("MotoPrevia_" + personagem + "_" + modelo);
            go.transform.SetPositionAndRotation(posicao, Quaternion.Euler(0f, yaw, 0f));
            var visual = go.AddComponent<MotoVisual>();
            visual.CopiarMateriaisDe(molde);
            visual.Configurar(modelo, cor, personagem);

            return visual;
        }
    }
}
