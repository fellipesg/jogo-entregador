using TMPro;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Visual da moto e do piloto: monta as malhas procedurais (MotoModel e RiderModel) e anima sem Animator:
    /// rodas giram com a velocidade, garfo e guidão esterçam, o piloto inclina um pouco mais que a moto, parada a moto
    /// tomba alguns graus e o pé esquerdo desce ao chão (braços e pernas por IK de dois ossos). Lanterna acende ao frear.
    /// A física não é tocada: só lê o estado do ArcadeBikeController.
    /// </summary>
    public sealed class MotoVisual : MonoBehaviour
    {
        /// <summary>Altura da raiz da física abaixo do chão visual (o collider da moto começa 5 cm acima da origem do Rigidbody).</summary>
        public const float AlturaDoChao = 0.05f;

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
        [SerializeField] private Transform _piloto;
        [SerializeField] private Transform _manopolaEsq;
        [SerializeField] private Transform _manopolaDir;
        [SerializeField] private Transform[] _braco = new Transform[4];
        [SerializeField] private Transform[] _perna = new Transform[4];
        [SerializeField] private Transform[] _pes = new Transform[2];
        [SerializeField] private Renderer _farolRenderer;
        [SerializeField] private Renderer _lanternaRenderer;
        [SerializeField] private Renderer _bauRenderer;
        [SerializeField] private float _raioRodaFrente = 0.31f;
        [SerializeField] private float _raioRodaTras = 0.31f;
        [SerializeField] private Vector3 _eixoDirecao = Vector3.up;
        [SerializeField] private Vector3 _pedaleiraEsq;
        [SerializeField] private Vector3 _pedaleiraDir;
        [SerializeField] private float[] _comprimentos = new float[6];
        [SerializeField] private int _triangulos;

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

        /// <summary>Liga o visual à moto de física (lê velocidade, direção, inclinação e freio).</summary>
        public void Ligar(ArcadeBikeController moto)
        {
            _moto = moto;
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
            RiderParts r = RiderModel.Build(c, pal);
            _triangulos = m.Triangulos + r.Triangulos;

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
            Material[] um = { paleta };

            _raizModelo = new GameObject("Modelo").transform;
            _raizModelo.SetParent(transform, false);
            _raizModelo.localPosition = new Vector3(0f, AlturaDoChao, 0f);
            _raioRodaFrente = m.RaioFrente;
            _raioRodaTras = m.RaioTras;
            _eixoDirecao = m.EixoDirecao;
            _pedaleiraEsq = m.PedaleiraEsq;
            _pedaleiraDir = m.PedaleiraDir;

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

            // Piloto.
            _piloto = Parte("Piloto", _raizModelo, r.Tronco, um, m.Quadril + new Vector3(0f, 0f, c.QuadrilZ)).transform;
            _comprimentos = new[] { r.CompBracoSuperior, r.CompAntebraco, r.CompCoxa, r.CompCanela, r.CompLuva, r.BolaDoPe };
            _braco = new Transform[4];
            _perna = new Transform[4];
            _pes = new Transform[2];

            for (int side = 0; side < 2; side++)
            {
                string s = side == 0 ? "Esq" : "Dir";
                _braco[side * 2] = Parte("BracoSup" + s, _raizModelo, r.BracoSuperior, um, Vector3.zero).transform;
                _braco[side * 2 + 1] = Parte("Antebraco" + s, _raizModelo, r.Antebraco, um, Vector3.zero).transform;
                _perna[side * 2] = Parte("Coxa" + s, _raizModelo, r.Coxa, um, Vector3.zero).transform;
                _perna[side * 2 + 1] = Parte("Canela" + s, _raizModelo, r.Canela, um, Vector3.zero).transform;
                _pes[side] = Parte("Tenis" + s, _raizModelo, r.Tenis, um, Vector3.zero).transform;
            }

            _ombroEsq = r.OmbroEsq;
            _ombroDir = r.OmbroDir;
            _quadrilEsq = r.QuadrilEsq;
            _quadrilDir = r.QuadrilDir;
            _pesoFarol = _pesoLanterna = -1f;
            DefinirApp(_corDoBau, _nomeDoApp);
            Posicionar(0f);
        }

        [SerializeField] private Vector3 _ombroEsq;
        [SerializeField] private Vector3 _ombroDir;
        [SerializeField] private Vector3 _quadrilEsq;
        [SerializeField] private Vector3 _quadrilDir;

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

            // Braços e pernas.
            Transform root = _raizModelo;
            Transform torso = _piloto;
            float la = _comprimentos[0];
            float lb = _comprimentos[1];
            float lt = _comprimentos[2];
            float ls = _comprimentos[3];
            float lg = _comprimentos[4];
            float ball = _comprimentos[5];

            Vector3 gripL = _manopolaEsq.position;
            Vector3 gripR = _manopolaDir.position;

            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                Vector3 shoulder = torso.TransformPoint(side == 0 ? _ombroEsq : _ombroDir);
                Vector3 grip = side == 0 ? gripL : gripR;
                Vector3 dirToHand = (grip - shoulder).normalized;
                Vector3 wrist = grip - dirToHand * (lg * 0.55f);
                Vector3 pole = root.TransformDirection(new Vector3(sx * 0.7f, -0.7f, -0.45f));
                TwoBone(_braco[side * 2], _braco[side * 2 + 1], shoulder, wrist, la, lb, pole);

                Vector3 hip = torso.TransformPoint(side == 0 ? _quadrilEsq : _quadrilDir);
                Vector3 peg = root.TransformPoint(side == 0 ? _pedaleiraEsq : _pedaleiraDir);
                Vector3 ankle = peg + root.TransformDirection(new Vector3(0f, 0.095f, -ball));
                Quaternion footRot = root.rotation * Quaternion.Euler(10f, 0f, 0f);

                if (side == 0)
                {
                    // Pé esquerdo no chão quando parado.
                    Vector3 groundXz = root.position + root.TransformDirection(new Vector3(_pedaleiraEsq.x - 0.1f, 0f, _pedaleiraEsq.z + 0.02f - ball));
                    Vector3 ground = new Vector3(groundXz.x, root.position.y + 0.085f + 0.004f, groundXz.z);
                    ankle = Vector3.Lerp(ankle, ground, stopEase);
                    float yaw = root.eulerAngles.y;
                    footRot = Quaternion.Slerp(footRot, Quaternion.Euler(0f, yaw, 0f), stopEase);
                }

                Vector3 legPole = root.TransformDirection(new Vector3(sx * 0.3f, 0.35f, 1f));
                Vector3 end = TwoBone(_perna[side * 2], _perna[side * 2 + 1], hip, ankle, lt, ls, legPole);
                _pes[side].SetPositionAndRotation(end, footRot);
            }
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

        /// <summary>IK analítico de dois ossos. As malhas apontam em +Z. Devolve o ponto alcançado pela ponta.</summary>
        private static Vector3 TwoBone(Transform upper, Transform lower, Vector3 root, Vector3 target, float a, float b, Vector3 pole)
        {
            Vector3 d = target - root;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(a - b) + 0.01f, (a + b) * 0.998f);
            Vector3 dir = d.sqrMagnitude < 1e-8f ? Vector3.down : d.normalized;
            float x = (a * a - b * b + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
            Vector3 perp = pole - dir * Vector3.Dot(pole, dir);
            perp = perp.sqrMagnitude < 1e-6f ? Vector3.up : perp.normalized;
            Vector3 elbow = root + dir * x + perp * h;
            Vector3 end = root + dir * dist;
            upper.SetPositionAndRotation(root, Quaternion.LookRotation(elbow - root, perp));
            lower.SetPositionAndRotation(elbow, Quaternion.LookRotation(end - elbow, perp));

            return end;
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
