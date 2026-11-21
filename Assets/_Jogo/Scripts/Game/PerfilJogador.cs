using Entregador.Core;

namespace Entregador.Game
{
    /// <summary>
    /// O que o jogador escolhe na tela de novo jogo: personagem, moto (alugada documentada ou bob), modelo e se tem CNH.
    /// A parte das regras (PerfilInicial) vai para a GameSession; o resto é visual e de física (modelo da moto).
    /// </summary>
    public sealed class PerfilJogador
    {
        public Personagem Personagem = Personagem.Jefinho;
        public MotoModelo Modelo = MotoModelo.Titan;
        public MotoCor Cor = MotoCor.Vermelha;
        public MotoOrigem Origem = MotoOrigem.Alugada;
        public bool TemCnh = true;

        /// <summary>Tornado 250 só existe como aluguel: a moto bob é sempre a Titã 160 (a mais comum e barata).</summary>
        public MotoModelo ModeloEfetivo => Origem == MotoOrigem.Bob ? MotoModelo.Titan : Modelo;

        public PerfilInicial ParaRegras() => new PerfilInicial { Origem = Origem, TemCnh = TemCnh };

        public PerfilJogador Clonar() => (PerfilJogador)MemberwiseClone();
    }
}
