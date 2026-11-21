using System;
using System.Collections;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Anúncio de mentira: espera uns segundos e devolve sucesso.</summary>
    public sealed class FakeAdService : MonoBehaviour, IAdService
    {
        [SerializeField] private float _seconds = 2f;

        public bool IsShowing { get; private set; }

        public void ShowRewarded(Action<bool> onFinished)
        {
            StartCoroutine(Run(onFinished));
        }

        private IEnumerator Run(Action<bool> onFinished)
        {
            IsShowing = true;
            yield return new WaitForSecondsRealtime(_seconds);
            IsShowing = false;
            onFinished?.Invoke(true);
        }
    }
}
