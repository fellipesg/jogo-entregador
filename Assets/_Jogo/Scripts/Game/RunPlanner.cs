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
    /// Escolhe, para um pedido de DistanceKm, o restaurante e o endereço cuja rota combina melhor com a distância
    /// (convertida em metros pelo fator WorldToGameKm).
    /// </summary>
    public static class RunPlanner
    {
        public static RunPlan Plan(CityMap map, GameTuning tuning, Order order, Vector3 bikePosition, IGameRandom random, Place avoidCustomer)
        {
            RoadGraph graph = map.Graph;
            float targetMeters = (float)order.DistanceKm / tuning.WorldToGameKm;

            var best = new List<(Place restaurant, Place customer, float meters, float error)>();
            float minError = float.MaxValue;

            foreach (Place restaurant in map.Restaurants)
            {
                Place bestCustomer = null;
                float bestMeters = 0f;
                float bestError = float.MaxValue;

                foreach (Place customer in map.Customers)
                {
                    if (customer == avoidCustomer)
                    {
                        continue;
                    }

                    float meters = graph.RouteLength(restaurant.Position, customer.Position);
                    float error = Mathf.Abs(meters - targetMeters);

                    if (error < bestError)
                    {
                        bestError = error;
                        bestMeters = meters;
                        bestCustomer = customer;
                    }
                }

                if (bestCustomer == null)
                {
                    continue;
                }

                best.Add((restaurant, bestCustomer, bestMeters, bestError));
                minError = Mathf.Min(minError, bestError);
            }

            // Entre os restaurantes que acertam a distância quase tão bem quanto o melhor, sorteia um.
            var candidates = best.FindAll(c => c.error <= minError + 40f);

            // Prefere um restaurante que não esteja colado na moto; se nenhum acerta a distância assim, relaxa um pouco a tolerância.
            if (!candidates.Exists(c => graph.RouteLength(bikePosition, c.restaurant.Position) >= tuning.MinPickupMeters))
            {
                var relaxed = best.FindAll(c => c.error <= minError + 100f && graph.RouteLength(bikePosition, c.restaurant.Position) >= tuning.MinPickupMeters);

                if (relaxed.Count > 0)
                {
                    candidates = relaxed;
                }
            }
            else
            {
                candidates = candidates.FindAll(c => graph.RouteLength(bikePosition, c.restaurant.Position) >= tuning.MinPickupMeters);
            }

            var pick = candidates[random.Range(0, candidates.Count)];

            return new RunPlan
            {
                Restaurant = pick.restaurant,
                Customer = pick.customer,
                DeliveryMeters = pick.meters,
                PickupMeters = graph.RouteLength(bikePosition, pick.restaurant.Position),
            };
        }
    }
}
