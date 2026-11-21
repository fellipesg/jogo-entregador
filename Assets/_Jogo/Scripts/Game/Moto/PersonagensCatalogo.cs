using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Os modelos dos cinco personagens (prefabs gerados por PersonagensBuilder a partir dos FBX do Mixamo), na ordem do enum
    /// <see cref="Personagem"/>. A cena guarda a referência no MotoVisual; assim a troca em jogo (Configurar) acha o modelo sem Resources.
    /// </summary>
    [CreateAssetMenu(menuName = "Jogo/Catálogo de personagens")]
    public sealed class PersonagensCatalogo : ScriptableObject
    {
        [SerializeField] private GameObject[] _modelos = new GameObject[5];

        public GameObject Modelo(Personagem personagem)
        {
            int index = (int)personagem;

            return index >= 0 && index < _modelos.Length ? _modelos[index] : null;
        }

        public void Definir(Personagem personagem, GameObject prefab)
        {
            int index = (int)personagem;

            if (_modelos == null || _modelos.Length <= index)
            {
                System.Array.Resize(ref _modelos, index + 1);
            }

            _modelos[index] = prefab;
        }
    }
}
