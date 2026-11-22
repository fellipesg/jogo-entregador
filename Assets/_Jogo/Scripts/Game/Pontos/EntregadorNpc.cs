using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Como um entregador do ponto é: personagem, tom, moto e cores do app. Sorteado pelo grupo.</summary>
    public struct EntregadorEspec
    {
        public string Nome;
        public Personagem Personagem;
        public Color Tom;
        public MotoModelo Modelo;
        public MotoCor CorMoto;
        public Color CorApp;
        public string NomeApp;

        /// <summary>Sentado na moto (senão está em pé ao lado dela).</summary>
        public bool Sentado;

        public bool CapaceteNaMao;
        public float Fase;
    }

    /// <summary>
    /// Um entregador parado num ponto: a moto estacionada (MotoVisual sem física, tombada no pezinho) e o piloto, que está sentado nela
    /// (o mesmo piloto da moto do jogador, com capacete) ou em pé ao lado (personagem inteiro do Mixamo, com mochila de entrega e,
    /// às vezes, o capacete na mão). Sem Animator: os ossos recebem uma pose por código (respirar, olhar o celular, gesticular,
    /// virar a cabeça para quem fala). Também sabe sair rodando por uma rota (sai de cena) e chegar por uma rota (estaciona).
    /// </summary>
    public sealed class EntregadorNpc : MonoBehaviour
    {
        private const float AlturaDaRaiz = -MotoVisual.AlturaDoChao;
        private const float Tombamento = 9f;
        private const float Velocidade = 6.5f;

        private enum Postura
        {
            Livre,
            Celular,
            Gesto,
            Capacete,
        }

        private static Mesh _cubo;
        private static Material _matPeca;
        private static readonly Dictionary<Personagem, KeyValuePair<Mesh, Material>> Capacetes = new Dictionary<Personagem, KeyValuePair<Mesh, Material>>();
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private EntregadorEspec _espec;
        private MotoVisual _moto;
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
        private Quaternion _cabecaSentado;
        private BoxCollider _caixa;
        private MaterialPropertyBlock _bloco;

        private Postura _postura;
        private float _tempoPostura;
        private float _t;
        private float _yawCabeca;
        private float _yawAlvo;
        private float _pitchCabeca;
        private float _bracoDown = 72f;
        private float _bracoPitch;
        private float _bracoBend = 12f;
        private float _libera;

        private List<Vector3> _rota;
        private int _passo;
        private float _velocidadeAtual;
        private bool _chegando;
        private float _parar;

        public string Nome => _espec.Nome;

        public bool Sentado => _espec.Sentado;

        public EntregadorEspec Espec => _espec;

        public MotoVisual Moto => _moto;

        public Transform Corpo => _corpo;

        public bool EmRota { get; private set; }

        /// <summary>Está chegando (rota de entrada).</summary>
        public bool Chegando => _chegando;

        /// <summary>Terminou a rota de saída (o grupo apaga o entregador).</summary>
        public bool Saiu { get; private set; }

        /// <summary>Altura aproximada da cabeça (para o balão).</summary>
        public Vector3 PosicaoDaCabeca
        {
            get
            {
                Transform head = _espec.Sentado ? (_moto != null ? _moto.Rig.Cabeca : null) : _cabeca;

                return head != null ? head.position + Vector3.up * 0.28f : transform.position + Vector3.up * 1.9f;
            }
        }

        public bool TemEsqueleto => _espec.Sentado ? _moto != null && _moto.Rig != null && _moto.Rig.Valido : _cabeca != null;

        /// <summary>Monta a moto e o piloto. Posição e giro da raiz (a moto) já devem estar definidos.</summary>
        public void Montar(PersonagensCatalogo catalogo, MotoVisual molde, EntregadorEspec espec)
        {
            _espec = espec;
            _t = espec.Fase;
            _postura = espec.CapaceteNaMao ? Postura.Capacete : Postura.Livre;
            _tempoPostura = 2f + Mathf.Abs(Mathf.Sin(espec.Fase * 12.9898f)) * 5f;

            var motoObj = new GameObject("Moto");
            motoObj.transform.SetParent(transform, false);
            _moto = motoObj.AddComponent<MotoVisual>();
            _moto.CopiarMateriaisDe(molde);

            if (!espec.Sentado)
            {
                _moto.DefinirCatalogo(null);
            }

            _moto.Configurar(espec.Modelo, espec.CorMoto, espec.Personagem);
            _moto.DefinirApp(espec.CorApp, espec.NomeApp);
            _moto.DefinirTombamento(Tombamento);
            _moto.Posicionar(0f);

            _caixa = gameObject.AddComponent<BoxCollider>();
            _caixa.center = new Vector3(0f, 0.6f, 0f);
            _caixa.size = new Vector3(0.7f, 1.1f, 2f);

            if (espec.Sentado)
            {
                // Dá três quadros para a pose de parada assentar; depois desliga o MotoVisual (some o custo por quadro).
                _parar = 0.9f;
                Tingir(_moto.gameObject);
                Transform head = _moto.Rig.Cabeca;

                if (head != null)
                {
                    _cabecaSentado = head.rotation;
                }
            }
            else
            {
                _moto.enabled = false;
                MontarEmPe(catalogo);
            }
        }

        /// <summary>Posiciona o piloto em pé (mundo) e vira para o yaw.</summary>
        public void PosicionarCorpo(Vector3 posicao, float yaw)
        {
            if (_corpo != null)
            {
                _corpo.SetPositionAndRotation(posicao, Quaternion.Euler(0f, yaw, 0f));
            }
        }

        // ---------- Montagem do piloto em pé ----------

        private void MontarEmPe(PersonagensCatalogo catalogo)
        {
            GameObject prefab = catalogo != null ? catalogo.Atendente(_espec.Personagem) : null;

            if (prefab == null)
            {
                return;
            }

            GameObject body = Instantiate(prefab);
            body.name = "Corpo_" + _espec.Nome;
            body.transform.SetParent(transform.parent, true);
            body.transform.localScale = Vector3.one * (0.97f + Mathf.Abs(Mathf.Sin(_espec.Fase * 3.1f)) * 0.08f);
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

            var capsule = body.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.radius = 0.28f;
            capsule.height = 1.75f;

            Acha(body.transform);
            Tingir(body);
            Mochila();

            if (_espec.CapaceteNaMao)
            {
                CapaceteNaMaoD();
            }
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

        /// <summary>Tom do personagem sem criar material: um MaterialPropertyBlock por renderer multiplica a cor.</summary>
        private void Tingir(GameObject alvo)
        {
            _bloco ??= new MaterialPropertyBlock();

            foreach (SkinnedMeshRenderer renderer in alvo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.GetPropertyBlock(_bloco);
                _bloco.SetColor(BaseColor, _espec.Tom);
                renderer.SetPropertyBlock(_bloco);
            }
        }

        private static Mesh Cubo()
        {
            if (_cubo == null)
            {
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cubo = temp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(temp);
            }

            return _cubo;
        }

        private static Material MatPeca()
        {
            if (_matPeca == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                _matPeca = new Material(shader != null ? shader : Shader.Find("Standard")) { name = "EntregadorPeca" };
                _matPeca.SetFloat("_Smoothness", 0.25f);
            }

            return _matPeca;
        }

        private void Peca(string nome, Transform osso, Vector3 local, Vector3 tamanho, Color cor)
        {
            var go = new GameObject(nome);
            go.AddComponent<MeshFilter>().sharedMesh = Cubo();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MatPeca();
            _bloco ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_bloco);
            _bloco.SetColor(BaseColor, cor);
            renderer.SetPropertyBlock(_bloco);
            go.transform.SetParent(osso, true);
            go.transform.position = _corpo.TransformPoint(local);
            go.transform.rotation = _corpo.rotation;
            go.transform.localScale = Vector3.one;
            Vector3 lossy = go.transform.lossyScale;
            go.transform.localScale = new Vector3(tamanho.x / Mathf.Max(lossy.x, 1e-5f), tamanho.y / Mathf.Max(lossy.y, 1e-5f), tamanho.z / Mathf.Max(lossy.z, 1e-5f));
        }

        /// <summary>Mochila de entrega nas costas, na cor do app, com a tampa mais escura.</summary>
        private void Mochila()
        {
            if (_coluna2 == null)
            {
                return;
            }

            float chest = _corpo.InverseTransformPoint(_coluna2.position).y;
            Peca("MochilaCaixa", _coluna2, new Vector3(0f, chest - 0.02f, -0.2f), new Vector3(0.4f, 0.42f, 0.26f), _espec.CorApp);
            Peca("MochilaTampa", _coluna2, new Vector3(0f, chest + 0.2f, -0.2f), new Vector3(0.42f, 0.05f, 0.28f), Color.Lerp(_espec.CorApp, Color.black, 0.45f));
            Peca("MochilaFaixa", _coluna2, new Vector3(0f, chest + 0.02f, -0.075f), new Vector3(0.36f, 0.05f, 0.05f), Color.Lerp(_espec.CorApp, Color.black, 0.6f));
        }

        private void CapaceteNaMaoD()
        {
            if (_maoD == null)
            {
                return;
            }

            if (!Capacetes.TryGetValue(_espec.Personagem, out KeyValuePair<Mesh, Material> par) || par.Key == null)
            {
                var pal = new MotoPalette();
                Mesh mesh = RiderModel.BuildHelmet(MotoCatalogo.Config(_espec.Personagem), pal, 0.95f, out _);
                var material = new Material(_moto.MatPaleta) { name = "CapaceteNaMao" };
                material.SetTexture("_BaseMap", pal.BuildTexture());
                par = new KeyValuePair<Mesh, Material>(mesh, material);
                Capacetes[_espec.Personagem] = par;
            }

            var go = new GameObject("CapaceteNaMao");
            go.AddComponent<MeshFilter>().sharedMesh = par.Key;
            go.AddComponent<MeshRenderer>().sharedMaterial = par.Value;
            go.transform.SetParent(_maoD, true);
            go.transform.position = _maoD.position + Vector3.down * 0.13f + _corpo.forward * 0.02f;
            go.transform.rotation = _corpo.rotation;
            go.transform.localScale = Vector3.one;
            Vector3 lossy = go.transform.lossyScale;
            go.transform.localScale = new Vector3(1f / lossy.x, 1f / lossy.y, 1f / lossy.z);
        }

        // ---------- Vida ----------

        /// <summary>Anima o piloto (em pé: pose dos ossos; sentado: só a cabeça). yawAlvo é o giro (graus, relativo ao corpo) até quem ele olha.</summary>
        public void Viver(float dt, bool perto, float olharYaw, bool olhando, bool conversando)
        {
            if (EmRota)
            {
                SeguirRota(dt);

                return;
            }

            if (_parar > 0f)
            {
                _parar -= dt;

                if (_parar <= 0f && _moto != null)
                {
                    _moto.Posicionar(0f);
                    _moto.enabled = false;
                    Transform head = _moto.Rig.Cabeca;

                    if (head != null)
                    {
                        _cabecaSentado = head.rotation;
                    }
                }

                return;
            }

            if (!perto)
            {
                return;
            }

            _t += dt;
            _yawAlvo = olhando ? Mathf.Clamp(olharYaw, -75f, 75f) : Mathf.Sin(_t * 0.31f + _espec.Fase) * 14f;
            _yawCabeca = Mathf.Lerp(_yawCabeca, _yawAlvo, 1f - Mathf.Exp(-5f * dt));

            if (_espec.Sentado)
            {
                Transform head = _moto != null ? _moto.Rig.Cabeca : null;

                if (head != null && (_moto == null || !_moto.enabled))
                {
                    head.rotation = Quaternion.AngleAxis(_yawCabeca, Vector3.up) * _cabecaSentado;
                }

                return;
            }

            if (_corpo == null || _quadril == null)
            {
                return;
            }

            _tempoPostura -= dt;

            if (_tempoPostura <= 0f)
            {
                Sorteia(conversando);
            }
            else if (conversando && _postura == Postura.Celular)
            {
                Sorteia(true);
            }

            AlvoDaPostura(out float down, out float pitch, out float bend, out float headPitch);
            float k = 1f - Mathf.Exp(-6f * dt);
            _bracoDown = Mathf.Lerp(_bracoDown, down, k);
            _bracoPitch = Mathf.Lerp(_bracoPitch, pitch, k);
            _bracoBend = Mathf.Lerp(_bracoBend, bend, k);
            _pitchCabeca = Mathf.Lerp(_pitchCabeca, headPitch, k);
            AplicarPose();
        }

        private void Sorteia(bool conversando)
        {
            float r = Mathf.Abs(Mathf.Sin(_t * 12.9898f + _espec.Fase * 78.233f));

            if (_espec.CapaceteNaMao && r < 0.75f)
            {
                _postura = Postura.Capacete;
            }
            else if (conversando)
            {
                _postura = r < 0.6f ? Postura.Gesto : Postura.Livre;
            }
            else
            {
                _postura = r < 0.45f ? Postura.Celular : (r < 0.7f ? Postura.Gesto : Postura.Livre);
            }

            _tempoPostura = 3f + (r * 977f % 1f) * 7f;
        }

        private void AlvoDaPostura(out float down, out float pitch, out float bend, out float headPitch)
        {
            down = 72f;
            pitch = 0f;
            bend = 12f;
            headPitch = 0f;

            switch (_postura)
            {
                case Postura.Celular:
                    down = 62f;
                    pitch = 22f;
                    bend = 88f;
                    headPitch = 22f;
                    break;
                case Postura.Gesto:
                    down = 58f;
                    pitch = 28f + Mathf.Sin(_t * 3.2f) * 10f;
                    bend = 70f + Mathf.Sin(_t * 4.7f + 1f) * 22f;
                    break;
                case Postura.Capacete:
                    down = 74f;
                    pitch = 6f;
                    bend = 22f;
                    break;
            }
        }

        private void AplicarPose()
        {
            float breath = Mathf.Sin(_t * 1.6f + _espec.Fase);
            float sway = Mathf.Sin(_t * 0.55f + _espec.Fase);
            Quaternion root = _corpo.rotation;
            Quaternion ombro = Quaternion.AngleAxis(_yawCabeca * 0.3f, Vector3.up);

            _quadril.rotation = root * Quaternion.AngleAxis(sway * 1.4f, Vector3.forward) * _rQuadril;

            if (_coluna != null) _coluna.rotation = root * ombro * Quaternion.AngleAxis(-sway * 1.6f, Vector3.forward) * Quaternion.AngleAxis(2f + breath * 0.8f, Vector3.right) * _rColuna;
            if (_coluna2 != null) _coluna2.rotation = root * ombro * Quaternion.AngleAxis(-sway * 1.2f, Vector3.forward) * Quaternion.AngleAxis(breath * 0.8f, Vector3.right) * _rColuna2;
            if (_pescoco != null) _pescoco.rotation = root * Quaternion.AngleAxis(_yawCabeca * 0.4f, Vector3.up) * Quaternion.AngleAxis(_pitchCabeca * 0.4f, Vector3.right) * _rPescoco;
            if (_cabeca != null) _cabeca.rotation = root * Quaternion.AngleAxis(_yawCabeca * 0.8f, Vector3.up) * Quaternion.AngleAxis(_pitchCabeca * 0.7f, Vector3.right) * Quaternion.AngleAxis(sway * 1.2f, Vector3.forward) * _rCabeca;

            // Braço esquerdo sempre caído; o direito segue a postura (celular, gesto, capacete).
            float down = 72f + breath * 1.2f;
            Braco(_bracoE, down, 0f, 0f, _rBracoE, root);
            Braco(_antebracoE, down - 10f, 0f, 10f, _rAntebracoE, root);
            Braco(_maoE, down - 10f, 0f, 10f, _rMaoE, root);
            Braco(_bracoD, -_bracoDown, _bracoPitch, 0f, _rBracoD, root);
            Braco(_antebracoD, -(_bracoDown - 10f), _bracoPitch, _bracoBend, _rAntebracoD, root);
            Braco(_maoD, -(_bracoDown - 10f), _bracoPitch, _bracoBend, _rMaoD, root);
        }

        private static void Braco(Transform bone, float angulo, float pitch, float dobra, Quaternion referencia, Quaternion root)
        {
            if (bone != null)
            {
                bone.rotation = root * Quaternion.AngleAxis(-(pitch + dobra), Vector3.right) * Quaternion.AngleAxis(angulo, Vector3.forward) * referencia;
            }
        }

        // ---------- Rotas (saída e chegada) ----------

        /// <summary>Sai rodando pela rota; ao terminar, Saiu = true.</summary>
        public void Partir(List<Vector3> rota)
        {
            Comecar(rota, false);
        }

        /// <summary>Chega rodando pela rota até o último ponto (a vaga) e estaciona.</summary>
        public void Chegar(List<Vector3> rota)
        {
            Comecar(rota, true);
        }

        private void Comecar(List<Vector3> rota, bool chegando)
        {
            _rota = rota;
            _passo = 1;
            _chegando = chegando;
            EmRota = true;
            Saiu = false;
            _velocidadeAtual = 0f;
            _caixa.enabled = false;

            if (_moto != null)
            {
                _moto.enabled = true;
            }

            if (chegando && rota.Count > 0)
            {
                transform.position = rota[0] + Vector3.up * AlturaDaRaiz;
            }
        }

        private void SeguirRota(float dt)
        {
            if (_rota == null || _passo >= _rota.Count)
            {
                Terminar();

                return;
            }

            Vector3 alvo = _rota[_passo] + Vector3.up * AlturaDaRaiz;
            Vector3 posicao = transform.position;
            Vector3 delta = alvo - posicao;
            delta.y = 0f;
            float dist = delta.magnitude;
            bool ultimo = _passo == _rota.Count - 1;
            float limite = _chegando && ultimo ? Mathf.Max(1.5f, _velocidadeAtual * 0.5f) : 4f;

            // Acelera suave; a chegada freia nos últimos metros.
            float velocidade = _chegando && ultimo ? Mathf.Clamp(dist * 1.6f, 1.2f, Velocidade) : Velocidade;
            _velocidadeAtual = Mathf.MoveTowards(_velocidadeAtual, _passo <= 1 && !_chegando ? 2.5f : velocidade, 4f * dt);

            if (dist > 0.05f)
            {
                Quaternion alvoRot = Quaternion.LookRotation(delta.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, alvoRot, 130f * dt);
            }

            Vector3 novo = Vector3.MoveTowards(posicao, new Vector3(alvo.x, posicao.y, alvo.z), _velocidadeAtual * dt);
            novo.y = Mathf.MoveTowards(posicao.y, alvo.y, 0.6f * dt + dist * 0.0f);
            transform.position = novo;

            if (_moto != null)
            {
                _moto.DefinirVelocidadeExterna(_velocidadeAtual);
            }

            if (dist <= (ultimo && _chegando ? 0.15f : Mathf.Min(limite, 3f)))
            {
                _passo++;
            }
        }

        private void Terminar()
        {
            EmRota = false;

            if (_chegando)
            {
                _velocidadeAtual = 0f;

                if (_moto != null)
                {
                    _moto.DefinirVelocidadeExterna(0f);
                }

                _parar = 1.1f;
                _caixa.enabled = true;
                _chegando = false;
            }
            else
            {
                Saiu = true;
            }
        }
    }
}
