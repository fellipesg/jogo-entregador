using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Câmera de terceira pessoa: atrás e acima da moto, suave, com o campo de visão abrindo com a velocidade.</summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] private ArcadeBikeController _target;
        [SerializeField] private float _distance = 6.5f;
        [SerializeField] private float _height = 3.2f;
        [SerializeField] private float _lookAhead = 5f;
        [SerializeField] private float _positionSharpness = 7f;
        [SerializeField] private float _headingSharpness = 3f;
        [SerializeField] private float _baseFov = 62f;
        [SerializeField] private float _speedFov = 12f;

        private const float RaioColisao = 0.4f;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly Collider[] Colliders = new Collider[24];

        private float _heading;
        private Camera _camera;
        private bool _enquadrando;
        private Vector3 _foco;
        private Vector3 _posEnquadro;
        private Vector3 _olharEnquadro;
        private float _proxReenquadro;

        /// <summary>Há um enquadramento de diálogo ligado.</summary>
        public bool Enquadrando => _enquadrando;

        public Vector3 PosicaoEnquadro => _posEnquadro;

        /// <summary>
        /// Enquadra a moto e o foco (porta, balcão, roda de conversa) para um diálogo: escolhe, entre vários ângulos em volta da moto, a
        /// primeira câmera que não fica dentro de parede e de onde o foco aparece. Sem foco (ou ao soltar) volta a seguir atrás da moto.
        /// </summary>
        public void Enquadrar(Vector3 foco)
        {
            if (_enquadrando && (foco - _foco).sqrMagnitude < 0.01f && _target != null && (_olharEnquadro - Meio(foco)).sqrMagnitude < 4f)
            {
                return;
            }

            bool corte = !_enquadrando;
            _foco = foco;
            _enquadrando = true;
            Escolher();

            // Ao abrir o diálogo a câmera corta direto para o enquadramento (suavizar atravessaria prédios no caminho).
            if (corte)
            {
                transform.position = _posEnquadro;
                transform.rotation = Quaternion.LookRotation(_olharEnquadro - _posEnquadro, Vector3.up);
            }
        }

        public void Soltar()
        {
            _enquadrando = false;
        }

        private Vector3 Pivo() => _target.Position + Vector3.up * 1.4f;

        private Vector3 Meio(Vector3 foco)
        {
            Vector3 meio = (_target.Position + foco) * 0.5f;
            meio.y = _target.Position.y + 1.3f;

            return meio;
        }

        /// <summary>Anda da origem até o destino com uma esfera e pára antes de qualquer parede ou carro (a moto não conta). Devolve a posição livre.</summary>
        private Vector3 PuxarParaForaDeParedes(Vector3 origem, Vector3 destino)
        {
            Vector3 d = destino - origem;
            float dist = d.magnitude;

            if (dist < 0.01f)
            {
                return destino;
            }

            d /= dist;
            int n = Physics.SphereCastNonAlloc(origem, RaioColisao, d, Hits, dist, ~0, QueryTriggerInteraction.Ignore);
            float menor = dist;

            for (int i = 0; i < n; i++)
            {
                Collider c = Hits[i].collider;

                if (c == null || (_target != null && c.transform.IsChildOf(_target.transform)) || Hits[i].distance <= 0f)
                {
                    continue;
                }

                menor = Mathf.Min(menor, Hits[i].distance);
            }

            return origem + d * Mathf.Max(Mathf.Min(1.5f, dist), menor - 0.1f);
        }

        /// <summary>A copa das árvores não tem collider (só o tronco): câmera a menos de 3,5 m de um tronco ou poste fino fica dentro da folhagem.</summary>
        private bool PertoDeTronco(Vector3 pos)
        {
            int n = Physics.OverlapSphereNonAlloc(new Vector3(pos.x, 0.5f, pos.z), 3.5f, Colliders, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                Bounds b = Colliders[i].bounds;

                if (b.size.x < 1.4f && b.size.z < 1.4f && b.size.y > 1.5f && (_target == null || !Colliders[i].transform.IsChildOf(_target.transform)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool Livre(Vector3 origem, Vector3 destino)
        {
            return (PuxarParaForaDeParedes(origem, destino) - destino).sqrMagnitude < 0.04f;
        }

        private void Escolher()
        {
            Vector3 pivo = Pivo();
            Vector3 meio = Meio(_foco);
            float[] graus = { 0f, 25f, -25f, 50f, -50f, 80f, -80f, 110f, -110f, 150f, -150f, 180f };
            float[] distancias = { 6.5f, 5f, 3.8f };
            Vector3 melhor = Vector3.zero;
            float melhorNota = float.MinValue;
            bool achou = false;

            foreach (float dist in distancias)
            {
                foreach (float g in graus)
                {
                    Vector3 dir = Quaternion.Euler(0f, _target.Yaw + g, 0f) * Vector3.back;
                    Vector3 pos = pivo + dir * dist + Vector3.up * (_height - 0.2f);
                    bool caminho = Livre(pivo, pos);

                    if (!caminho)
                    {
                        continue;
                    }

                    Vector3 alvo = _foco + Vector3.up * 1.3f;
                    bool veFoco = Livre(pos, Vector3.Lerp(pos, alvo, 0.9f));
                    float nota = (veFoco ? 100f : 0f) - Mathf.Abs(g) * 0.2f + dist * 2f - (PertoDeTronco(pos) ? 70f : 0f);

                    if (nota > melhorNota)
                    {
                        melhorNota = nota;
                        melhor = pos;
                        achou = true;
                    }
                }

                if (achou && melhorNota >= 100f)
                {
                    break;
                }
            }

            if (!achou)
            {
                melhor = PuxarParaForaDeParedes(pivo, pivo + Vector3.up * 5f + Quaternion.Euler(0f, _target.Yaw, 0f) * Vector3.back * 2f);
            }

            _posEnquadro = melhor;
            _olharEnquadro = meio;
        }

        public void Configure(ArcadeBikeController target)
        {
            _target = target;
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void Start()
        {
            Snap();
        }

        /// <summary>Coloca a câmera direto atrás da moto, sem suavização (depois de um teleporte).</summary>
        public void Snap()
        {
            if (_target == null)
            {
                return;
            }

            _enquadrando = false;
            _heading = _target.Yaw;
            transform.position = DesiredPosition();
            transform.rotation = DesiredRotation();
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            float dt = Time.deltaTime;

            if (_enquadrando)
            {
                _proxReenquadro -= dt;

                if (_proxReenquadro <= 0f)
                {
                    _proxReenquadro = 1f;

                    // Se a posição escolhida deixou de ser livre (carro parou ali), escolhe de novo.
                    if (!Livre(Pivo(), _posEnquadro))
                    {
                        Escolher();
                    }
                }

                transform.position = Vector3.Lerp(transform.position, _posEnquadro, 1f - Mathf.Exp(-5f * dt));
                Vector3 olhar = _olharEnquadro - transform.position;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(olhar, Vector3.up), 1f - Mathf.Exp(-6f * dt));

                if (_camera != null)
                {
                    _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, _baseFov, 1f - Mathf.Exp(-3f * dt));
                }

                return;
            }

            _heading = Mathf.LerpAngle(_heading, _target.Yaw, 1f - Mathf.Exp(-_headingSharpness * dt));

            // Em alta velocidade a câmera segue mais colada (senão a moto fugiria do quadro) e olha mais longe.
            float ratio = Mathf.Clamp01(_target.Speed / _target.TopSpeed);
            float sharpness = Mathf.Lerp(_positionSharpness, _positionSharpness * 2.2f, ratio);
            Vector3 livre = PuxarParaForaDeParedes(Pivo(), DesiredPosition());
            transform.position = Vector3.Lerp(transform.position, livre, 1f - Mathf.Exp(-sharpness * dt));
            transform.rotation = Quaternion.LookRotation(DesiredLookPoint() - transform.position, Vector3.up);

            if (_camera != null)
            {
                float speedRatio = ratio;
                _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, _baseFov + _speedFov * speedRatio, 1f - Mathf.Exp(-3f * dt));
            }
        }

        private Vector3 DesiredPosition()
        {
            Vector3 back = Quaternion.Euler(0f, _heading, 0f) * Vector3.back;

            float ratio = Mathf.Clamp01(_target.Speed / _target.TopSpeed);

            return _target.Position + back * (_distance * (1f + 0.25f * ratio)) + Vector3.up * _height;
        }

        private Vector3 DesiredLookPoint()
        {
            float ratio = Mathf.Clamp01(_target.Speed / _target.TopSpeed);
            Vector3 ahead = Quaternion.Euler(0f, _heading, 0f) * Vector3.forward * (_lookAhead * (1f + ratio));

            return _target.Position + ahead + Vector3.up * 1.2f;
        }

        private Quaternion DesiredRotation()
        {
            return Quaternion.LookRotation(DesiredLookPoint() - DesiredPosition(), Vector3.up);
        }
    }
}
