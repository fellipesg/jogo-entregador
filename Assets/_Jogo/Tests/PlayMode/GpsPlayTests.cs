using System.Collections;
using System.Collections.Generic;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>GPS no grafo real: sempre há rota, ela se refaz quando o jogador sai dela e a navegação fala as manobras antes das curvas.</summary>
    public sealed class GpsPlayTests : SceneFixture
    {
        private RoadGraph Graph => Flow.Map.Graph;

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(a, b);
        }

        [UnityTest]
        public IEnumerator EveryRestaurantAndCustomerSitsOnTheStreetGraph()
        {
            foreach (Place place in Flow.Map.Restaurants)
            {
                Graph.NearestEdge(place.Position, out Vector3 snapped);
                Assert.Less(Flat(place.Position, snapped), 15f, "Restaurante longe das ruas: " + place.Name);
            }

            foreach (Place place in Flow.Map.Customers)
            {
                Graph.NearestEdge(place.Position, out Vector3 snapped);
                Assert.Less(Flat(place.Position, snapped), 15f, "Cliente longe das ruas: " + place.Name);
            }

            foreach (Place place in Flow.Map.Restaurants)
            {
                Assert.AreEqual(0, Graph.ComponentOf(Graph.NearestEdge(place.Position, out _)), "No pedaço principal das ruas: " + place.Name);
            }

            foreach (Place place in Flow.Map.Customers)
            {
                Assert.AreEqual(0, Graph.ComponentOf(Graph.NearestEdge(place.Position, out _)), "No pedaço principal das ruas: " + place.Name);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RandomStartAndTargetPairsAlwaysGetARealRoute()
        {
            var random = new System.Random(2024);
            var places = new List<Vector3>();

            foreach (Place p in Flow.Map.Restaurants)
            {
                places.Add(p.Position);
            }

            foreach (Place p in Flow.Map.Customers)
            {
                places.Add(p.Position);
            }

            var route = new List<Vector3>();
            int failures = 0;

            for (int i = 0; i < 400; i++)
            {
                // Pontos aleatórios: nós do grafo deslocados até 12 m e os lugares do jogo.
                Vector3 a = i % 4 == 0 ? places[random.Next(places.Count)] : Jitter(random);
                Vector3 b = i % 3 == 0 ? places[random.Next(places.Count)] : Jitter(random);

                bool real = Graph.TryFindRoute(a, b, route, out float length);

                if (!real)
                {
                    failures++;

                    continue;
                }

                Assert.GreaterOrEqual(length, Flat(a, b) - 0.5f, "A rota não é mais curta que a reta.");
                Assert.AreEqual(a, route[0]);
                Assert.AreEqual(b, route[route.Count - 1]);
                Assert.Greater(route.Count, 2);
            }

            Assert.AreEqual(0, failures, "Pares sem rota pelas ruas (caía na reta).");
            yield return null;
        }

        private Vector3 Jitter(System.Random random)
        {
            Vector3 node = Graph.NodePosition(random.Next(Graph.NodeCount));

            return node + new Vector3((float)(random.NextDouble() * 24 - 12), 0f, (float)(random.NextDouble() * 24 - 12));
        }

        [UnityTest]
        public IEnumerator EveryRestaurantReachesEveryCustomerAndBack()
        {
            var route = new List<Vector3>();

            foreach (Place r in Flow.Map.Restaurants)
            {
                foreach (Place c in Flow.Map.Customers)
                {
                    Assert.IsTrue(Graph.TryFindRoute(r.Position, c.Position, route, out float there), $"{r.Name} -> {c.Name}");
                    Assert.IsTrue(Graph.TryFindRoute(c.Position, r.Position, route, out float back), $"{c.Name} -> {r.Name}");
                    Assert.Less(there, 3500f);
                    Assert.Less(back, 3500f);
                }
            }

            yield return null;
        }

        private IEnumerator SetGuideTarget(Place place)
        {
            Flow.NewWeek(1, new ScriptedRandom());
            Flow.Bike.SetLocked(true);
            Flow.Bike.Teleport(Flow.Map.BikeSpawn, Flow.Map.BikeSpawnYaw);
            yield return null;
            Flow.Route.SetTarget(place.Position);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GoingMoreThan25MetersOffRouteRecalculatesTheRoute()
        {
            Place target = Flow.Map.Customers[Flow.Map.Customers.Length - 1];
            yield return SetGuideTarget(target);
            RouteGuide guide = Flow.Route;
            int before = guide.Recalculations;
            bool announced = false;
            guide.Anunciou += (text, m) => announced |= text.Contains("Recalculando");
            Assert.IsTrue(guide.RouteIsReal);

            // Anda uns 30 m pela rota (sem recalcular), depois sai 60 m para o lado.
            Vector3 first = guide.Route[Mathf.Min(2, guide.Route.Count - 1)];
            Vector3 off = first + new Vector3(60f, 0f, 60f);
            Flow.Bike.Teleport(new Vector3(off.x, 0.1f, off.z), 0f);
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(before, guide.Recalculations, "Menos de 1,5 s fora da rota: ainda não recalcula.");
            yield return new WaitForSeconds(1.8f);

            Assert.Greater(guide.Recalculations, before, "Saiu da rota: o GPS recalcula.");
            Assert.Less(Flat(guide.Route[0], Flow.Bike.Position), 2f, "A nova rota nasce na moto.");
            Assert.Less(Flat(guide.Route[guide.Route.Count - 1], target.Position), 1f);
            Assert.IsTrue(announced, "O GPS avisa 'Recalculando rota'.");
        }

        [UnityTest]
        public IEnumerator RidingBackAgainstTheRouteRecalculatesInsteadOfPointingBackwards()
        {
            Place target = Flow.Map.Customers[Flow.Map.Customers.Length - 1];
            yield return SetGuideTarget(target);
            RouteGuide guide = Flow.Route;

            // Avança 120 m pela rota original (o GPS vai cortando o que ficou para trás).
            Vector3[] original = new Vector3[guide.Route.Count];

            for (int i = 0; i < original.Length; i++)
            {
                original[i] = guide.Route[i];
            }

            Vector3 ahead = PointAlong(original, 120f);
            Flow.Bike.Teleport(new Vector3(ahead.x, 0.1f, ahead.z), 0f);
            yield return null;
            yield return null;
            float remainingAhead = guide.RemainingMeters;
            int before = guide.Recalculations;

            // Volta pela mesma rua, em passos, até 45 m para trás.
            for (float back = 120f; back > 70f; back -= 5f)
            {
                Vector3 p = PointAlong(original, back);
                Flow.Bike.Teleport(new Vector3(p.x, 0.1f, p.z), 0f);
                yield return new WaitForSeconds(0.1f);
            }

            yield return new WaitForSeconds(2.5f);
            Assert.Greater(guide.Recalculations, before, "Voltou pela rota: o GPS refaz o caminho.");
            Assert.Greater(guide.RemainingMeters, remainingAhead, "E agora mostra o caminho que falta de onde a moto está.");
            Assert.Less(Flat(guide.Route[0], Flow.Bike.Position), 2f);
        }

        private static Vector3 PointAlong(Vector3[] polyline, float meters)
        {
            float left = meters;

            for (int i = 1; i < polyline.Length; i++)
            {
                float segment = Flat(polyline[i - 1], polyline[i]);

                if (left <= segment)
                {
                    return Vector3.Lerp(polyline[i - 1], polyline[i], segment < 0.01f ? 0f : left / segment);
                }

                left -= segment;
            }

            return polyline[polyline.Length - 1];
        }

        [UnityTest]
        public IEnumerator RouteLineStartsOnTheFirstFrameAndTheNavigationHasInstructions()
        {
            Place target = Flow.Map.Customers[Flow.Map.Customers.Length - 1];
            yield return SetGuideTarget(target);
            RouteGuide guide = Flow.Route;

            Assert.IsTrue(guide.HasTarget);
            Assert.Greater(guide.Route.Count, 2);
            Assert.Greater(guide.RemainingMeters, 100f);
            Assert.GreaterOrEqual(guide.Instrucoes.Count, 2, "Uma rota longa tem manobras e a chegada.");
            Assert.AreEqual(Manobra.Chegada, guide.Instrucoes[guide.Instrucoes.Count - 1].Manobra);
            Assert.IsTrue(guide.ProximaInstrucao.Existe);
            Assert.Greater(guide.ProximaInstrucao.Distancia, 0.0);
            Assert.IsFalse(string.IsNullOrEmpty(guide.ProximaInstrucao.Rua));
        }

        [UnityTest]
        public IEnumerator DrivingTheRouteAnnouncesEveryTurnBeforeItAndInOrder()
        {
            Place target = Flow.Map.Customers[Flow.Map.Customers.Length - 1];
            yield return SetGuideTarget(target);
            RouteGuide guide = Flow.Route;
            var heard = new List<(string text, Manobra manobra, double toManeuver)>();
            Instrucao current = null;
            System.Action<string, Manobra> listener = (text, manobra) =>
            {
                Instrucao inst = guide.ProximaInstrucao.Instrucao;
                heard.Add((text, manobra, inst == null ? -1 : Flat(Flow.Bike.Position, new Vector3((float)inst.X, 0f, (float)inst.Z))));
                current = inst;
            };
            guide.Anunciou += listener;

            var instructions = new List<Instrucao>(guide.Instrucoes);
            var polyline = new List<Vector3>(guide.Route);
            var body = Flow.Bike.GetComponent<Rigidbody>();
            Flow.Bike.SetLocked(false);

            // Percorre a rota a 12 m/s (43 km/h), um passo de 0,2 s por quadro.
            float total = 0f;

            for (int i = 1; i < polyline.Count; i++)
            {
                total += Flat(polyline[i - 1], polyline[i]);
            }

            for (float along = 0f; along < total - 2f; along += 2.4f)
            {
                Vector3 p = PointAlong(polyline.ToArray(), along);
                Vector3 q = PointAlong(polyline.ToArray(), along + 3f);
                Vector3 d = (q - p).normalized;
                Flow.Bike.Teleport(new Vector3(p.x, 0.1f, p.z), Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                body.linearVelocity = d * 12f;
                yield return null;
            }

            guide.Anunciou -= listener;
            Assert.IsNotNull(current);

            // Cada manobra (menos "siga em frente") foi anunciada de longe, de perto e agora, nesta ordem, antes da curva.
            int cursor = 0;

            foreach (Instrucao inst in instructions)
            {
                if (inst.Manobra == Manobra.SigaEmFrente)
                {
                    continue;
                }

                string[] words = inst.Texto.Split(' ');
                string key = inst.Manobra == Manobra.Chegada ? "Você chegou" : words[0] + " " + words[1];
                int found = heard.FindIndex(cursor, h => h.text.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0);
                Assert.GreaterOrEqual(found, 0, "Sem anúncio para: " + inst.Texto);
                Assert.GreaterOrEqual(found, cursor, "Fora de ordem: " + inst.Texto);

                if (inst.Manobra != Manobra.Chegada)
                {
                    Assert.Greater(heard[found].toManeuver, 5.0, "O primeiro aviso vem antes de chegar na curva: " + inst.Texto);
                }

                cursor = found;
            }

            Assert.IsTrue(heard.Exists(h => h.text.StartsWith("Em ")), "Há avisos com distância.");
            Assert.IsFalse(heard.Exists(h => h.text.Contains("Recalculando")), "Seguir a rota não pede recálculo.");
        }

        [UnityTest]
        public IEnumerator DeviationIsAnnouncedOnceAndRecalculationIsRateLimited()
        {
            Place target = Flow.Map.Customers[Flow.Map.Customers.Length - 1];
            yield return SetGuideTarget(target);
            RouteGuide guide = Flow.Route;
            int announcements = 0;
            System.Action<string, Manobra> listener = (text, m) => announcements += text.Contains("Recalculando") ? 1 : 0;
            guide.Anunciou += listener;
            int before = guide.Recalculations;
            float start = Time.unscaledTime;

            for (int i = 1; i <= 3; i++)
            {
                Vector3 p = Flow.Map.BikeSpawn + new Vector3(70f * i, 0f, -60f * i);
                Flow.Bike.Teleport(new Vector3(p.x, 0.1f, p.z), 0f);
                yield return new WaitForSeconds(1.8f);
            }

            guide.Anunciou -= listener;
            float elapsed = Time.unscaledTime - start;
            int recalcs = guide.Recalculations - before;
            Assert.GreaterOrEqual(recalcs, 2);
            Assert.LessOrEqual(recalcs, Mathf.CeilToInt(elapsed / guide.MinRecalcInterval) + 1, "Recálculo com intervalo mínimo.");
            Assert.AreEqual(1, announcements, "'Recalculando rota' é dito uma vez só por desvio.");
        }

        [UnityTest]
        public IEnumerator RidingAgainstTheRouteForMoreThanThreeSecondsRecalculatesFromHere()
        {
            Place target = Flow.Map.Customers[Flow.Map.Customers.Length - 1];
            yield return SetGuideTarget(target);
            RouteGuide guide = Flow.Route;
            var body = Flow.Bike.GetComponent<Rigidbody>();
            Flow.Bike.SetLocked(false);

            Vector3 d = guide.Route[Mathf.Min(2, guide.Route.Count - 1)] - guide.Route[1];
            d.y = 0f;
            d.Normalize();
            Vector3 start = Flow.Map.BikeSpawn + d * 40f;
            Flow.Bike.Teleport(new Vector3(start.x, 0.1f, start.z), Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg);
            body.linearVelocity = -d * 6f;
            yield return new WaitForSeconds(0.2f);
            int before = guide.Recalculations;

            for (int i = 0; i < 50; i++)
            {
                body.linearVelocity = -d * 6f;
                yield return new WaitForSeconds(0.1f);
            }

            Assert.Greater(guide.Recalculations, before, "Andando ao contrário da rota por mais de 3 s: recalcula.");
            Assert.Less(Flat(guide.Route[0], Flow.Bike.Position), 2f, "A rota nova sai de onde a moto está.");
            Assert.Less(Flat(guide.Route[guide.Route.Count - 1], target.Position), 1f);
        }

        [UnityTest]
        public IEnumerator ASlowDetourIsReplacedByAMuchShorterRouteEveryTenSeconds()
        {
            Place target = Flow.Map.Customers[Flow.Map.Customers.Length - 1];
            yield return SetGuideTarget(target);
            RouteGuide guide = Flow.Route;
            float good = guide.RemainingMeters;

            Vector3 waypoint = Graph.NodePosition(0);
            float best = 0f;

            for (int i = 0; i < Graph.NodeCount; i++)
            {
                Vector3 n = Graph.NodePosition(i);
                float away = Flat(n, Flow.Bike.Position);

                if (away > best && away < 380f && Flat(n, target.Position) > 300f)
                {
                    best = away;
                    waypoint = n;
                }
            }

            var first = new List<Vector3>();
            var second = new List<Vector3>();
            Graph.TryFindRoute(Flow.Bike.Position, waypoint, first, out _);
            Graph.TryFindRoute(waypoint, target.Position, second, out _);
            first.AddRange(second);

            var field = typeof(RouteGuide).GetField("_route", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = (List<Vector3>)field.GetValue(guide);
            list.Clear();
            list.AddRange(first);
            typeof(RouteGuide).GetField("_bestRemaining", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(guide, 1e6f);
            typeof(RouteGuide).GetField("_movingAwayMeters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(guide, 1e6f);
            yield return null;
            Assert.Greater(guide.RemainingMeters, good * 1.15f + 40f, "A volta é bem mais longa.");

            yield return new WaitForSeconds(11f);
            Assert.GreaterOrEqual(guide.FasterRoutes, 1, "A checagem periódica achou um caminho bem mais curto.");
            Assert.Less(guide.RemainingMeters, good * 1.1f);
        }

        [UnityTest]
        public IEnumerator EveryPhysicalBlockerSitsOnABlockedEdge()
        {
            ViaBloqueada[] blockers = Object.FindObjectsByType<ViaBloqueada>(FindObjectsSortMode.None);
            Assert.Greater(blockers.Length, 0, "A cidade tem barreiras de obra.");
            Assert.Greater(Graph.BlockedCount, 0);
            int onBlocked = 0;

            foreach (ViaBloqueada blocker in blockers)
            {
                for (int e = 0; e < Graph.EdgeCount; e++)
                {
                    Vector3 a = Graph.EdgeStart(e);
                    Vector3 ab = Graph.EdgeEnd(e) - a;
                    ab.y = 0f;
                    Vector3 ap = blocker.Centro - a;
                    ap.y = 0f;
                    float t = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude);
                    bool near = Flat(a + ab * t, blocker.Centro) <= ViaBloqueada.Raio + 0.01f;

                    if (near)
                    {
                        Assert.IsTrue(Graph.IsBlocked(e), "Trecho livre passando por uma barreira de obra em " + blocker.Centro);
                        onBlocked++;
                    }
                }
            }

            Assert.Greater(onBlocked, 0, "Pelo menos uma barreira fica sobre um trecho do grafo (as outras estão em ruas que o GPS nem usa).");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoutesNeverUseBlockedEdgesNorGoThroughDeadEnds()
        {
            var random = new System.Random(77);
            var route = new List<Vector3>();
            var places = new List<Vector3>();

            foreach (Place p in Flow.Map.Restaurants)
            {
                places.Add(p.Position);
            }

            foreach (Place p in Flow.Map.Customers)
            {
                places.Add(p.Position);
            }

            var nodeIndex = new Dictionary<Vector3, int>();

            for (int i = 0; i < Graph.NodeCount; i++)
            {
                nodeIndex[Graph.NodePosition(i)] = i;
            }

            for (int n = 0; n < 300; n++)
            {
                Vector3 a = n % 2 == 0 ? places[random.Next(places.Count)] : Jitter(random);
                Vector3 b = n % 3 == 0 ? places[random.Next(places.Count)] : Jitter(random);

                if (!Graph.TryFindRoute(a, b, route, out _))
                {
                    continue;
                }

                for (int i = 1; i < route.Count - 2; i++)
                {
                    if (!nodeIndex.TryGetValue(route[i], out int from) || !nodeIndex.TryGetValue(route[i + 1], out int to) || from == to)
                    {
                        continue;
                    }

                    bool found = false;

                    for (int e = 0; e < Graph.EdgeCount; e++)
                    {
                        bool same = (Graph.EdgeStart(e) == route[i] && Graph.EdgeEnd(e) == route[i + 1]) || (Graph.EdgeStart(e) == route[i + 1] && Graph.EdgeEnd(e) == route[i]);

                        if (same && !Graph.IsBlocked(e))
                        {
                            found = true;

                            break;
                        }
                    }

                    Assert.IsTrue(found, "A rota usa um trecho bloqueado (ou inexistente) entre os nós " + from + " e " + to);
                }

                for (int i = 2; i < route.Count - 2; i++)
                {
                    if (nodeIndex.TryGetValue(route[i], out int node))
                    {
                        Assert.Greater(Graph.NodeDegree(node), 1, "A rota passa por um beco sem saída (nó " + node + ").");
                    }
                }
            }

            yield return null;
        }
    }
}
