using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Faz o objeto (letreiros) olhar para a câmera principal.</summary>
    public sealed class FaceCamera : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera camera = Camera.main;

            if (camera != null)
            {
                transform.rotation = camera.transform.rotation;
            }
        }
    }
}
