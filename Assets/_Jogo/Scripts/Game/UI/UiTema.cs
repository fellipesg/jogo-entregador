using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Tokens visuais da interface: cores, espaçamentos, raios de canto e tamanhos de texto (referência 1920x1080).</summary>
    public static class UiTema
    {
        // Vidro escuro (#0E1116 a 80-94% de opacidade).
        public static readonly Color Vidro = new Color(0.055f, 0.067f, 0.086f, 0.84f);
        public static readonly Color VidroForte = new Color(0.055f, 0.067f, 0.086f, 0.94f);
        public static readonly Color Superficie = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color SuperficieForte = new Color(1f, 1f, 1f, 0.11f);
        public static readonly Color Borda = new Color(1f, 1f, 1f, 0.12f);

        /// <summary>Cor do app de entrega (fictício): vermelho-coral.</summary>
        public static readonly Color Destaque = new Color(0.92f, 0.22f, 0.27f, 1f);
        public static readonly Color DestaqueEscuro = new Color(0.66f, 0.13f, 0.18f, 1f);
        public static readonly Color Sucesso = new Color(0.22f, 0.82f, 0.50f, 1f);
        public static readonly Color Aviso = new Color(1f, 0.69f, 0.13f, 1f);
        public static readonly Color Perigo = new Color(1f, 0.30f, 0.32f, 1f);
        public static readonly Color Azul = new Color(0.30f, 0.62f, 1f, 1f);

        public static readonly Color Texto = new Color(0.95f, 0.96f, 0.98f, 1f);
        public static readonly Color TextoSuave = new Color(0.66f, 0.70f, 0.76f, 1f);
        public static readonly Color TextoFraco = new Color(0.45f, 0.49f, 0.55f, 1f);

        public const string NomeApp = "Entrega Já";

        // Espaçamento.
        public const float Esp1 = 4f;
        public const float Esp2 = 8f;
        public const float Esp3 = 12f;
        public const float Esp4 = 16f;
        public const float Esp5 = 24f;
        public const float Esp6 = 32f;

        // Cantos.
        public const int RaioChip = 14;
        public const int RaioCartao = 18;
        public const int RaioTelefone = 34;
        public const int RaioPilula = 26;

        // Textos (px na referência 1920x1080).
        public const float TamMicro = 13f;
        public const float TamPequeno = 16f;
        public const float TamCorpo = 21f;
        public const float TamSubtitulo = 24f;
        public const float TamTitulo = 30f;
        public const float TamNumero = 28f;

        public static Color Alfa(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
