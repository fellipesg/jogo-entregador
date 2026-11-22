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

        [SerializeField] private GameObject[] _atendentes = new GameObject[5];

        /// <summary>O mesmo personagem inteiro (com cabeça e cabelo, sem capacete), para atender no balcão dos restaurantes.</summary>
        public GameObject Atendente(Personagem personagem)
        {
            int index = (int)personagem;

            return _atendentes != null && index >= 0 && index < _atendentes.Length ? _atendentes[index] : null;
        }

        public void DefinirAtendente(Personagem personagem, GameObject prefab)
        {
            int index = (int)personagem;

            if (_atendentes == null || _atendentes.Length <= index)
            {
                System.Array.Resize(ref _atendentes, index + 1);
            }

            _atendentes[index] = prefab;
        }

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
