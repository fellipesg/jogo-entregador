using System.Collections.Generic;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Um braço do cruzamento que chega ao semáforo: a linha de retenção pintada na pista, o sentido de quem chega,
    /// as lentes dos focos (vermelho, amarelo, verde) e do foco de pedestre, e o flash do fotossensor (se houver).
    /// </summary>
    [System.Serializable]
    public sealed class SemaforoAproximacao
    {
        /// <summary>Eixo (0 ou 1): braços do mesmo eixo abrem juntos.</summary>
        public int Grupo;

        /// <summary>Centro da linha de retenção, no eixo da rua.</summary>
        public Vector3 Linha;

        /// <summary>Sentido de quem chega (para o cruzamento), unitário e horizontal.</summary>
        public Vector3 Direcao;

        /// <summary>Quanto a pista de chegada se estende à esquerda e à direita do eixo (m).</summary>
        public float LarguraEsquerda;

        public float LarguraDireita;

        public Renderer[] Vermelhos;
        public Renderer[] Amarelos;
        public Renderer[] Verdes;
        public Renderer[] PedestreVermelho;
        public Renderer[] PedestreVerde;
        public Light Flash;
        public Renderer FlashLente;

        [System.NonSerialized] public SemaforoControlador Dono;
        [System.NonSerialized] public SemaforoEstado EstadoAplicado = (SemaforoEstado)(-1);
        [System.NonSerialized] public bool PedestreVerdeAplicado;

        /// <summary>Direita de quem chega (horizontal).</summary>
        public Vector3 Direita => new Vector3(Direcao.z, 0f, -Direcao.x);

        /// <summary>Deslocamento lateral do ponto em relação ao eixo (positivo = à direita de quem chega).</summary>
        public float Lateral(Vector3 p) => Vector3.Dot(p - Linha, Direita);

        /// <summary>Distância (m) do ponto até a linha, medida no sentido de chegada. Negativa depois de cruzar.</summary>
        public float DistanciaAte(Vector3 p) => Vector3.Dot(Linha - p, Direcao);

        /// <summary>O ponto está sobre a pista deste braço (com uma folga lateral)?</summary>
        public bool NaPista(Vector3 p, float folga)
        {
            float lateral = Lateral(p);

            return lateral >= -LarguraEsquerda - folga && lateral <= LarguraDireita + folga;
        }
    }

    /// <summary>
    /// Controlador de um cruzamento semaforizado: roda o ciclo do Core (SemaforoCiclo) com o relógio do jogo mais um deslocamento
    /// próprio, acende as lentes (emissivas) de cada braço e diz o estado a quem perguntar (carros, fiscal, GPS e interface).
    /// Pode ter um fotossensor (câmera de avanço de sinal) que sempre captura quem cruza no vermelho.
    /// </summary>
    public sealed class SemaforoControlador : MonoBehaviour
    {
        private const float FlashSeconds = 0.18f;

        private static readonly List<SemaforoControlador> Todos = new List<SemaforoControlador>();

        [SerializeField] private float _verdeA = 26f;
        [SerializeField] private float _verdeB = 22f;
        [SerializeField] private float _amarelo = 3f;
        [SerializeField] private float _todoVermelho = 1.5f;
        [SerializeField] private float _deslocamento;
        [SerializeField] private bool _fotossensor;
        [SerializeField] private SemaforoAproximacao[] _aproximacoes = new SemaforoAproximacao[0];
        [SerializeField] private Material[] _lenteAcesa = new Material[3];
        [SerializeField] private Material[] _lenteApagada = new Material[3];

        private SemaforoConfig _config;
        private bool _congelado;
        private double _tempoCongelado;
        private float _flashAte;

        /// <summary>Todos os cruzamentos semaforizados da cena.</summary>
        public static IReadOnlyList<SemaforoControlador> Lista => Todos;

        public SemaforoAproximacao[] Aproximacoes => _aproximacoes;

        /// <summary>Tem fotossensor: quem avança o vermelho é sempre capturado.</summary>
        public bool Fotossensor => _fotossensor;

        /// <summary>Quantas infrações o fotossensor já capturou.</summary>
        public int Capturas { get; private set; }

        public SemaforoConfig Config => _config;

        public void Configure(SemaforoConfig config, bool fotossensor, SemaforoAproximacao[] aproximacoes, Material[] lenteAcesa, Material[] lenteApagada)
        {
            _verdeA = (float)config.VerdeA;
            _verdeB = (float)config.VerdeB;
            _amarelo = (float)config.Amarelo;
            _todoVermelho = (float)config.TodoVermelho;
            _deslocamento = (float)config.Deslocamento;
            _fotossensor = fotossensor;
            _aproximacoes = aproximacoes;
            _lenteAcesa = lenteAcesa;
            _lenteApagada = lenteApagada;
        }

        private void Awake()
        {
            _config = new SemaforoConfig { VerdeA = _verdeA, VerdeB = _verdeB, Amarelo = _amarelo, TodoVermelho = _todoVermelho, Deslocamento = _deslocamento };

            foreach (SemaforoAproximacao a in _aproximacoes)
            {
                a.Dono = this;
                SetFlash(a, false);
            }
        }

        private void OnEnable()
        {
            Todos.Add(this);
        }

        private void OnDisable()
        {
            Todos.Remove(this);
        }

        /// <summary>Relógio do ciclo (s).</summary>
        public double Tempo => _congelado ? _tempoCongelado : Time.timeAsDouble;

        public SemaforoEstado Estado(int grupo) => SemaforoCiclo.Estado(_config, Tempo, grupo);

        public SemaforoEstado Estado(SemaforoAproximacao a) => Estado(a.Grupo);

        /// <summary>Segundos desde que o braço ficou vermelho, ou -1 se não está vermelho.</summary>
        public float SegundosDesdeVermelho(SemaforoAproximacao a) => (float)SemaforoCiclo.SegundosDesdeVermelho(_config, Tempo, a.Grupo);

        /// <summary>Segundos até o braço mudar de cor.</summary>
        public float SegundosParaMudar(SemaforoAproximacao a) => (float)SemaforoCiclo.SegundosParaMudar(_config, Tempo, a.Grupo);

        /// <summary>
        /// Congela o ciclo com o grupo no estado indicado, "dentro" segundos depois de ele começar (testes e capturas de tela).
        /// O outro grupo fica no estado que o ciclo manda para aquele instante.
        /// </summary>
        public void Fixar(int grupo, SemaforoEstado estado, double dentro = 0.2)
        {
            _congelado = true;
            _tempoCongelado = SemaforoCiclo.InicioDoEstado(_config, grupo, estado) - _config.Deslocamento + dentro;
            Atualizar(true);
        }

        /// <summary>Volta a seguir o relógio.</summary>
        public void Liberar()
        {
            _congelado = false;
        }

        /// <summary>Dispara o flash do fotossensor do braço (e conta a captura).</summary>
        public void Capturar(SemaforoAproximacao a)
        {
            Capturas++;
            _flashAte = Time.time + FlashSeconds;
            SetFlash(a, true);
        }

        private void Update()
        {
            Atualizar(false);

            if (_flashAte > 0f && Time.time >= _flashAte)
            {
                _flashAte = 0f;

                foreach (SemaforoAproximacao a in _aproximacoes)
                {
                    SetFlash(a, false);
                }
            }
        }

        private void Atualizar(bool forcar)
        {
            foreach (SemaforoAproximacao a in _aproximacoes)
            {
                SemaforoEstado estado = Estado(a.Grupo);
                bool pedestre = Estado(1 - a.Grupo) == SemaforoEstado.Verde;

                if (!forcar && estado == a.EstadoAplicado && pedestre == a.PedestreVerdeAplicado)
                {
                    continue;
                }

                a.EstadoAplicado = estado;
                a.PedestreVerdeAplicado = pedestre;
                Aplicar(a.Vermelhos, estado == SemaforoEstado.Vermelho ? _lenteAcesa[0] : _lenteApagada[0]);
                Aplicar(a.Amarelos, estado == SemaforoEstado.Amarelo ? _lenteAcesa[1] : _lenteApagada[1]);
                Aplicar(a.Verdes, estado == SemaforoEstado.Verde ? _lenteAcesa[2] : _lenteApagada[2]);
                Aplicar(a.PedestreVermelho, pedestre ? _lenteApagada[0] : _lenteAcesa[0]);
                Aplicar(a.PedestreVerde, pedestre ? _lenteAcesa[2] : _lenteApagada[2]);
            }
        }

        private static void Aplicar(Renderer[] lentes, Material material)
        {
            if (lentes == null || material == null)
            {
                return;
            }

            foreach (Renderer lente in lentes)
            {
                if (lente != null)
                {
                    lente.sharedMaterial = material;
                }
            }
        }

        private static void SetFlash(SemaforoAproximacao a, bool on)
        {
            if (a.Flash != null)
            {
                a.Flash.enabled = on;
            }

            if (a.FlashLente != null)
            {
                a.FlashLente.enabled = on;
            }
        }
    }
}
