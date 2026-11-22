using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Entregador.Game
{
    /// <summary>
    /// Painel da moto no canto de baixo à direita, no estilo dos jogos de corrida: conta-giros analógico com agulha amortecida e faixa
    /// vermelha vinda do limitador, velocidade digital grande, marcha, luzes de troca (shift light), arcos de acelerador e freio,
    /// placa de limite da via, avisos de contramão e corredor de ônibus e a fileira de luzes de aviso da moto.
    /// Tudo desenhado em código (malhas de UI e sprites gerados).
    /// </summary>
    public sealed class PainelMotoView
    {
        public const float RpmMaximo = 10000f;
        private const float Raio = 170f;
        private const float Sweep = 270f;
        private const float StartDeg = 225f;

        private enum Luz { Injecao, Oleo, Temperatura, Bateria, Combustivel, Neutro, FarolAlto, SetaEsq, SetaDir }

        private sealed class Aviso
        {
            public Image Glow;
            public UiShape Icone;
            public TextMeshProUGUI Letra;
            public Image Fundo;
            public Color Cor;
            public float Nivel;
        }

        private readonly RectTransform _root;
        private readonly Aviso[] _luzes = new Aviso[9];
        private readonly Image[] _leds = new Image[9];
        private UiShape _agulha;
        private UiArc _acelerador;
        private UiArc _freio;
        private UiArc _faixaVermelha;
        private UiTicks _ticks;
        private readonly List<TextMeshProUGUI> _numeros = new List<TextMeshProUGUI>();
        private TextMeshProUGUI _velocidade;
        private TextMeshProUGUI _marcha;
        private bool _cortandoGiro;
        private bool _noLimitador;
        private Image _brilhoAlerta;
        private Image _aroLimitador;
        private RectTransform _combustivelBarra;
        private Image _combustivelCor;

        private RectTransform _placa;
        private Image _placaAro;
        private Image _placaBrilho;
        private TextMeshProUGUI _placaTexto;
        private RectTransform _chipContramao;
        private RectTransform _chipCorredor;
        private RectTransform _chipSinal;
        private TextMeshProUGUI _chipSinalTexto;
        private RectTransform _chipCamera;
        private bool _sinalVermelho;
        private bool _sinalCamera;
        private int _sinalMetros;

        private float _rpmAlvo;
        private float _rpmMostrado;
        private float _rpmVel;
        private float _limitador = 9000f;
        private float _speedKmh;
        private float _speedMostrada;
        private int _limiteVia;
        private float _acel;
        private float _freioValor;
        private bool _contramao;
        private bool _corredor;
        private int _marchaAtual;
        private PainelMotoEstado _estado;
        private float _redStart = -1f;

        public RectTransform Root => _root;

        /// <summary>Rotação da agulha (graus) e valor mostrado, para os testes.</summary>
        public float RpmMostrado => _rpmMostrado;

        public PainelMotoView(Transform parent)
        {
            _root = UiKit.Rect("PainelMoto", parent);
            UiKit.Place(_root, new Vector2(1f, 0f), new Vector2(-24f, 20f), new Vector2(360f, 410f));
            BuildDial();
            BuildStrip();
            BuildLimitSign();
            BuildWarnings();
            SetLimiter(9000f);
        }

        // ---------- Montagem ----------

        private void BuildDial()
        {
            const float cx = 180f;
            const float cy = 236f;
            RectTransform dial = UiKit.Rect("Mostrador", _root);
            UiKit.Place(dial, new Vector2(0f, 0f), new Vector2(cx - Raio, cy - Raio), new Vector2(Raio * 2f, Raio * 2f));

            _brilhoAlerta = UiKit.Img(dial, "BrilhoAlerta", UiSprites.Glow(), new Color(1f, 0.1f, 0.1f, 0f), false);
            UiKit.Stretch(_brilhoAlerta.rectTransform, -34f);

            UiKit.Img(dial, "Sombra", UiSprites.Glow(), new Color(0f, 0f, 0f, 0.55f), false).rectTransform.SetStretch(-26f);
            Image bezel = UiKit.Circle(dial, "Aro", new Color(0.03f, 0.034f, 0.042f, 0.97f));
            UiKit.Stretch(bezel.rectTransform);
            Image rim = UiKit.Img(dial, "AroBorda", UiSprites.Ring(), new Color(1f, 1f, 1f, 0.16f), false);
            UiKit.Stretch(rim.rectTransform);
            Image face = UiKit.Circle(dial, "Face", new Color(0.07f, 0.08f, 0.10f, 0.96f));
            UiKit.Stretch(face.rectTransform, 12f);
            Image backlight = UiKit.Img(dial, "Luz", UiSprites.Glow(), new Color(0.2f, 0.42f, 0.8f, 0.2f), false);
            UiKit.Stretch(backlight.rectTransform, 14f);
            _aroLimitador = UiKit.Img(dial, "AroLimitador", UiSprites.Ring(), new Color(1f, 0.15f, 0.15f, 0f), false);
            UiKit.Stretch(_aroLimitador.rectTransform, 12f);

            // Trilhos e arcos de acelerador (esquerda) e freio (direita), por fora da escala.
            UiArc trackL = UiKit.Gfx<UiArc>(dial, "TrilhoAcel");
            UiKit.Stretch(trackL.rectTransform);
            trackL.SetRadii(Raio - 22f, Raio - 17f);
            trackL.color = new Color(1f, 1f, 1f, 0.10f);
            trackL.Set(225f, 92f);
            UiArc trackR = UiKit.Gfx<UiArc>(dial, "TrilhoFreio");
            UiKit.Stretch(trackR.rectTransform);
            trackR.SetRadii(Raio - 22f, Raio - 17f);
            trackR.color = new Color(1f, 1f, 1f, 0.10f);
            trackR.Set(-45f, 88f);

            _acelerador = UiKit.Gfx<UiArc>(dial, "Acelerador");
            UiKit.Stretch(_acelerador.rectTransform);
            _acelerador.SetRadii(Raio - 22f, Raio - 17f);
            _acelerador.color = UiTema.Sucesso;
            _acelerador.Set(225f, 225f);

            _freio = UiKit.Gfx<UiArc>(dial, "Freio");
            UiKit.Stretch(_freio.rectTransform);
            _freio.SetRadii(Raio - 22f, Raio - 17f);
            _freio.color = UiTema.Perigo;
            _freio.Set(-45f, -45f);

            _faixaVermelha = UiKit.Gfx<UiArc>(dial, "FaixaVermelha");
            UiKit.Stretch(_faixaVermelha.rectTransform);
            _faixaVermelha.SetRadii(Raio - 44f, Raio - 40f);
            _faixaVermelha.color = UiTema.Perigo;

            UiArc scaleArc = UiKit.Gfx<UiArc>(dial, "Escala");
            UiKit.Stretch(scaleArc.rectTransform);
            scaleArc.SetRadii(Raio - 45.5f, Raio - 44.5f);
            scaleArc.color = new Color(1f, 1f, 1f, 0.35f);
            scaleArc.Set(StartDeg, StartDeg - Sweep);

            _ticks = UiKit.Gfx<UiTicks>(dial, "Marcas");
            UiKit.Stretch(_ticks.rectTransform);

            for (int i = 0; i <= 20; i++)
            {
                float rpm = i * 500f;
                bool major = i % 2 == 0;
                _ticks.Add(Angulo(rpm), Raio - (major ? 66f : 58f), Raio - 46f, major ? 3f : 1.5f, major ? UiTema.Texto : new Color(1f, 1f, 1f, 0.55f));
            }

            for (int i = 0; i <= 10; i++)
            {
                float a = Angulo(i * 1000f) * Mathf.Deg2Rad;
                TextMeshProUGUI n = UiKit.Text(dial, "Num" + i, i.ToString(), 19f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Center);
                UiKit.Place(n.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (Raio - 88f), new Vector2(34f, 24f));
                _numeros.Add(n);
            }

            TextMeshProUGUI x1000 = UiKit.Caps(dial, "x1000", "rpm x1000", UiTema.TextoFraco, TextAlignmentOptions.Center, 11f);
            UiKit.Place(x1000.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(120f, 16f));

            // Luzes de troca de marcha (shift light).
            for (int i = 0; i < _leds.Length; i++)
            {
                Image led = UiKit.Circle(dial, "Led" + i, Color.white);
                UiKit.Place(led.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
                float la = (90f + (4 - i) * 9.5f) * Mathf.Deg2Rad;
                led.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(la), Mathf.Sin(la)) * (Raio - 7f);
                _leds[i] = led;
            }

            // Velocidade digital grande e marcha.
            _velocidade = UiKit.Text(dial, "Velocidade", "0", 88f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Center, -2f);
            UiKit.Place(_velocidade.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(220f, 100f));
            TextMeshProUGUI unit = UiKit.Caps(dial, "Unidade", "km/h", UiTema.TextoSuave, TextAlignmentOptions.Center, 15f);
            UiKit.Place(unit.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(120f, 20f));

            RectTransform gearBox = UiKit.Panel(dial, "MarchaCaixa", 14, new Color(1f, 1f, 1f, 0.07f), false, true);
            UiKit.Place(gearBox, new Vector2(0.5f, 0.5f), new Vector2(0f, -94f), new Vector2(62f, 56f));
            _marcha = UiKit.Text(gearBox, "Marcha", "N", 40f, UiTema.Sucesso, FontStyles.Bold, TextAlignmentOptions.Center);
            UiKit.Stretch(_marcha.rectTransform);
            _marcha.rectTransform.offsetMax = new Vector2(0f, 2f);

            // Agulha: lâmina curta sobre a escala (não cobre os números nem a velocidade); caixa centrada girada por RectTransform.
            RectTransform needleBox = UiKit.Rect("AgulhaCaixa", dial);
            UiKit.Place(needleBox, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 134f * 2f));
            _agulha = UiKit.Gfx<UiShape>(needleBox, "Agulha");
            UiKit.Stretch(_agulha.rectTransform);
            _agulha.SetPolygons(new[]
            {
                new[] { new Vector2(0.5f, 1f), new Vector2(0.74f, 0.86f), new Vector2(0.5f, 0.82f), new Vector2(0.26f, 0.86f) },
            });
            _agulha.color = new Color(1f, 0.28f, 0.22f, 1f);

            // Reflexo do vidro.
            Image gloss = UiKit.Circle(dial, "Reflexo", new Color(1f, 1f, 1f, 0.045f));
            UiKit.Place(gloss.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 92f), new Vector2(268f, 118f));
        }

        private void BuildStrip()
        {
            RectTransform strip = UiKit.Panel(_root, "FaixaLuzes", 16, new Color(0.04f, 0.045f, 0.055f, 0.94f));
            UiKit.Place(strip, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(352f, 56f));

            Luz[] order = { Luz.SetaEsq, Luz.Injecao, Luz.Oleo, Luz.Temperatura, Luz.Bateria, Luz.Combustivel, Luz.Neutro, Luz.FarolAlto, Luz.SetaDir };

            for (int i = 0; i < order.Length; i++)
            {
                BuildLight(strip, order[i], 12f + i * 36.5f);
            }
        }

        private void BuildLight(RectTransform strip, Luz luz, float x)
        {
            Color onColor;

            switch (luz)
            {
                case Luz.Injecao:
                case Luz.Combustivel:
                    onColor = UiTema.Aviso;
                    break;
                case Luz.Neutro:
                case Luz.SetaEsq:
                case Luz.SetaDir:
                    onColor = UiTema.Sucesso;
                    break;
                case Luz.FarolAlto:
                    onColor = UiTema.Azul;
                    break;
                default:
                    onColor = UiTema.Perigo;
                    break;
            }

            RectTransform slot = UiKit.Rect("Luz_" + luz, strip);
            UiKit.Place(slot, new Vector2(0f, 1f), new Vector2(x, -7f), new Vector2(32f, 32f));
            var a = new Aviso { Cor = onColor };
            a.Glow = UiKit.Img(slot, "Brilho", UiSprites.Glow(), new Color(onColor.r, onColor.g, onColor.b, 0f), false);
            UiKit.Place(a.Glow.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(78f, 78f));
            a.Fundo = UiKit.Img(slot, "Fundo", UiSprites.Fill(9), new Color(1f, 1f, 1f, 0.05f), true);
            UiKit.Stretch(a.Fundo.rectTransform);

            if (luz == Luz.Neutro)
            {
                a.Letra = UiKit.Text(slot, "N", "N", 22f, new Color(1f, 1f, 1f, 0.2f), FontStyles.Bold, TextAlignmentOptions.Center);
                UiKit.Stretch(a.Letra.rectTransform);
            }
            else
            {
                a.Icone = UiKit.Gfx<UiShape>(slot, "Icone");
                UiKit.Stretch(a.Icone.rectTransform, 4f);
                a.Icone.SetPolygons(Polygons(luz));
                a.Icone.color = new Color(1f, 1f, 1f, 0.2f);
            }

            if (luz == Luz.Combustivel)
            {
                RectTransform bar = UiKit.Bar(strip, "NivelCombustivel", UiTema.Aviso, 4f, out _combustivelCor);
                UiKit.PlaceBar(bar, new Vector2(0f, 0f), new Vector2(x + 1f, 6f), new Vector2(30f, 4f));
                _combustivelBarra = _combustivelCor.rectTransform;
            }

            _luzes[(int)luz] = a;
        }

        private static IEnumerable<Vector2[]> Polygons(Luz luz)
        {
            var list = new List<Vector2[]>();

            switch (luz)
            {
                case Luz.Injecao:
                    list.Add(UiShape.Rect(0.22f, 0.28f, 0.74f, 0.64f));
                    list.Add(UiShape.Rect(0.34f, 0.64f, 0.62f, 0.78f));
                    list.Add(UiShape.Rect(0.08f, 0.40f, 0.22f, 0.56f));
                    list.Add(UiShape.Rect(0.74f, 0.36f, 0.92f, 0.58f));
                    list.Add(UiShape.Rect(0.28f, 0.16f, 0.40f, 0.28f));
                    list.Add(UiShape.Rect(0.56f, 0.16f, 0.68f, 0.28f));
                    break;
                case Luz.Oleo:
                    list.Add(UiShape.Rect(0.30f, 0.26f, 0.84f, 0.58f));
                    list.Add(UiShape.Rect(0.38f, 0.58f, 0.62f, 0.67f));
                    list.Add(new[] { new Vector2(0.30f, 0.46f), new Vector2(0.06f, 0.66f), new Vector2(0.14f, 0.72f), new Vector2(0.30f, 0.60f) });
                    list.Add(new[] { new Vector2(0.92f, 0.50f), new Vector2(1f, 0.34f), new Vector2(0.84f, 0.34f) });
                    break;
                case Luz.Temperatura:
                    list.Add(UiShape.Rect(0.22f, 0.30f, 0.38f, 0.90f));
                    list.Add(UiShape.Disc(0.30f, 0.24f, 0.17f, 0.17f));
                    list.Add(UiShape.Rect(0.50f, 0.74f, 0.92f, 0.82f));
                    list.Add(UiShape.Rect(0.50f, 0.54f, 0.92f, 0.62f));
                    list.Add(UiShape.Rect(0.50f, 0.34f, 0.92f, 0.42f));
                    break;
                case Luz.Bateria:
                    list.Add(UiShape.Rect(0.06f, 0.22f, 0.94f, 0.70f));
                    list.Add(UiShape.Rect(0.16f, 0.70f, 0.34f, 0.82f));
                    list.Add(UiShape.Rect(0.66f, 0.70f, 0.84f, 0.82f));
                    break;
                case Luz.Combustivel:
                    list.Add(UiShape.Rect(0.12f, 0.12f, 0.58f, 0.88f));
                    list.Add(UiShape.Rect(0.64f, 0.52f, 0.76f, 0.60f));
                    list.Add(UiShape.Rect(0.72f, 0.28f, 0.80f, 0.60f));
                    list.Add(UiShape.Rect(0.72f, 0.28f, 0.90f, 0.38f));
                    break;
                case Luz.FarolAlto:
                    {
                        var lamp = new List<Vector2>();

                        for (int d = 90; d <= 270; d += 15)
                        {
                            float r = d * Mathf.Deg2Rad;
                            lamp.Add(new Vector2(0.44f + Mathf.Cos(r) * 0.28f, 0.5f + Mathf.Sin(r) * 0.36f));
                        }

                        list.Add(lamp.ToArray());
                        list.Add(UiShape.Rect(0.62f, 0.76f, 0.96f, 0.82f));
                        list.Add(UiShape.Rect(0.62f, 0.58f, 0.96f, 0.64f));
                        list.Add(UiShape.Rect(0.62f, 0.40f, 0.96f, 0.46f));
                        list.Add(UiShape.Rect(0.62f, 0.22f, 0.96f, 0.28f));
                        break;
                    }
                case Luz.SetaEsq:
                    list.Add(new[]
                    {
                        new Vector2(0.06f, 0.5f), new Vector2(0.46f, 0.88f), new Vector2(0.46f, 0.66f), new Vector2(0.94f, 0.66f),
                        new Vector2(0.94f, 0.34f), new Vector2(0.46f, 0.34f), new Vector2(0.46f, 0.12f),
                    });
                    break;
                case Luz.SetaDir:
                    list.Add(new[]
                    {
                        new Vector2(0.94f, 0.5f), new Vector2(0.54f, 0.12f), new Vector2(0.54f, 0.34f), new Vector2(0.06f, 0.34f),
                        new Vector2(0.06f, 0.66f), new Vector2(0.54f, 0.66f), new Vector2(0.54f, 0.88f),
                    });
                    break;
            }

            return list;
        }

        private void BuildLimitSign()
        {
            _placa = UiKit.Rect("PlacaLimite", _root);
            UiKit.Place(_placa, new Vector2(0f, 0f), new Vector2(-96f, 78f), new Vector2(86f, 86f));
            _placaBrilho = UiKit.Img(_placa, "Brilho", UiSprites.Glow(), new Color(1f, 0.1f, 0.1f, 0f), false);
            UiKit.Stretch(_placaBrilho.rectTransform, -22f);
            UiKit.Img(_placa, "Sombra", UiSprites.Glow(), new Color(0f, 0f, 0f, 0.4f), false).rectTransform.SetStretch(-12f);
            _placaAro = UiKit.Circle(_placa, "Aro", new Color(0.86f, 0.09f, 0.12f, 1f));
            UiKit.Stretch(_placaAro.rectTransform);
            Image face = UiKit.Circle(_placa, "Face", new Color(0.98f, 0.98f, 0.98f, 1f));
            UiKit.Stretch(face.rectTransform, 10f);
            _placaTexto = UiKit.Text(face.transform, "Numero", "50", 34f, new Color(0.08f, 0.08f, 0.1f), FontStyles.Bold, TextAlignmentOptions.Center, -1f);
            UiKit.Stretch(_placaTexto.rectTransform);
            _placa.gameObject.SetActive(false);
        }

        private RectTransform BuildChip(string name, string text, Color color, float y)
        {
            RectTransform chip = UiKit.Panel(_root, name, 14, new Color(0.055f, 0.067f, 0.086f, 0.9f));
            chip.anchorMin = chip.anchorMax = new Vector2(0f, 0f);
            chip.pivot = new Vector2(1f, 0f);
            chip.anchoredPosition = new Vector2(-12f, y);
            chip.sizeDelta = new Vector2(252f, 38f);
            Image dot = UiKit.Circle(chip, "Ponto", color);
            UiKit.Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(13f, 0f), new Vector2(12f, 12f));
            TextMeshProUGUI t = UiKit.Caps(chip, "Texto", text, color, TextAlignmentOptions.Left, 14f);
            UiKit.Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(34f, 0f);
            chip.gameObject.SetActive(false);

            return chip;
        }

        private void BuildWarnings()
        {
            _chipContramao = BuildChip("AvisoContramao", "Contramão", UiTema.Perigo, 176f);
            _chipCorredor = BuildChip("AvisoCorredor", "Corredor de ônibus", UiTema.Aviso, 220f);

            // Sinal vermelho à frente: chip logo abaixo da placa de limite, e (com fotossensor) um chip pequeno com uma câmera ao lado.
            _chipSinal = BuildChip("AvisoSinal", "Sinal vermelho", UiTema.Perigo, 30f);
            _chipSinalTexto = _chipSinal.Find("Texto").GetComponent<TextMeshProUGUI>();
            _chipCamera = UiKit.Panel(_root, "AvisoCamera", 14, new Color(0.055f, 0.067f, 0.086f, 0.9f));
            _chipCamera.anchorMin = _chipCamera.anchorMax = new Vector2(0f, 0f);
            _chipCamera.pivot = new Vector2(1f, 0f);
            _chipCamera.anchoredPosition = new Vector2(-12f - 252f - 8f, 30f);
            _chipCamera.sizeDelta = new Vector2(46f, 38f);
            Image body = UiKit.Img(_chipCamera, "Corpo", UiSprites.Fill(4), UiTema.Aviso, true);
            UiKit.Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -1f), new Vector2(26f, 17f));
            Image bump = UiKit.Img(_chipCamera, "Visor", UiSprites.Fill(2), UiTema.Aviso, true);
            UiKit.Place(bump.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-6f, 9f), new Vector2(10f, 5f));
            Image lens = UiKit.Circle(_chipCamera, "Lente", new Color(0.055f, 0.067f, 0.086f, 1f));
            UiKit.Place(lens.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -1f), new Vector2(10f, 10f));
            _chipCamera.gameObject.SetActive(false);
        }

        /// <summary>Sinal vermelho à frente (até 60 m): mostra o chip com a distância e, se o cruzamento tem fotossensor, a câmera.</summary>
        public void SetSinal(bool vermelho, float metros, bool fotossensor)
        {
            _sinalVermelho = vermelho;
            _sinalCamera = vermelho && fotossensor;
            _sinalMetros = Mathf.RoundToInt(metros / 5f) * 5;
        }

        // ---------- Dados ----------

        private static float Angulo(float rpm) => StartDeg - Sweep * Mathf.Clamp01(rpm / RpmMaximo);

        public void SetLimiter(float limiterRpm)
        {
            _limitador = Mathf.Clamp(limiterRpm, 3000f, RpmMaximo);
            float start = _limitador - 1000f;

            if (Mathf.Approximately(start, _redStart))
            {
                return;
            }

            _redStart = start;
            _faixaVermelha.Set(Angulo(start), Angulo(RpmMaximo));

            for (int i = 0; i < _numeros.Count; i++)
            {
                _numeros[i].color = i * 1000f >= start ? UiTema.Perigo : UiTema.Texto;
            }
        }

        public void SetData(float rpm, int gear, float throttle, float brake)
        {
            _rpmAlvo = rpm;
            _marchaAtual = gear;
            _acel = Mathf.Clamp01(throttle);
            _freioValor = Mathf.Clamp01(brake);
        }

        /// <summary>Estado do câmbio: cortando giro (parado, freio e acelerador: giro livre, luz de neutro) e limitador cortando (pisca o aro e as luzes).</summary>
        public void SetTransmissao(bool cortandoGiro, bool noLimitador)
        {
            _cortandoGiro = cortandoGiro;
            _noLimitador = noLimitador;
        }

        public void SetSpeed(float kmh)
        {
            _speedKmh = Mathf.Max(0f, kmh);
        }

        public void SetLimit(int limitKmh)
        {
            _limiteVia = limitKmh;
        }

        public bool SinalVisivel => _chipSinal != null && _chipSinal.gameObject.activeSelf;

        public bool CameraVisivel => _chipCamera != null && _chipCamera.gameObject.activeSelf;

        public void SetWarnings(bool wrongWay, bool busLane)
        {
            _contramao = wrongWay;
            _corredor = busLane;
        }

        public void SetEstado(PainelMotoEstado estado)
        {
            _estado = estado;
        }

        public void SetVisible(bool visible)
        {
            _root.gameObject.SetActive(visible);
        }

        /// <summary>Anima agulha, números, luzes e avisos (tempo não escalado).</summary>
        public void Tick(float dt, float time)
        {
            _rpmMostrado = Mathf.SmoothDamp(_rpmMostrado, Mathf.Clamp(_rpmAlvo, 0f, RpmMaximo), ref _rpmVel, 0.07f, float.PositiveInfinity, dt);
            _speedMostrada = Mathf.Lerp(_speedMostrada, _speedKmh, 1f - Mathf.Exp(-14f * dt));
            _agulha.rectTransform.parent.localRotation = Quaternion.Euler(0f, 0f, Angulo(_rpmMostrado) - 90f);

            _acelerador.Set(225f, 225f - 133f * _acel);
            _freio.Set(-45f, -45f + 133f * _freioValor);

            int shown = Mathf.RoundToInt(_speedMostrada);
            _velocidade.text = shown.ToString();
            bool over = _limiteVia > 0 && _speedKmh > _limiteVia * 1.05f;
            _velocidade.color = over ? Color.Lerp(UiTema.Texto, UiTema.Perigo, 0.5f + 0.5f * Mathf.Sin(time * 10f)) : UiTema.Texto;
            bool neutral = _marchaAtual <= 0 || _cortandoGiro;
            _marcha.text = neutral ? "N" : _marchaAtual.ToString();
            _marcha.color = neutral ? UiTema.Sucesso : UiTema.Texto;

            // Shift light: acende da esquerda para a direita a partir de 70% do limitador; no limite pisca tudo.
            float frac = _limitador > 0f ? _rpmMostrado / _limitador : 0f;
            bool flash = frac >= 0.985f || _noLimitador;
            bool blink = Mathf.Repeat(time * 11f, 2f) < 1f;

            for (int i = 0; i < _leds.Length; i++)
            {
                Color on = i < 3 ? UiTema.Sucesso : i < 6 ? UiTema.Aviso : UiTema.Perigo;
                bool lit = flash ? blink : frac >= 0.70f + 0.3f * i / _leds.Length;
                _leds[i].color = lit ? (flash ? UiTema.Perigo : on) : new Color(1f, 1f, 1f, 0.12f);
            }

            float zone = Mathf.InverseLerp(_limitador - 700f, _limitador, _rpmMostrado);
            float pulse = flash ? (blink ? 0.55f : 0.15f) : zone * 0.22f;
            _brilhoAlerta.color = new Color(1f, 0.1f, 0.1f, pulse);
            _aroLimitador.color = new Color(1f, 0.2f, 0.2f, flash ? (blink ? 0.9f : 0.3f) : zone * 0.5f);
            _agulha.color = Color.Lerp(new Color(1f, 0.55f, 0.2f), new Color(1f, 0.2f, 0.2f), Mathf.Clamp01(zone * 1.5f));

            TickPlate(time, over);
            TickWarnings(time);
            TickLights(dt, time);
        }

        private void TickPlate(float time, bool over)
        {
            bool has = _limiteVia > 0;

            if (_placa.gameObject.activeSelf != has)
            {
                _placa.gameObject.SetActive(has);
            }

            if (!has)
            {
                return;
            }

            _placaTexto.text = _limiteVia.ToString();
            float p = over ? 0.5f + 0.5f * Mathf.Sin(time * 9f) : 0f;
            _placa.localScale = Vector3.one * (1f + 0.07f * p);
            _placaBrilho.color = new Color(1f, 0.1f, 0.1f, over ? 0.35f + 0.5f * p : 0f);
            _placaAro.color = over ? Color.Lerp(new Color(0.86f, 0.09f, 0.12f), new Color(1f, 0.35f, 0.3f), p) : new Color(0.86f, 0.09f, 0.12f);
        }

        private void TickWarnings(float time)
        {
            if (_chipContramao.gameObject.activeSelf != _contramao)
            {
                _chipContramao.gameObject.SetActive(_contramao);
            }

            if (_chipCorredor.gameObject.activeSelf != _corredor)
            {
                _chipCorredor.gameObject.SetActive(_corredor);
            }

            if (_chipSinal.gameObject.activeSelf != _sinalVermelho)
            {
                _chipSinal.gameObject.SetActive(_sinalVermelho);
            }

            if (_chipCamera.gameObject.activeSelf != _sinalCamera)
            {
                _chipCamera.gameObject.SetActive(_sinalCamera);
            }

            if (_sinalVermelho)
            {
                _chipSinalTexto.text = $"Sinal vermelho · {_sinalMetros} m";
            }

            float s = 1f + 0.04f * Mathf.Sin(time * 8f);
            _chipContramao.localScale = Vector3.one * s;
            _chipCorredor.localScale = Vector3.one * s;
        }

        private void TickLights(float dt, float time)
        {
            PainelMotoEstado e = _estado;
            bool blink = Mathf.Repeat(time * 2.6f, 1f) < 0.5f;
            Set(Luz.Injecao, e != null && e.Injecao, dt);
            Set(Luz.Oleo, e != null && e.Oleo, dt);
            Set(Luz.Temperatura, e != null && e.Temperatura, dt);
            Set(Luz.Bateria, e != null && e.Bateria, dt);
            Set(Luz.Combustivel, e != null && e.Reserva, dt);
            Set(Luz.Neutro, e != null && e.Neutro, dt);
            Set(Luz.FarolAlto, e != null && e.FarolAlto, dt);
            Set(Luz.SetaEsq, e != null && e.SetaEsquerda && blink, dt);
            Set(Luz.SetaDir, e != null && e.SetaDireita && blink, dt);

            if (_combustivelBarra != null && e != null)
            {
                UiKit.SetBar(_combustivelBarra, e.Combustivel);
                _combustivelCor.color = e.Combustivel <= PainelMotoEstado.LimiteReserva ? UiTema.Perigo : UiTema.Aviso;
            }
        }

        private void Set(Luz luz, bool on, float dt)
        {
            Aviso a = _luzes[(int)luz];
            a.Nivel = Mathf.MoveTowards(a.Nivel, on ? 1f : 0f, dt * 12f);
            float n = a.Nivel;
            Color off = new Color(1f, 1f, 1f, 0.2f);
            Color c = Color.Lerp(off, a.Cor, n);

            if (a.Icone != null)
            {
                a.Icone.color = c;
            }

            if (a.Letra != null)
            {
                a.Letra.color = c;
            }

            a.Fundo.color = Color.Lerp(new Color(1f, 1f, 1f, 0.05f), new Color(a.Cor.r, a.Cor.g, a.Cor.b, 0.2f), n);
            a.Glow.color = new Color(a.Cor.r, a.Cor.g, a.Cor.b, 0.5f * n);
        }
    }

    internal static class RectExtensions
    {
        public static void SetStretch(this RectTransform rect, float pad)
        {
            UiKit.Stretch(rect, pad);
        }
    }
}
