using System;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Um lugar do mapa: restaurante ou endereço de cliente.</summary>
    [Serializable]
    public sealed class Place
    {
        public string Name;
        public Vector3 Position;

        /// <summary>Feixe de luz que aparece só quando o lugar é o alvo atual.</summary>
        public GameObject Beam;

        public Place(string name, Vector3 position, GameObject beam)
        {
            Name = name;
            Position = position;
            Beam = beam;
        }
    }

    /// <summary>Lista dos lugares da cidade, preenchida pelo gerador. As regras só enxergam estes dados.</summary>
    public sealed class CityMap : MonoBehaviour
    {
        [SerializeField] private Place[] _restaurants = new Place[0];
        [SerializeField] private Place[] _customers = new Place[0];
        [SerializeField] private RoadGraph _graph;
        [SerializeField] private Vector3 _bikeSpawn;
        [SerializeField] private float _bikeSpawnYaw;
        [SerializeField] private Vector2 _mapCenter;
        [SerializeField] private float _mapSize = 1000f;

        public Place[] Restaurants => _restaurants;

        public Place[] Customers => _customers;

        public RoadGraph Graph => _graph;

        public Vector3 BikeSpawn => _bikeSpawn;

        public float BikeSpawnYaw => _bikeSpawnYaw;

        public Vector2 MapCenter => _mapCenter;

        public float MapSize => _mapSize;

        public void Configure(Place[] restaurants, Place[] customers, RoadGraph graph, Vector3 bikeSpawn, float bikeSpawnYaw, Vector2 mapCenter, float mapSize)
        {
            _restaurants = restaurants;
            _customers = customers;
            _graph = graph;
            _bikeSpawn = bikeSpawn;
            _bikeSpawnYaw = bikeSpawnYaw;
            _mapCenter = mapCenter;
            _mapSize = mapSize;
        }

        /// <summary>Apaga todos os feixes e acende só o do lugar indicado (pode ser null).</summary>
        public void Highlight(Place target)
        {
            SetBeams(_restaurants, target);
            SetBeams(_customers, target);
        }

        private static void SetBeams(Place[] places, Place target)
        {
            foreach (Place place in places)
            {
                if (place.Beam != null)
                {
                    place.Beam.SetActive(place == target);
                }
            }
        }
    }
}
