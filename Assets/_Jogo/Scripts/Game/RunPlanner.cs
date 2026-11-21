using System.Collections.Generic;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Par restaurante + cliente escolhido para um pedido, com os comprimentos de rota em metros.</summary>
    public sealed class RunPlan
    {
        public Place Restaurant;
        public Place Customer;

        /// <summary>Restaurante até o cliente.</summary>
        public float DeliveryMeters;

        /// <summary>Moto até o restaurante, no momento em que o pedido foi oferecido.</summary>
        public float PickupMeters;

        public float TotalMeters => PickupMeters + DeliveryMeters;
    }

    /// <summary>
    /// Escolhe, para um pedido de DistanceKm, o restaurante e o endereço cuja rota combina com a distância (convertida em metros pelo
    /// fator WorldToGameKm). Entre os pares que acertam a distância quase tão bem quanto o melhor, prefere restaurantes que não estejam
    /// colados na moto (MinPickupMeters) e, destes, os mais perto: a busca do pedido não vira um passeio pela cidade.
    /// </summary>
    public static class RunPlanner
    {
        public static RunPlan Plan(CityMap map, GameTuning tuning, Order order, Vector3 bikePosition, IGameRandom random, Place avoidCustomer)
        {
            RoadGraph graph = map.Graph;
            float targetMeters = (float)order.DistanceKm / tuning.WorldToGameKm;
            var pairs = new List<(Place restaurant, Place customer, float meters, float error, float pickup)>();
            float minError = float.MaxValue;

            foreach (Place restaurant in map.Restaurants)
            {
                float pickup = graph.RouteLength(bikePosition, restaurant.Position);

                foreach (Place customer in map.Customers)
                {
                    if (customer == avoidCustomer)
                    {
                        continue;
                    }

                    float meters = graph.RouteLength(restaurant.Position, customer.Position);
                    float error = Mathf.Abs(meters - targetMeters);
                    pairs.Add((restaurant, customer, meters, error, pickup));
                    minError = Mathf.Min(minError, error);
                }
            }

            var candidates = pairs.FindAll(p => p.error <= Mathf.Max(minError + 40f, 80f));
            var notGlued = candidates.FindAll(p => p.pickup >= tuning.MinPickupMeters);

            if (notGlued.Count > 0)
            {
                candidates = notGlued;
            }

            float nearest = float.MaxValue;

            foreach (var candidate in candidates)
            {
                nearest = Mathf.Min(nearest, candidate.pickup);
            }

            candidates = candidates.FindAll(p => p.pickup <= nearest + 120f);
            var pick = candidates[random.Range(0, candidates.Count)];

            return new RunPlan
            {
                Restaurant = pick.restaurant,
                Customer = pick.customer,
                DeliveryMeters = pick.meters,
                PickupMeters = pick.pickup,
            };
        }
    }
}
