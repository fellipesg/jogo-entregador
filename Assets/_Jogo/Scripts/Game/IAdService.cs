using System;

namespace Entregador.Game
{
    /// <summary>Anúncio premiado. A implementação real (SDK de anúncios) entra depois.</summary>
    public interface IAdService
    {
        /// <summary>Mostra o anúncio e avisa no fim se o jogador assistiu até o final.</summary>
        void ShowRewarded(Action<bool> onFinished);
    }
}
