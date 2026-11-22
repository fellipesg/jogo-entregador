using System;
using System.Collections.Generic;
using Entregador.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Entregador.Game
{
    /// <summary>
    /// Interface do rádio: o app "Rádio" do celular (lista de estações com ícone, busca, chips de gênero, favoritos, "Desligar rádio" e a barra
    /// "tocando agora") e a pílula discreta do HUD, no canto de baixo à esquerda, com anterior e próxima para o toque.
    /// Tudo lê e comanda o <see cref="RadioPlayer"/> (singleton); a tela só desenha.
    /// </summary>
    public sealed partial class GameUI
    {
        private const string ChipTodas = "Todas";
        private const string ChipFavoritas = "Favoritas";

        /// <summary>Ícone de uma estação: círculo com o favicon (baixado de forma assíncrona) ou, sem ele, a inicial sobre a cor da estação.</summary>
        private sealed class IconeEstacao
        {
            public RectTransform Raiz;
            public Image Fundo;
            public RawImage Imagem;
            public AspectRatioFitter Ajuste;
            public TextMeshProUGUI Letra;
            public RadioEstacao Estacao;

            public static IconeEstacao Criar(Transform parent, float tamanho)
            {
                var ic = new IconeEstacao();
                ic.Raiz = UiKit.Rect("Icone", parent);
                ic.Raiz.sizeDelta = new Vector2(tamanho, tamanho);
                ic.Fundo = UiKit.Circle(ic.Raiz, "Fundo", Color.gray);
                UiKit.Stretch(ic.Fundo.rectTransform);
                ic.Fundo.gameObject.AddComponent<Mask>().showMaskGraphic = true;

                RectTransform box = UiKit.Rect("Imagem", ic.Fundo.transform);
                UiKit.Stretch(box, tamanho * 0.14f);
                var go = new GameObject("Favicon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
                go.transform.SetParent(box, false);
                ic.Imagem = go.GetComponent<RawImage>();
                ic.Imagem.raycastTarget = false;
                ic.Ajuste = go.GetComponent<AspectRatioFitter>();
                ic.Ajuste.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                ic.Imagem.gameObject.SetActive(false);

                ic.Letra = UiKit.Text(ic.Raiz, "Letra", "R", tamanho * 0.46f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
                UiKit.Stretch(ic.Letra.rectTransform);

                return ic;
            }

            /// <summary>Mostra a inicial na hora e troca pelo favicon quando ele chegar (se esta ainda for a estação do ícone).</summary>
            public void Definir(RadioEstacao e, MonoBehaviour host)
            {
                Estacao = e;
                Imagem.gameObject.SetActive(false);
                Imagem.texture = null;
                Letra.gameObject.SetActive(true);
                Letra.text = Inicial(e);
                Fundo.color = e != null ? e.Cor : UiTema.SuperficieForte;

                if (e == null || !e.EhStream || string.IsNullOrEmpty(e.Info.Favicon))
                {
                    return;
                }

                RadioFavicons.Pedir(host, e.Info.Favicon, tex =>
                {
                    if (tex == null || Estacao != e || Raiz == null)
                    {
                        return;
                    }

                    Imagem.texture = tex;
                    Ajuste.aspectRatio = tex.width / (float)tex.height;
                    Imagem.gameObject.SetActive(true);
                    Letra.gameObject.SetActive(false);
                    Fundo.color = new Color(0.95f, 0.96f, 0.98f, 1f);
                });
            }

            private static string Inicial(RadioEstacao e)
            {
                if (e == null)
                {
                    return "R";
                }

                string name = e.Nome;

                foreach (string prefix in new[] { "Rádio ", "Radio ", "Super Rádio " })
                {
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
                    {
                        name = name.Substring(prefix.Length);

                        break;
                    }
                }

                foreach (char c in name)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        return char.ToUpperInvariant(c).ToString();
                    }
                }

                return "R";
            }
        }

        private sealed class LinhaRadio
        {
            public RectTransform Raiz;
            public Button Botao;
            public Image Fundo;
            public Image Contorno;
            public IconeEstacao Icone;
            public TextMeshProUGUI Nome;
            public TextMeshProUGUI Sub;
            public TextMeshProUGUI Tag;
            public Button Estrela;
            public UiShape EstrelaForma;
            public RadioEstacao Estacao;
        }

        // App do celular.
        private RectTransform _radioRoot;
        private TMP_InputField _radioBusca;
        private ScrollRect _radioLista;
        private RectTransform _radioConteudo;
        private TextMeshProUGUI _radioNota;
        private TextMeshProUGUI _radioVazio;
        private Button _radioDesligar;
        private Button _radioVoltar;
        private readonly List<LinhaRadio> _radioLinhas = new List<LinhaRadio>();
        private readonly List<KeyValuePair<string, Image>> _radioChips = new List<KeyValuePair<string, Image>>();
        private readonly List<TextMeshProUGUI> _radioChipRotulos = new List<TextMeshProUGUI>();
        private string _radioGenero = ChipTodas;
        private string _radioTextoBusca = string.Empty;
        private bool _radioHooked;

        // Barra "tocando agora" do app.
        private IconeEstacao _barraIcone;
        private TextMeshProUGUI _barraNome;
        private TextMeshProUGUI _barraStatus;

        // Pílula do HUD.
        private RectTransform _pilulaRoot;
        private CanvasGroup _pilulaGrupo;
        private IconeEstacao _pilulaIcone;
        private TextMeshProUGUI _pilulaNome;
        private TextMeshProUGUI _pilulaStatus;
        private string _pilulaChave = "?";
        private Button _pilulaAnterior;
        private Button _pilulaProxima;

        /// <summary>O jogador está digitando num campo de texto: as teclas do jogo (R do rádio, M do celular) não valem.</summary>
        public static bool DigitandoTexto
        {
            get
            {
                EventSystem es = EventSystem.current;

                if (es == null || es.currentSelectedGameObject == null)
                {
                    return false;
                }

                TMP_InputField input = es.currentSelectedGameObject.GetComponentInParent<TMP_InputField>();

                return input != null && input.isFocused;
            }
        }

        public bool RadioAppVisivel => _radioRoot != null && _radioRoot.gameObject.activeInHierarchy;

        /// <summary>Quantas estações a lista do app mostra agora (testes).</summary>
        public int RadioAppLinhasVisiveis
        {
            get
            {
                int n = 0;

                foreach (LinhaRadio l in _radioLinhas)
                {
                    if (l.Raiz.gameObject.activeSelf)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public string RadioPilulaTexto => _pilulaNome != null ? _pilulaNome.text : string.Empty;

        public string RadioPilulaStatus => _pilulaStatus != null ? _pilulaStatus.text : string.Empty;

        /// <summary>Clica na linha visível de índice i do app (testes e automação).</summary>
        public bool ClickRadioLinha(int visivel)
        {
            int n = 0;

            foreach (LinhaRadio l in _radioLinhas)
            {
                if (!l.Raiz.gameObject.activeSelf)
                {
                    continue;
                }

                if (n++ == visivel)
                {
                    l.Botao.onClick.Invoke();

                    return true;
                }
            }

            return false;
        }

        public bool ClickRadioDesligar()
        {
            if (!_radioDesligar.interactable)
            {
                return false;
            }

            _radioDesligar.onClick.Invoke();

            return true;
        }

        public void ClickRadioPilula(bool proxima)
        {
            (proxima ? _pilulaProxima : _pilulaAnterior).onClick.Invoke();
        }

        // ---------- Montagem ----------

        private static Button BotaoRedondo(Transform parent, string name, List<Vector2[]> forma, float tamanho, Color fundo)
        {
            RectTransform root = UiKit.Rect(name, parent);
            root.sizeDelta = new Vector2(tamanho, tamanho);
            Image circle = UiKit.Circle(root, "Fundo", fundo);
            UiKit.Stretch(circle.rectTransform);
            circle.raycastTarget = true;
            UiShape icon = UiKit.Gfx<UiShape>(root, "Icone");
            UiKit.Stretch(icon.rectTransform, tamanho * 0.2f);
            icon.SetPolygons(forma);
            icon.color = UiTema.Texto;
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = circle;
            button.transition = Selectable.Transition.None;
            var fx = root.gameObject.AddComponent<UiButtonFx>();
            fx.Target = circle;
            fx.Normal = fundo;
            fx.Hover = Color.Lerp(fundo, Color.white, 0.18f);
            fx.Pressed = Color.Lerp(fundo, Color.black, 0.3f);
            fx.Disabled = fundo;

            return button;
        }

        private void BuildRadioApp(Transform parent)
        {
            _radioRoot = UiKit.Rect("AppRadio", parent);
            UiKit.Stretch(_radioRoot);
            Image dim = UiKit.Img(_radioRoot, "Fundo", UiSprites.Fill(1), new Color(0f, 0f, 0f, 0.6f), false);
            UiKit.Stretch(dim.rectTransform);

            RectTransform card = UiKit.Panel(_radioRoot, "Cartao", UiTema.RaioTelefone, UiTema.VidroForte);
            UiKit.Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 980f));

            TextMeshProUGUI title = UiKit.Text(card, "Titulo", "Rádio", 34f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(300f, 44f));
            _radioNota = UiKit.Caps(card, "Nota", "Rádio pela internet · usa dados móveis", UiTema.TextoFraco, TextAlignmentOptions.Left, 13f);
            UiKit.Place(_radioNota.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -80f), new Vector2(700f, 18f));

            _radioDesligar = UiKit.Pill(card, "Desligar", "Desligar rádio", UiBotaoEstilo.Secundario, 48f, 17f);
            UiKit.Place((RectTransform)_radioDesligar.transform, new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(230f, 48f));
            _radioDesligar.onClick.AddListener(() => RadioPlayer.Instance?.Desligar());

            // Busca.
            RectTransform field = UiKit.Panel(card, "Busca", 28, UiTema.Superficie, false, true);
            UiKit.Place(field, new Vector2(0f, 1f), new Vector2(40f, -112f), new Vector2(1020f, 56f));
            field.Find("Fundo").GetComponent<Image>().raycastTarget = true;
            RectTransform viewport = UiKit.Rect("Texto", field);
            UiKit.Stretch(viewport);
            viewport.offsetMin = new Vector2(26f, 6f);
            viewport.offsetMax = new Vector2(-26f, -6f);
            viewport.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI typed = UiKit.Text(viewport, "Digitado", string.Empty, 21f, UiTema.Texto, FontStyles.Normal, TextAlignmentOptions.Left);
            UiKit.Stretch(typed.rectTransform);
            TextMeshProUGUI hint = UiKit.Text(viewport, "Dica", "Buscar rádio, gênero ou cidade", 21f, UiTema.TextoFraco, FontStyles.Italic, TextAlignmentOptions.Left);
            UiKit.Stretch(hint.rectTransform);
            _radioBusca = field.gameObject.AddComponent<TMP_InputField>();
            _radioBusca.textViewport = viewport;
            _radioBusca.textComponent = typed;
            _radioBusca.placeholder = hint;
            _radioBusca.targetGraphic = field.Find("Fundo").GetComponent<Image>();
            _radioBusca.lineType = TMP_InputField.LineType.SingleLine;
            _radioBusca.caretColor = UiTema.Texto;
            _radioBusca.selectionColor = UiTema.Alfa(UiTema.Azul, 0.4f);
            _radioBusca.characterLimit = 40;
            _radioBusca.onValueChanged.AddListener(OnRadioBusca);

            BuildRadioChips(card);

            // Lista com rolagem.
            RectTransform listaRaiz = UiKit.Rect("Lista", card);
            UiKit.Place(listaRaiz, new Vector2(0f, 1f), new Vector2(40f, -246f), new Vector2(1020f, 530f));
            var raycast = listaRaiz.gameObject.AddComponent<Image>();
            raycast.color = new Color(1f, 1f, 1f, 0f);
            raycast.raycastTarget = true;
            RectTransform vp = UiKit.Rect("Janela", listaRaiz);
            UiKit.Stretch(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            _radioConteudo = UiKit.Rect("Conteudo", vp);
            _radioConteudo.anchorMin = new Vector2(0f, 1f);
            _radioConteudo.anchorMax = new Vector2(1f, 1f);
            _radioConteudo.pivot = new Vector2(0.5f, 1f);
            _radioConteudo.offsetMin = _radioConteudo.offsetMax = Vector2.zero;
            var layout = _radioConteudo.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.padding = new RectOffset(0, 0, 0, 8);
            _radioConteudo.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _radioLista = listaRaiz.gameObject.AddComponent<ScrollRect>();
            _radioLista.viewport = vp;
            _radioLista.content = _radioConteudo;
            _radioLista.horizontal = false;
            _radioLista.vertical = true;
            _radioLista.movementType = ScrollRect.MovementType.Elastic;
            _radioLista.scrollSensitivity = 45f;
            _radioLista.inertia = true;

            _radioVazio = UiKit.Text(listaRaiz, "Vazio", "Nenhuma rádio encontrada", 22f, UiTema.TextoFraco, FontStyles.Italic, TextAlignmentOptions.Center);
            UiKit.Place(_radioVazio.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 40f));
            _radioVazio.gameObject.SetActive(false);

            // Barra "tocando agora".
            RectTransform barra = UiKit.Panel(card, "Tocando", 24, new Color(1f, 1f, 1f, 0.07f), false, true);
            UiKit.Place(barra, new Vector2(0.5f, 0f), new Vector2(0f, 104f), new Vector2(1020f, 84f));
            _barraIcone = IconeEstacao.Criar(barra, 56f);
            UiKit.Place(_barraIcone.Raiz, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(56f, 56f));
            _barraNome = UiKit.Text(barra, "Nome", string.Empty, 23f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(_barraNome.rectTransform, new Vector2(0f, 1f), new Vector2(88f, -14f), new Vector2(640f, 30f));
            _barraNome.overflowMode = TextOverflowModes.Ellipsis;
            _barraStatus = UiKit.Text(barra, "Status", string.Empty, 16f, UiTema.TextoSuave, FontStyles.Normal, TextAlignmentOptions.Left);
            UiKit.Place(_barraStatus.rectTransform, new Vector2(0f, 1f), new Vector2(88f, -48f), new Vector2(640f, 24f));
            _barraStatus.overflowMode = TextOverflowModes.Ellipsis;

            Button prev = BotaoRedondo(barra, "Anterior", UiIcones.Anterior(), 56f, UiTema.SuperficieForte);
            UiKit.Place((RectTransform)prev.transform, new Vector2(1f, 0.5f), new Vector2(-84f, 0f), new Vector2(56f, 56f));
            prev.onClick.AddListener(() => RadioPlayer.Instance?.Anterior());
            Button next = BotaoRedondo(barra, "Proxima", UiIcones.Proxima(), 56f, UiTema.SuperficieForte);
            UiKit.Place((RectTransform)next.transform, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(56f, 56f));
            next.onClick.AddListener(() => RadioPlayer.Instance?.Proxima());

            _radioVoltar = UiKit.Pill(card, "Voltar", "Voltar", UiBotaoEstilo.Primario, 56f, 20f);
            UiKit.Place((RectTransform)_radioVoltar.transform, new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(300f, 56f));

            _radioRoot.gameObject.SetActive(false);
        }

        private void BuildRadioChips(Transform card)
        {
            RectTransform raiz = UiKit.Rect("Generos", card);
            UiKit.Place(raiz, new Vector2(0f, 1f), new Vector2(40f, -184f), new Vector2(1020f, 46f));
            RectTransform vp = UiKit.Rect("Janela", raiz);
            UiKit.Stretch(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            var back = vp.gameObject.AddComponent<Image>();
            back.color = new Color(1f, 1f, 1f, 0f);
            back.raycastTarget = true;
            RectTransform content = UiKit.Rect("Conteudo", vp);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var row = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = raiz.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = vp;
            scroll.content = content;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            var names = new List<string> { ChipTodas, ChipFavoritas };
            names.AddRange(RadioDirectory.Generos);

            foreach (string g in names)
            {
                string genero = g;
                RectTransform chip = UiKit.Rect("Chip_" + g, content);
                Image fill = UiKit.Img(chip, "Fundo", UiSprites.Fill(UiTema.RaioChip + 8), UiTema.Superficie, true);
                UiKit.Stretch(fill.rectTransform);
                fill.raycastTarget = true;
                TextMeshProUGUI label = UiKit.Text(chip, "Rotulo", g, 19f, UiTema.TextoSuave, FontStyles.Bold, TextAlignmentOptions.Center);
                UiKit.Stretch(label.rectTransform);
                var le = chip.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 44f + g.Length * 10.5f;
                le.preferredHeight = 46f;
                var button = chip.gameObject.AddComponent<Button>();
                button.targetGraphic = fill;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SelecionarGeneroRadio(genero));
                _radioChips.Add(new KeyValuePair<string, Image>(g, fill));
                _radioChipRotulos.Add(label);
            }
        }

        private LinhaRadio CriarLinhaRadio()
        {
            var l = new LinhaRadio();
            l.Raiz = UiKit.Rect("Estacao", _radioConteudo);
            l.Raiz.gameObject.AddComponent<LayoutElement>().preferredHeight = 92f;
            l.Fundo = UiKit.Img(l.Raiz, "Fundo", UiSprites.Fill(UiTema.RaioCartao), UiTema.Superficie, true);
            UiKit.Stretch(l.Fundo.rectTransform);
            l.Fundo.raycastTarget = true;
            l.Contorno = UiKit.Img(l.Raiz, "Borda", UiSprites.Outline(UiTema.RaioCartao), UiTema.Alfa(UiTema.Destaque, 0f), true);
            UiKit.Stretch(l.Contorno.rectTransform);
            l.Icone = IconeEstacao.Criar(l.Raiz, 64f);
            UiKit.Place(l.Icone.Raiz, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(64f, 64f));
            l.Nome = UiKit.Text(l.Raiz, "Nome", string.Empty, 23f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(l.Nome.rectTransform, new Vector2(0f, 1f), new Vector2(98f, -16f), new Vector2(620f, 30f));
            l.Nome.overflowMode = TextOverflowModes.Ellipsis;
            l.Sub = UiKit.Text(l.Raiz, "Sub", string.Empty, 16f, UiTema.TextoSuave, FontStyles.Normal, TextAlignmentOptions.Left);
            UiKit.Place(l.Sub.rectTransform, new Vector2(0f, 1f), new Vector2(98f, -52f), new Vector2(620f, 24f));
            l.Sub.overflowMode = TextOverflowModes.Ellipsis;
            l.Tag = UiKit.Caps(l.Raiz, "Tag", string.Empty, UiTema.Destaque, TextAlignmentOptions.Right, 12f);
            UiKit.Place(l.Tag.rectTransform, new Vector2(1f, 0.5f), new Vector2(-96f, 0f), new Vector2(220f, 20f));

            l.Estrela = UiKit.Pill(l.Raiz, "Favorita", string.Empty, UiBotaoEstilo.Secundario, 56f, 12f);
            UiKit.Place((RectTransform)l.Estrela.transform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(56f, 56f));
            ((RectTransform)l.Estrela.transform.Find("Fundo")).GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.0f);
            l.EstrelaForma = UiKit.Star(l.Estrela.transform, "Estrela", 36f, UiTema.TextoFraco);
            UiKit.Place(l.EstrelaForma.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36f, 36f));
            var fx = l.Estrela.GetComponent<UiButtonFx>();
            fx.Normal = new Color(1f, 1f, 1f, 0f);
            fx.Hover = new Color(1f, 1f, 1f, 0.08f);
            fx.Pressed = new Color(1f, 1f, 1f, 0.14f);
            fx.Disabled = fx.Normal;

            l.Botao = l.Raiz.gameObject.AddComponent<Button>();
            l.Botao.targetGraphic = l.Fundo;
            l.Botao.transition = Selectable.Transition.None;
            LinhaRadio capture = l;
            l.Botao.onClick.AddListener(() => RadioPlayer.Instance?.Sintonizar(capture.Estacao));
            l.Estrela.onClick.AddListener(() =>
            {
                RadioPlayer.Instance?.AlternarFavorito(capture.Estacao);
            });

            return l;
        }

        // ---------- Dados ----------

        private static string GeneroDe(RadioEstacao e)
        {
            return e.EhStream && !string.IsNullOrEmpty(e.Info.Genero) ? e.Info.Genero : RadioDirectory.Classificar(e.Genero, e.Nome);
        }

        private static string DetalheDe(RadioEstacao e)
        {
            string s = GeneroDe(e) + " · " + e.Origem;

            if (e.EhStream)
            {
                string codec = e.Info.Hls ? "HLS" : e.Info.Codec.Replace("UNKNOWN", string.Empty);

                if (!string.IsNullOrEmpty(codec))
                {
                    s += " · " + codec + (e.Info.Bitrate > 0 && !e.Info.Hls ? " " + e.Info.Bitrate + " kbps" : string.Empty);
                }
            }

            return s;
        }

        private bool Combina(RadioPlayer radio, RadioEstacao e)
        {
            if (_radioGenero == ChipFavoritas)
            {
                if (!radio.EhFavorito(e))
                {
                    return false;
                }
            }
            else if (_radioGenero != ChipTodas && GeneroDe(e) != _radioGenero)
            {
                return false;
            }

            string q = RadioDirectory.Sem(_radioTextoBusca).Trim();

            if (q.Length == 0)
            {
                return true;
            }

            string hay = e.Nome + " " + e.Genero + " " + GeneroDe(e) + (e.EhStream ? " " + e.Info.Tags + " " + e.Info.Cidade : " radio do jogo");

            return RadioDirectory.Sem(hay).IndexOf(q, StringComparison.Ordinal) >= 0;
        }

        /// <summary>Redesenha lista, chips e barra com o estado atual do rádio.</summary>
        private void AtualizarAppRadio(bool voltarAoTopo)
        {
            if (_radioRoot == null || !_radioRoot.gameObject.activeInHierarchy)
            {
                return;
            }

            RadioPlayer radio = RadioPlayer.Instance;

            for (int i = 0; i < _radioChips.Count; i++)
            {
                bool sel = _radioChips[i].Key == _radioGenero;
                _radioChips[i].Value.color = sel ? UiTema.Destaque : UiTema.Superficie;
                _radioChipRotulos[i].color = sel ? Color.white : UiTema.TextoSuave;
            }

            int used = 0;

            if (radio != null)
            {
                RadioEstacao atual = radio.EstacaoAtual;

                foreach (RadioEstacao e in radio.Estacoes)
                {
                    if (!Combina(radio, e))
                    {
                        continue;
                    }

                    if (used >= _radioLinhas.Count)
                    {
                        _radioLinhas.Add(CriarLinhaRadio());
                    }

                    LinhaRadio l = _radioLinhas[used++];
                    l.Raiz.gameObject.SetActive(true);

                    if (l.Estacao != e)
                    {
                        l.Estacao = e;
                        l.Icone.Definir(e, this);
                    }

                    l.Nome.text = e.Nome;
                    l.Sub.text = DetalheDe(e);
                    bool tocando = e == atual;
                    l.Fundo.color = tocando ? UiTema.Alfa(UiTema.Destaque, 0.20f) : UiTema.Superficie;
                    l.Contorno.color = UiTema.Alfa(UiTema.Destaque, tocando ? 0.9f : 0f);
                    l.Tag.text = tocando ? (radio.Sintonizando ? "Sintonizando…" : "No ar") : string.Empty;
                    l.EstrelaForma.color = radio.EhFavorito(e) ? UiTema.Aviso : UiTema.TextoFraco;
                }
            }

            for (int i = used; i < _radioLinhas.Count; i++)
            {
                _radioLinhas[i].Raiz.gameObject.SetActive(false);
            }

            _radioVazio.gameObject.SetActive(used == 0);
            _radioVazio.text = _radioGenero == ChipFavoritas && string.IsNullOrEmpty(_radioTextoBusca) ? "Toque na estrela de uma rádio para favoritar" : "Nenhuma rádio encontrada";

            if (voltarAoTopo && _radioLista != null)
            {
                _radioLista.verticalNormalizedPosition = 1f;
            }

            string nota = "Rádio pela internet · usa dados móveis";

            if (radio != null && !string.IsNullOrEmpty(radio.StatusLista))
            {
                nota += " · " + radio.StatusLista;
            }

            if (!RadioPlayer.InternetAtiva)
            {
                nota = RadioPlayer.MusicaExterna ? "Música de outro app ligada: o rádio do jogo fica desligado" : "Rádio pela internet desligado nas Opções";
            }

            _radioNota.text = nota;
            AtualizarBarraRadio();
        }

        private void AtualizarBarraRadio()
        {
            RadioPlayer radio = RadioPlayer.Instance;
            RadioEstacao e = radio != null ? radio.EstacaoAtual : null;

            if (_barraIcone.Estacao != e || e == null)
            {
                _barraIcone.Definir(e, this);
            }

            _barraNome.text = e != null ? e.Nome : "Rádio desligado";
            _barraStatus.text = radio != null ? radio.Status : string.Empty;
            _radioDesligar.interactable = e != null;
        }

        private void OnRadioBusca(string texto)
        {
            _radioTextoBusca = texto ?? string.Empty;
            AtualizarAppRadio(true);
        }

        private void SelecionarGeneroRadio(string genero)
        {
            _radioGenero = genero;
            AtualizarAppRadio(true);
        }

        private void OnRadioMudou()
        {
            AtualizarAppRadio(false);
        }

        private void OnRadioEstacao(int index, RadioEstacao e)
        {
            AtualizarAppRadio(false);
        }

        // ---------- API ----------

        /// <summary>Abre o app "Rádio" do celular. onClose volta ao menu do celular.</summary>
        public void ShowRadioApp(Action onClose)
        {
            _radioVoltar.onClick.RemoveAllListeners();
            _radioVoltar.onClick.AddListener(() => onClose?.Invoke());
            ShowScreen(_radioRoot, "Rádio", string.Empty, new[] { new PanelButton("Voltar", onClose) });
            RadioPlayer radio = RadioPlayer.Instance;

            if (radio != null && !_radioHooked)
            {
                _radioHooked = true;
                radio.ListaMudou += OnRadioMudou;
                radio.EstacaoMudou += OnRadioEstacao;
                radio.Stream.EstadoMudou += s => OnRadioMudou();
            }

            _radioBusca.SetTextWithoutNotify(string.Empty);
            _radioTextoBusca = string.Empty;
            _radioGenero = ChipTodas;
            AtualizarAppRadio(true);
        }

        /// <summary>Define o texto de busca do app (testes e automação).</summary>
        public void SetRadioBusca(string texto)
        {
            _radioBusca.text = texto;
        }

        public void SetRadioGenero(string genero)
        {
            SelecionarGeneroRadio(genero);
        }

        // ---------- Pílula do HUD ----------

        private void BuildRadioPilula(Transform parent)
        {
            _pilulaRoot = UiKit.Panel(parent, "PilulaRadio", 24, UiTema.VidroForte);
            UiKit.Place(_pilulaRoot, new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(420f, 68f));
            _pilulaGrupo = _pilulaRoot.gameObject.AddComponent<CanvasGroup>();

            Button prev = BotaoRedondo(_pilulaRoot, "Anterior", UiIcones.Anterior(), 46f, UiTema.SuperficieForte);
            UiKit.Place((RectTransform)prev.transform, new Vector2(0f, 0.5f), new Vector2(11f, 0f), new Vector2(46f, 46f));
            prev.onClick.AddListener(() => RadioPlayer.Instance?.Anterior());
            _pilulaAnterior = prev;

            _pilulaIcone = IconeEstacao.Criar(_pilulaRoot, 44f);
            UiKit.Place(_pilulaIcone.Raiz, new Vector2(0f, 0.5f), new Vector2(66f, 0f), new Vector2(44f, 44f));

            _pilulaNome = UiKit.Text(_pilulaRoot, "Nome", string.Empty, 19f, UiTema.Texto, FontStyles.Bold, TextAlignmentOptions.Left);
            UiKit.Place(_pilulaNome.rectTransform, new Vector2(0f, 1f), new Vector2(120f, -12f), new Vector2(240f, 26f));
            _pilulaNome.overflowMode = TextOverflowModes.Ellipsis;
            _pilulaStatus = UiKit.Caps(_pilulaRoot, "Status", string.Empty, UiTema.TextoFraco, TextAlignmentOptions.Left, 11f);
            UiKit.Place(_pilulaStatus.rectTransform, new Vector2(0f, 1f), new Vector2(120f, -40f), new Vector2(240f, 16f));
            _pilulaStatus.overflowMode = TextOverflowModes.Ellipsis;

            Button next = BotaoRedondo(_pilulaRoot, "Proxima", UiIcones.Proxima(), 46f, UiTema.SuperficieForte);
            UiKit.Place((RectTransform)next.transform, new Vector2(1f, 0.5f), new Vector2(-11f, 0f), new Vector2(46f, 46f));
            next.onClick.AddListener(() => RadioPlayer.Instance?.Proxima());
            _pilulaProxima = next;
        }

        private void TickRadioPilula()
        {
            if (_pilulaRoot == null || !_pilulaRoot.gameObject.activeInHierarchy)
            {
                return;
            }

            RadioPlayer radio = RadioPlayer.Instance;
            RadioEstacao e = radio != null ? radio.EstacaoAtual : null;
            string status = radio != null ? radio.Status : string.Empty;
            string chave = (e != null ? e.Chave : "-") + "|" + status;

            if (chave == _pilulaChave)
            {
                return;
            }

            _pilulaChave = chave;

            if (_pilulaIcone.Estacao != e || e == null)
            {
                _pilulaIcone.Definir(e, this);
            }

            _pilulaNome.text = e != null ? e.Nome : "Rádio desligado";
            _pilulaStatus.text = e != null ? status : "Toque em ▶ para ligar";
            _pilulaGrupo.alpha = e != null ? 1f : 0.7f;
        }
    }
}
