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

    /// <summary>Ponto de vista nomeado (posição e direção da moto) que os testes de captura usam: rua residencial, largo, avenida etc.</summary>
    [Serializable]
    public sealed class ViewPoint
    {
        public string Name;
        public Vector3 Position;
        public float Yaw;

        public ViewPoint(string name, Vector3 position, float yaw)
        {
            Name = name;
            Position = position;
            Yaw = yaw;
        }
    }

    /// <summary>Faixa exclusiva de ônibus (corredor vermelho): eixo A-B e meia-largura. Ilegal para moto: o GPS não passa por ela.</summary>
    [Serializable]
    public sealed class BusLane
    {
        public Vector3 A;
        public Vector3 B;
        public float HalfWidth;

        public BusLane(Vector3 a, Vector3 b, float halfWidth)
        {
            A = a;
            B = b;
            HalfWidth = halfWidth;
        }

        /// <summary>Distância (no plano) do ponto ao eixo da faixa.</summary>
        public float DistanceTo(Vector3 p)
        {
            Vector2 a = new Vector2(A.x, A.z);
            Vector2 ab = new Vector2(B.x, B.z) - a;
            Vector2 ap = new Vector2(p.x, p.z) - a;
            float lengthSqr = ab.sqrMagnitude;
            float t = lengthSqr < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(ap, ab) / lengthSqr);

            return (ap - ab * t).magnitude;
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
        [SerializeField] private ViewPoint[] _viewPoints = new ViewPoint[0];
        [SerializeField] private BusLane[] _busLanes = new BusLane[0];

        public Place[] Restaurants => _restaurants;

        public Place[] Customers => _customers;

        public RoadGraph Graph => _graph;

        public Vector3 BikeSpawn => _bikeSpawn;

        public float BikeSpawnYaw => _bikeSpawnYaw;

        public Vector2 MapCenter => _mapCenter;

        public float MapSize => _mapSize;

        public ViewPoint[] ViewPoints => _viewPoints;

        /// <summary>Faixas de ônibus do mapa (os testes conferem que a rota do GPS nunca entra nelas).</summary>
        public BusLane[] BusLanes => _busLanes;

        /// <summary>Ponto de vista pelo nome, ou null.</summary>
        public ViewPoint FindViewPoint(string name)
        {
            foreach (ViewPoint point in _viewPoints)
            {
                if (point.Name == name)
                {
                    return point;
                }
            }

            return null;
        }

        public void SetViewPoints(ViewPoint[] viewPoints)
        {
            _viewPoints = viewPoints;
        }

        public void SetBusLanes(BusLane[] busLanes)
        {
            _busLanes = busLanes;
        }

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
