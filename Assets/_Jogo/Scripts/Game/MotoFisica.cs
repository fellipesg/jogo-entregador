using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Números de física de uma moto: câmbio de 5 marchas, motor, arrasto, freio e curva. Os valores foram ajustados em simulação
    /// para bater o desempenho real (velocidade final e 0 a 60 km/h). O mundo está em metros reais, então km/h na tela = km/h de verdade.
    /// </summary>
    public sealed class MotoFisicaPerfil
    {
        public string Nome;

        /// <summary>Velocidade final aproximada (km/h), só informativa: ela sai do equilíbrio entre força e arrasto.</summary>
        public float VelocidadeFinalKmh;

        /// <summary>Velocidade (m/s) de cada marcha no giro máximo (corte de giro).</summary>
        public float[] MarchaVmax;

        /// <summary>Força do motor (m/s² multiplicados pela razão da 1ª marcha). Quanto maior, mais rápido o 0 a 60.</summary>
        public float Forca;

        /// <summary>Arrasto aerodinâmico (1/m): desaceleração = Arrasto * v².</summary>
        public float Arrasto;

        /// <summary>Resistência de rolagem constante (m/s²).</summary>
        public float Rolagem = 0.15f;

        public float RpmMarchaLenta = 1300f;
        public float RpmLimite;

        /// <summary>Freio-motor máximo (m/s²) com o acelerador solto e o giro no teto.</summary>
        public float FreioMotor = 2.2f;

        /// <summary>Freio combinado (dianteiro e traseiro) em m/s². Moto de rua freia perto de 1 g; 9 é "boa frenagem" sem exagero.</summary>
        public float Freio = 9f;

        /// <summary>Aceleração lateral máxima em curva (m/s²): limita o giro quanto maior a velocidade.</summary>
        public float AceleracaoLateral = 9.5f;

        /// <summary>Giro máximo (graus por segundo) em baixa velocidade (manobra de pátio).</summary>
        public float GiroBaixa = 120f;

        public int Marchas => MarchaVmax.Length;

        public float VelocidadeFinal => VelocidadeFinalKmh / 3.6f;
    }

    /// <summary>Perfil de física de cada modelo de moto.</summary>
    public static class MotoFisicaCatalogo
    {
        private static MotoFisicaPerfil _titan;
        private static MotoFisicaPerfil _twister;

        /// <summary>Titã 160: final ~115 km/h, 0 a 60 em ~6 s, giro até 9.000 rpm.</summary>
        public static MotoFisicaPerfil Titan => _titan ?? (_titan = new MotoFisicaPerfil
        {
            Nome = "Titã 160",
            VelocidadeFinalKmh = 115f,
            MarchaVmax = new[] { 8f, 14f, 21f, 30f, 40f },
            Forca = 6.2f,
            Arrasto = 0.001f,
            RpmLimite = 9000f,
        });

        /// <summary>Tornado 250: final ~140 km/h, 0 a 60 em ~4,5 s, giro até 10.000 rpm.</summary>
        public static MotoFisicaPerfil Twister => _twister ?? (_twister = new MotoFisicaPerfil
        {
            Nome = "Tornado 250",
            VelocidadeFinalKmh = 140f,
            MarchaVmax = new[] { 9f, 16f, 24f, 34f, 46f },
            Forca = 7.6f,
            Arrasto = 0.0008f,
            RpmLimite = 10000f,
            Freio = 9.6f,
            AceleracaoLateral = 10.5f,
        });

        public static MotoFisicaPerfil Para(MotoModelo modelo) => modelo == MotoModelo.Twister ? Twister : Titan;
    }

    /// <summary>
    /// Câmbio automático de 5 marchas e giro do motor. Dá a aceleração do motor (m/s²) para a velocidade e o acelerador,
    /// troca de marcha sozinho (com um corte curto de força na troca), limita o giro e faz o freio-motor.
    /// </summary>
    public sealed class Transmissao
    {
        private const float UpshiftAt = 0.86f;
        private const float DownshiftBelow = 0.30f;
        private const float ShiftSeconds = 0.12f;

        private readonly MotoFisicaPerfil _perfil;
        private float _shiftTimer;
        private float _rpm;

        public Transmissao(MotoFisicaPerfil perfil)
        {
            _perfil = perfil;
            _rpm = perfil.RpmMarchaLenta;
            Marcha = 1;
        }

        /// <summary>Marcha engatada, de 1 a 5.</summary>
        public int Marcha { get; private set; }

        public float Rpm => _rpm;

        /// <summary>Embreagem puxada (cortando giro parado): o giro sobe livre e a roda não recebe força.</summary>
        public bool Embreagem { get; private set; }

        /// <summary>O limitador de giro está cortando (o giro quica perto do limite).</summary>
        public bool NoLimitador { get; private set; }

        private float _cutTimer;
        private float _holdTimer;

        /// <summary>Fração do giro (0 na marcha lenta, 1 no limite).</summary>
        public float GiroNormalizado => Mathf.InverseLerp(_perfil.RpmMarchaLenta, _perfil.RpmLimite, _rpm);

        /// <summary>Curva de torque: máximo em ~62% do giro, cai nas pontas.</summary>
        public static float Torque(float x)
        {
            if (x >= 1f)
            {
                return 0f;
            }

            float d = x - 0.62f;

            return Mathf.Max(0.35f, 1f - 0.9f * d * d / 0.3844f);
        }

        /// <summary>
        /// Avança o câmbio e o motor um passo. speed em m/s (zero se a moto está parada ou em ré), throttle de 0 a 1.
        /// Devolve a aceleração do motor em m/s²: positiva com acelerador, negativa (freio-motor) com ele solto.
        /// </summary>
        public float Step(float speed, float throttle, float dt)
        {
            Embreagem = false;
            speed = Mathf.Max(0f, speed);
            int top = _perfil.Marchas - 1;
            int g = Marcha - 1;
            float x = speed / _perfil.MarchaVmax[g];

            if (_shiftTimer > 0f)
            {
                _shiftTimer -= dt;
            }
            else if (g < top && x > UpshiftAt)
            {
                Marcha++;
                g++;
                _shiftTimer = ShiftSeconds;
                x = speed / _perfil.MarchaVmax[g];
            }
            else if (g > 0 && x < DownshiftBelow && speed / _perfil.MarchaVmax[g - 1] < 0.85f)
            {
                Marcha--;
                g--;
                x = speed / _perfil.MarchaVmax[g];
            }

            // Giro: acompanha a roda; parado, o acelerador "toca" o motor até metade do caminho (embreagem patinando).
            float target = Mathf.Max(Mathf.Clamp01(x), speed < 3f ? throttle * 0.5f : 0f);
            float goal = Mathf.Lerp(_perfil.RpmMarchaLenta, _perfil.RpmLimite, Mathf.Min(1f, target));

            // Saindo de um corte de giro: a roda patina e o giro demora a cair.
            if (_holdTimer > 0f)
            {
                _holdTimer -= dt;
                goal = Mathf.Max(goal, Mathf.Lerp(_perfil.RpmMarchaLenta, _perfil.RpmLimite, 0.7f));
            }

            _rpm = Mathf.MoveTowards(_rpm, goal, 14000f * dt);
            AplicarLimitador(throttle, dt);

            if (throttle > 0.05f)
            {
                if (_shiftTimer > 0f || x >= 1f)
                {
                    return 0f;
                }

                float torque = Torque(Mathf.Max(x, 0.12f));

                return _perfil.Forca * torque * throttle * _perfil.MarchaVmax[0] / _perfil.MarchaVmax[g];
            }

            return -_perfil.FreioMotor * (0.3f + 0.7f * Mathf.Clamp01(x));
        }

        /// <summary>
        /// Moto parada com a embreagem puxada ("cortando giro"): o giro sobe livre até o limitador (cerca de 0,5 s) e cai ao soltar.
        /// A roda não recebe força: quem chama mantém a moto parada.
        /// </summary>
        public void StepNeutro(float throttle, float dt)
        {
            Embreagem = true;
            float limit = _perfil.RpmLimite;
            float goal = throttle > 0.05f ? Mathf.Lerp(_perfil.RpmMarchaLenta, limit * 1.02f, throttle) : _perfil.RpmMarchaLenta;
            float rate = goal > _rpm ? (limit - _perfil.RpmMarchaLenta) / 0.5f : 9000f;
            _rpm = Mathf.MoveTowards(_rpm, goal, rate * dt);
            AplicarLimitador(throttle, dt);
        }

        /// <summary>Soltou o freio com o motor girando: a roda patina um instante e o giro demora a cair.</summary>
        public void IniciarArrancada()
        {
            float n = Mathf.Clamp01((_rpm - _perfil.RpmMarchaLenta) / (_perfil.RpmLimite - _perfil.RpmMarchaLenta));
            _holdTimer = 0.5f * n;
        }

        /// <summary>Corte de ignição no limite: o giro cai uns 550 rpm, volta e corta de novo (quica a ~13 Hz).</summary>
        private void AplicarLimitador(float throttle, float dt)
        {
            float limit = _perfil.RpmLimite;

            if (_cutTimer > 0f)
            {
                _cutTimer -= dt;
                _rpm = Mathf.MoveTowards(_rpm, limit - 550f, 40000f * dt);
                NoLimitador = true;

                return;
            }

            if (throttle > 0.5f && _rpm >= limit - 20f)
            {
                _cutTimer = 0.036f;
                _rpm = Mathf.Min(_rpm, limit);
            }

            NoLimitador = throttle > 0.5f && _rpm >= limit - 650f;
        }
    }
}
