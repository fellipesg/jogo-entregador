using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Modelos de moto jogáveis (nomes fictícios; inspirados nas motos mais comuns da entrega no Brasil).</summary>
    public enum MotoModelo
    {
        /// <summary>"Titã 160": a street clássica de entrega (farol redondo, tanque reto, banco longo, guidão alto).</summary>
        Titan,

        /// <summary>"Tornado 250": naked esportiva (carenagem pequena angular, tanque esculpido com abas, banco escalonado).</summary>
        Twister,
    }

    /// <summary>Cor da pintura (tanque, laterais e rabeta).</summary>
    public enum MotoCor
    {
        Vermelha,
        Preta,
        Azul,
        Branca,
    }

    /// <summary>Os cinco personagens jogáveis. A tela de seleção (rodada futura) só precisa chamar MotoVisual.Configurar.</summary>
    public enum Personagem
    {
        Jefinho,
        Robsao,
        Marcao,
        SeuValdir,
        Camila,
    }

    /// <summary>
    /// Parâmetros de um personagem: o corpo vem do modelo do Mixamo (ver PersonagensCatalogo); aqui ficam o tamanho,
    /// a postura na moto e as cores do capacete.
    /// </summary>
    public sealed class PersonagemConfig
    {
        public string Nome;

        /// <summary>Escala do modelo do Mixamo (1 = como veio, cerca de 1,75 m).</summary>
        public float Escala = 1f;

        /// <summary>Inclinação do tronco para frente, em graus.</summary>
        public float Inclinacao = 14f;

        /// <summary>Quanto a cabeça avança em relação ao pescoço (postura curvada), em metros.</summary>
        public float CabecaFrente;

        /// <summary>Deslocamento do quadril no banco, em metros (positivo = para frente).</summary>
        public float QuadrilZ;

        public Color Capacete;
        public Color CapaceteDetalhe;
    }

    /// <summary>Catálogo: nomes de exibição e parâmetros de cada moto, cor e personagem.</summary>
    public static class MotoCatalogo
    {
        public static string Nome(MotoModelo modelo) => modelo == MotoModelo.Titan ? "Titã 160" : "Tornado 250";

        public static Color Pintura(MotoCor cor)
        {
            switch (cor)
            {
                case MotoCor.Preta: return new Color(0.04f, 0.04f, 0.05f);
                case MotoCor.Azul: return new Color(0.07f, 0.18f, 0.55f);
                case MotoCor.Branca: return new Color(0.9f, 0.9f, 0.88f);
                default: return new Color(0.72f, 0.05f, 0.06f);
            }
        }

        /// <summary>Cor secundária (abas do tanque, detalhes), que contrasta com a pintura.</summary>
        public static Color Detalhe(MotoCor cor)
        {
            switch (cor)
            {
                case MotoCor.Preta: return new Color(0.7f, 0.05f, 0.06f);
                case MotoCor.Branca: return new Color(0.07f, 0.07f, 0.08f);
                default: return new Color(0.88f, 0.88f, 0.86f);
            }
        }

        public static PersonagemConfig Config(Personagem personagem)
        {
            switch (personagem)
            {
                case Personagem.Robsao:
                    return new PersonagemConfig
                    {
                        Nome = "Robsão", Escala = 1.03f, Inclinacao = 8f, QuadrilZ = -0.04f,
                        Capacete = new Color(0.05f, 0.05f, 0.06f), CapaceteDetalhe = new Color(0.8f, 0.8f, 0.2f),
                    };
                case Personagem.Marcao:
                    return new PersonagemConfig
                    {
                        Nome = "Marcão", Escala = 1.01f, Inclinacao = 11f, QuadrilZ = -0.02f,
                        Capacete = new Color(0.85f, 0.3f, 0.06f), CapaceteDetalhe = new Color(0.05f, 0.05f, 0.06f),
                    };
                case Personagem.SeuValdir:
                    return new PersonagemConfig
                    {
                        Nome = "Seu Valdir", Escala = 0.97f, Inclinacao = 22f, CabecaFrente = 0.05f,
                        Capacete = new Color(0.82f, 0.78f, 0.66f), CapaceteDetalhe = new Color(0.3f, 0.3f, 0.3f),
                    };
                case Personagem.Camila:
                    return new PersonagemConfig
                    {
                        Nome = "Camila", Escala = 0.96f, Inclinacao = 13f,
                        Capacete = new Color(0.92f, 0.55f, 0.7f), CapaceteDetalhe = new Color(0.95f, 0.95f, 0.95f),
                    };
                default:
                    return new PersonagemConfig
                    {
                        Nome = "Jefinho", Escala = 1f, Inclinacao = 14f,
                        Capacete = new Color(0.9f, 0.9f, 0.92f), CapaceteDetalhe = new Color(0.95f, 0.45f, 0.05f),
                    };
            }
        }
    }
}
