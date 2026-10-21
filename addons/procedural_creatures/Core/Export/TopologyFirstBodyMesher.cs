// Experimental topology-first body construction.
// This path does not sample an SDF, voxel grid, depth map, or independent final primitive shells.
// It creates parametric ellipsoid patches, cuts explicit attachment loops, then stitches those
// loops to tapered bridges. Failed stitches are rolled back to closed patches and reported.
using System;
using System.Collections.Generic;
using System.Linq;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;

namespace PixelCreatures.Core.Export
{
    public static class TopologyFirstBodyMesher
    {
        private const int SurfaceSides = 24;
        private const int SurfaceRings = 14;
        private const int BridgeSteps = 3;

        public sealed class Face
        {
            public int A { get; }
            public int B { get; }
            public int C { get; }
            public MaterialSlot Material { get; }

            public Face(int a, int b, int c, MaterialSlot material)
            {
                A = a; B = b; C = c; Material = material;
            }
        }

        public sealed class MeshData
        {
            public List<Vec3> Vertices { get; } = new List<Vec3>();
            public List<Face> Faces { get; } = new List<Face>();
            public HashSet<int> PrimitiveIndices { get; } = new HashSet<int>();
            public int BoundaryEdgeCount { get; internal set; }
            public int NonManifoldEdgeCount { get; internal set; }
            public int ConnectedComponentCount { get; internal set; }
            public int FailedConnections { get; internal set; }
        }

        private sealed class Node
        {
            public int Id;
            public int BoneIndex;
            public int SourcePrimitiveIndex = -1;
            public Vec3 Center;
            public Vec3 Radii;
            public Vec3 LocalCenter;
            public Quat LocalRotation = Quat.Identity;
            public Xform BoneWorld = Xform.Identity;
            public bool IsFallback;
            public MaterialSlot Material;
            public string GroupName = string.Empty;
            public string Tag = string.Empty;
            public readonly List<int> ConnectionIds = new List<int>();
            public readonly Dictionary<int, Vec3> LocalAxes = new Dictionary<int, Vec3>();
            public readonly Dictionary<int, List<int>> ConnectionLoops = new Dictionary<int, List<int>>();
            public int[] VertexIds = Array.Empty<int>();
            public Vec3[] UnitDirections = Array.Empty<Vec3>();
            public readonly List<LocalTriangle> LocalFaces = new List<LocalTriangle>();

            public double MaxRadius => Math.Max(Radii.X, Math.Max(Radii.Y, Radii.Z));
            public double MinRadius => Math.Min(Radii.X, Math.Min(Radii.Y, Radii.Z));

            public Vec3 WorldPoint(Vec3 localOffset)
            {
                if (IsFallback) return Center + LocalRotation.Rotate(localOffset);
                return BoneWorld.Point(LocalCenter + LocalRotation.Rotate(localOffset));
            }

            public Vec3 LocalDirectionTo(Vec3 worldPoint)
            {
                if (IsFallback)
                    return LocalRotation.Conjugate.Rotate(worldPoint - Center).NormalizedOr(Vec3.UnitX);
                Vec3 boneLocal = BoneWorld.InversePoint(worldPoint) - LocalCenter;
                return LocalRotation.Conjugate.Rotate(boneLocal).NormalizedOr(Vec3.UnitX);
            }
        }

        private sealed class Connection
        {
            public int Id;
            public int A;
            public int B;
            public Vec3 AnchorA;
            public Vec3 AnchorB;
            public double RadiusA;
            public double RadiusB;
            public MaterialSlot Material;
            public int PrimitiveIndex = -1;
            public bool Active = true;
            public bool Explicit;
        }

        private readonly struct LocalTriangle
        {
            public readonly int A;
            public readonly int B;
            public readonly int C;
            public LocalTriangle(int a, int b, int c) { A = a; B = b; C = c; }
        }

        private sealed class Cutout
        {
            public int ConnectionId;
            public Vec3 Axis;
            public double CosAngle;
        }

        private sealed class DisjointSet
        {
            private readonly int[] _parent;
            private readonly byte[] _rank;

            public DisjointSet(int count)
            {
                _parent = new int[count];
                _rank = new byte[count];
                for (int i = 0; i < count; i++) _parent[i] = i;
            }

            public int Find(int value)
            {
                while (_parent[value] != value)
                {
                    _parent[value] = _parent[_parent[value]];
                    value = _parent[value];
                }
                return value;
            }

            public bool Join(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb) return false;
                if (_rank[ra] < _rank[rb]) _parent[ra] = rb;
                else if (_rank[ra] > _rank[rb]) _parent[rb] = ra;
                else { _parent[rb] = ra; _rank[ra]++; }
                return true;
            }
        }

        public static MeshData Build(CreatureAnatomy anatomy)
        {
            if (anatomy == null) throw new ArgumentNullException(nameof(anatomy));
            var result = new MeshData();
            var nodes = new List<Node>();
            var connections = new List<Connection>();
            var pairToConnection = new Dictionary<long, int>();

            // All eligible source skin primitives are consumed by this mode, including nested
            // ellipsoids deliberately dropped in favour of the larger enclosing patch.
            var ellipsoids = new List<(int Index, PrimitiveDef Primitive)>();
            for (int i = 0; i < anatomy.Primitives.Count; i++)
            {
                PrimitiveDef p = anatomy.Primitives[i];
                if (!IsStructural(anatomy, p)) continue;
                if (p.Kind == PrimKind.Ellipsoid)
                {
                    result.PrimitiveIndices.Add(i);
                    ellipsoids.Add((i, p));
                }
                else if (p.Kind == PrimKind.RoundCone)
                {
                    result.PrimitiveIndices.Add(i);
                }
            }

            // Prefer the outer ellipsoid when a family emits nested, concentric skin volumes on
            // the same bone. Keeping both as closed surfaces would reintroduce the old shell bug.
            ellipsoids.Sort((a, b) => Volume(b.Primitive.Radii).CompareTo(Volume(a.Primitive.Radii)));
            foreach ((int index, PrimitiveDef p) in ellipsoids)
            {
                Xform bone = anatomy.Skeleton.RestWorld(p.BoneA);
                Vec3 center = bone.Point(p.LocalA);
                bool nested = false;
                foreach (Node existing in nodes)
                {
                    if (existing.BoneIndex != p.BoneA || (existing.Center - center).Length > 0.28) continue;
                    if (existing.Radii.X >= p.Radii.X && existing.Radii.Y >= p.Radii.Y && existing.Radii.Z >= p.Radii.Z)
                    {
                        nested = true;
                        break;
                    }
                }
                if (nested) continue;

                nodes.Add(new Node
                {
                    Id = nodes.Count,
                    BoneIndex = p.BoneA,
                    SourcePrimitiveIndex = index,
                    Center = center,
                    Radii = p.Radii,
                    LocalCenter = p.LocalA,
                    LocalRotation = p.LocalRotation,
                    BoneWorld = bone,
                    Material = p.Material,
                    GroupName = anatomy.Groups[p.Group].Name ?? string.Empty,
                    Tag = p.Tag ?? string.Empty,
                });
            }

            // Source tapered segments become graph edges, not closed frustum shells. Endpoints
            // attach to an existing semantic patch when close enough; otherwise an explicit joint
            // patch is created at that endpoint.
            for (int i = 0; i < anatomy.Primitives.Count; i++)
            {
                PrimitiveDef p = anatomy.Primitives[i];
                if (!IsStructural(anatomy, p) || p.Kind != PrimKind.RoundCone) continue;
                Vec3 a = anatomy.Skeleton.RestWorld(p.BoneA).Point(p.LocalA);
                Vec3 b = anatomy.Skeleton.RestWorld(p.BoneB).Point(p.LocalB);
                int nodeA = FindOrCreateEndpoint(nodes, anatomy, p.BoneA, a, p.RadiusA, p.Material);
                int nodeB = FindOrCreateEndpoint(nodes, anatomy, p.BoneB, b, p.RadiusB, p.Material);
                if (nodeA == nodeB) continue;
                AddConnection(nodes, connections, pairToConnection, nodeA, nodeB,
                    a, b, p.RadiusA, p.RadiusB, p.Material, i, true);
            }

            // Fill gaps between adjacent skeletal regions (for example pelvis -> belly -> chest)
            // only when their explicit surfaces overlap. A minimum spanning forest prevents
            // redundant loops from punching several holes through the same joint.
            var sets = new DisjointSet(nodes.Count);
            foreach (Connection c in connections) sets.Join(c.A, c.B);
            var candidates = new List<(int A, int B, double Score, double Distance)>();
            for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
            {
                Node a = nodes[i], b = nodes[j];
                if (pairToConnection.ContainsKey(PairKey(i, j))) continue;
                bool sameBone = a.BoneIndex == b.BoneIndex;
                bool adjacentBones = AreAdjacent(anatomy.Skeleton, a.BoneIndex, b.BoneIndex);
                if (!sameBone && !adjacentBones) continue;
                double distance = (a.Center - b.Center).Length;
                if (distance < 0.18) continue;
                double reach = a.MaxRadius + b.MaxRadius;
                if (distance > reach * 1.12) continue;
                candidates.Add((i, j, distance / Math.Max(0.1, reach), distance));
            }
            candidates.Sort((a, b) => a.Score.CompareTo(b.Score));
            foreach (var candidate in candidates)
            {
                if (!sets.Join(candidate.A, candidate.B)) continue;
                Node a = nodes[candidate.A], b = nodes[candidate.B];
                double radius = Math.Max(0.45, Math.Min(a.MinRadius, b.MinRadius) * 0.24);
                AddConnection(nodes, connections, pairToConnection, candidate.A, candidate.B,
                    a.Center, b.Center, radius, radius, a.Material, -1, false);
            }

            // Extract parametric patches and retry without any connection whose cap could not be
            // represented by a clean boundary loop. A failed bridge therefore leaves closed source
            // patches rather than a hole or an open, torn surface.
            for (int pass = 0; pass <= connections.Count; pass++)
            {
                result.Vertices.Clear();
                result.Faces.Clear();
                foreach (Node node in nodes) BuildNodeSurface(result, node, connections, nodes);

                bool removedAny = false;
                foreach (Connection connection in connections)
                {
                    if (!connection.Active) continue;
                    Node a = nodes[connection.A], b = nodes[connection.B];
                    if (!a.ConnectionLoops.ContainsKey(connection.Id) || !b.ConnectionLoops.ContainsKey(connection.Id))
                    {
                        connection.Active = false;
                        removedAny = true;
                    }
                }
                if (!removedAny) break;
            }

            foreach (Connection connection in connections)
            {
                if (!connection.Active) continue;
                Node a = nodes[connection.A], b = nodes[connection.B];
                if (!a.ConnectionLoops.TryGetValue(connection.Id, out List<int>? loopA) ||
                    !b.ConnectionLoops.TryGetValue(connection.Id, out List<int>? loopB)) continue;
                BridgeLoops(result, nodes, connection, loopA, loopB);
            }

            result.FailedConnections = connections.Count(c => !c.Active);
            ComputeDiagnostics(result);
            return result;
        }

        private static double Volume(Vec3 r) => r.X * r.Y * r.Z;

        private static bool IsStructural(CreatureAnatomy anatomy, PrimitiveDef p)
        {
            if (p.Group < 0 || p.Group >= anatomy.Groups.Count) return false;
            if (p.Kind != PrimKind.Ellipsoid && p.Kind != PrimKind.RoundCone) return false;
            if (p.Material != MaterialSlot.Primary && p.Material != MaterialSlot.Secondary &&
                p.Material != MaterialSlot.Accent && p.Material != MaterialSlot.White) return false;
            if ((p.Flags & (PrimFlags.Thin | PrimFlags.Translucent | PrimFlags.FrontOnly)) != 0) return false;
            if ((anatomy.Groups[p.Group].Flags & GroupFlags.Interior) != 0) return false;

            string group = anatomy.Groups[p.Group].Name ?? string.Empty;
            string tag = p.Tag ?? string.Empty;
            string[] excluded =
            {
                "gear", "weapon", "equipment", "shield", "pauldron",
                "horn", "ear", "antenna", "whisker", "claw", "teeth", "fang", "tusk",
                "pincer", "stinger", "spike", "thorn", "wing", "fin", "membrane",
                "shell", "crown", "crest", "leaf", "tentacle", "eyestalk", "eye",
                "beard", "mane"
            };
            foreach (string token in excluded)
            {
                if (group.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return false;
                if (tag.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }
            if (tag.IndexOf("mouthinterior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                tag.IndexOf("nostril", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        private static bool AreAdjacent(Skeleton skeleton, int a, int b)
        {
            if (a < 0 || b < 0 || a >= skeleton.Count || b >= skeleton.Count) return false;
            return skeleton.Bones[a].Parent == b || skeleton.Bones[b].Parent == a;
        }

        private static int FindOrCreateEndpoint(List<Node> nodes, CreatureAnatomy anatomy,
            int boneIndex, Vec3 worldPoint, double radius, MaterialSlot material)
        {
            int best = -1;
            double bestDistance = double.PositiveInfinity;
            foreach (Node node in nodes)
            {
                if (node.BoneIndex != boneIndex) continue;
                double distance = (node.Center - worldPoint).Length;
                double reach = Math.Max(1.0, node.MaxRadius * 0.92 + radius * 1.15);
                if (distance <= reach && distance < bestDistance)
                {
                    best = node.Id;
                    bestDistance = distance;
                }
            }
            if (best >= 0) return best;

            foreach (Node node in nodes)
            {
                if (!node.IsFallback || node.BoneIndex != boneIndex) continue;
                if ((node.Center - worldPoint).Length <= Math.Max(0.35, radius * 0.28))
                    return node.Id;
            }

            double r = Math.Max(0.5, radius * 0.82);
            var fallback = new Node
            {
                Id = nodes.Count,
                BoneIndex = boneIndex,
                Center = worldPoint,
                Radii = new Vec3(r, r, r),
                LocalCenter = Vec3.Zero,
                LocalRotation = Quat.Identity,
                BoneWorld = Xform.Identity,
                IsFallback = true,
                Material = material,
                GroupName = "joint",
                Tag = "joint"
            };
            nodes.Add(fallback);
            return fallback.Id;
        }

        private static void AddConnection(List<Node> nodes, List<Connection> connections,
            Dictionary<long, int> pairToConnection, int a, int b, Vec3 anchorA, Vec3 anchorB,
            double radiusA, double radiusB, MaterialSlot material, int primitiveIndex, bool explicitEdge)
        {
            if (a == b) return;
            long key = PairKey(a, b);
            if (pairToConnection.TryGetValue(key, out int existingId))
            {
                if (primitiveIndex >= 0) connections[existingId].PrimitiveIndex = primitiveIndex;
                return;
            }

            var connection = new Connection
            {
                Id = connections.Count,
                A = a,
                B = b,
                AnchorA = anchorA,
                AnchorB = anchorB,
                RadiusA = Math.Max(0.35, radiusA),
                RadiusB = Math.Max(0.35, radiusB),
                Material = material,
                PrimitiveIndex = primitiveIndex,
                Explicit = explicitEdge
            };
            connections.Add(connection);
            pairToConnection.Add(key, connection.Id);
            nodes[a].ConnectionIds.Add(connection.Id);
            nodes[b].ConnectionIds.Add(connection.Id);
        }

        private static long PairKey(int a, int b)
        {
            int low = Math.Min(a, b), high = Math.Max(a, b);
            return ((long)low << 32) | (uint)high;
        }

        private static void BuildNodeSurface(MeshData mesh, Node node,
            List<Connection> connections, List<Node> nodes)
        {
            node.LocalAxes.Clear();
            node.ConnectionLoops.Clear();
            node.LocalFaces.Clear();

            var cutouts = new List<Cutout>();
            foreach (int connectionId in node.ConnectionIds)
            {
                Connection connection = connections[connectionId];
                if (!connection.Active) continue;
                Vec3 target = connection.A == node.Id ? connection.AnchorB : connection.AnchorA;
                Vec3 axis = node.LocalDirectionTo(target);
                if (axis.LengthSquared < 1e-10)
                    axis = node.LocalDirectionTo(nodes[connection.A == node.Id ? connection.B : connection.A].Center);
                axis = axis.NormalizedOr(Vec3.UnitX);
                double connectorRadius = connection.A == node.Id ? connection.RadiusA : connection.RadiusB;
                double surfaceRadius = RadiusAlong(node.Radii, axis);
                double ratio = connectorRadius / Math.Max(0.1, surfaceRadius);
                double angle = Math.Asin(Math.Max(0.34, Math.Min(0.62, ratio * 1.18)));
                cutouts.Add(new Cutout { ConnectionId = connectionId, Axis = axis, CosAngle = Math.Cos(angle) });
                node.LocalAxes[connectionId] = axis;
            }

            // Closely aligned links must not carve overlapping holes at one joint.
            for (int i = 0; i < cutouts.Count; i++)
            for (int j = i + 1; j < cutouts.Count; j++)
            {
                if (Vec3.Dot(cutouts[i].Axis, cutouts[j].Axis) > 0.78)
                {
                    double smallerAngleCos = Math.Cos(0.34);
                    cutouts[i].CosAngle = Math.Max(cutouts[i].CosAngle, smallerAngleCos);
                    cutouts[j].CosAngle = Math.Max(cutouts[j].CosAngle, smallerAngleCos);
                }
            }

            var localPoints = new List<Vec3>();
            var units = new List<Vec3>();
            int AddPoint(Vec3 unit)
            {
                units.Add(unit);
                localPoints.Add(new Vec3(unit.X * node.Radii.X, unit.Y * node.Radii.Y, unit.Z * node.Radii.Z));
                return localPoints.Count - 1;
            }

            int bottom = AddPoint(new Vec3(0, -1, 0));
            var rings = new int[SurfaceRings + 1, SurfaceSides];
            for (int j = 1; j < SurfaceRings; j++)
            {
                double latitude = -Math.PI * 0.5 + Math.PI * j / SurfaceRings;
                double h = Math.Cos(latitude), y = Math.Sin(latitude);
                for (int i = 0; i < SurfaceSides; i++)
                {
                    double angle = 2 * Math.PI * i / SurfaceSides;
                    rings[j, i] = AddPoint(new Vec3(h * Math.Cos(angle), y, h * Math.Sin(angle)));
                }
            }
            int top = AddPoint(new Vec3(0, 1, 0));

            var candidates = new List<LocalTriangle>();
            void AddCandidate(int a, int b, int c)
            {
                Vec3 wa = node.WorldPoint(localPoints[a]);
                Vec3 wb = node.WorldPoint(localPoints[b]);
                Vec3 wc = node.WorldPoint(localPoints[c]);
                Vec3 outward = (wa + wb + wc) / 3.0 - node.Center;
                if (Vec3.Dot(Vec3.Cross(wb - wa, wc - wa), outward) < 0)
                {
                    int swap = b; b = c; c = swap;
                }
                candidates.Add(new LocalTriangle(a, b, c));
            }

            for (int i = 0; i < SurfaceSides; i++)
            {
                int next = (i + 1) % SurfaceSides;
                AddCandidate(bottom, rings[1, i], rings[1, next]);
                for (int j = 1; j < SurfaceRings - 1; j++)
                {
                    int a = rings[j, i], b = rings[j, next];
                    int c = rings[j + 1, i], d = rings[j + 1, next];
                    AddCandidate(a, c, b);
                    AddCandidate(b, c, d);
                }
                AddCandidate(rings[SurfaceRings - 1, i], top, rings[SurfaceRings - 1, next]);
            }

            var kept = new List<LocalTriangle>(candidates.Count);
            foreach (LocalTriangle face in candidates)
            {
                Vec3 centroid = (units[face.A] + units[face.B] + units[face.C]).NormalizedOr(Vec3.UnitY);
                bool remove = false;
                foreach (Cutout cutout in cutouts)
                {
                    if (Vec3.Dot(centroid, cutout.Axis) > cutout.CosAngle)
                    {
                        remove = true;
                        break;
                    }
                }
                if (!remove) kept.Add(face);
            }

            int baseIndex = mesh.Vertices.Count;
            node.VertexIds = new int[localPoints.Count];
            node.UnitDirections = units.ToArray();
            for (int i = 0; i < localPoints.Count; i++)
            {
                node.VertexIds[i] = baseIndex + i;
                mesh.Vertices.Add(node.WorldPoint(localPoints[i]));
            }
            foreach (LocalTriangle face in kept)
            {
                node.LocalFaces.Add(face);
                mesh.Faces.Add(new Face(node.VertexIds[face.A], node.VertexIds[face.B],
                    node.VertexIds[face.C], node.Material));
            }
            AssignBoundaryLoops(node, connections);
        }

        private static double RadiusAlong(Vec3 radii, Vec3 direction)
        {
            double x = direction.X / Math.Max(0.1, radii.X);
            double y = direction.Y / Math.Max(0.1, radii.Y);
            double z = direction.Z / Math.Max(0.1, radii.Z);
            double denominator = Math.Sqrt(x * x + y * y + z * z);
            return denominator > 1e-9 ? 1.0 / denominator : Math.Max(radii.X, Math.Max(radii.Y, radii.Z));
        }

        private static void AssignBoundaryLoops(Node node, List<Connection> connections)
        {
            var edgeUse = new Dictionary<long, int>();
            foreach (LocalTriangle face in node.LocalFaces)
            {
                CountEdge(edgeUse, face.A, face.B);
                CountEdge(edgeUse, face.B, face.C);
                CountEdge(edgeUse, face.C, face.A);
            }

            var adjacency = new Dictionary<int, List<int>>();
            var remaining = new HashSet<long>();
            foreach (var pair in edgeUse)
            {
                if (pair.Value != 1) continue;
                int a = EdgeA(pair.Key), b = EdgeB(pair.Key);
                if (!adjacency.TryGetValue(a, out List<int>? aa)) adjacency[a] = aa = new List<int>();
                if (!adjacency.TryGetValue(b, out List<int>? bb)) adjacency[b] = bb = new List<int>();
                aa.Add(b); bb.Add(a);
                remaining.Add(pair.Key);
            }

            var loops = new List<List<int>>();
            while (remaining.Count > 0)
            {
                long first = remaining.First();
                int start = EdgeA(first), current = EdgeB(first), previous = start;
                var loop = new List<int> { start };
                remaining.Remove(first);
                int guard = 0;
                while (current != start && guard++ < edgeUse.Count + 4)
                {
                    loop.Add(current);
                    if (!adjacency.TryGetValue(current, out List<int>? neighbours) || neighbours.Count != 2) break;
                    int next = neighbours[0] == previous ? neighbours[1] : neighbours[0];
                    long key = EdgeKey(current, next);
                    if (next != start && !remaining.Contains(key)) break;
                    remaining.Remove(key);
                    previous = current;
                    current = next;
                }
                if (current == start && loop.Count >= 3) loops.Add(loop);
            }

            var ranked = new List<(int Loop, int Connection, double Score)>();
            for (int loopIndex = 0; loopIndex < loops.Count; loopIndex++)
            {
                Vec3 average = Vec3.Zero;
                foreach (int vertex in loops[loopIndex]) average += node.UnitDirections[vertex];
                average = average.NormalizedOr(Vec3.UnitX);
                foreach (int connectionId in node.ConnectionIds)
                {
                    if (!node.LocalAxes.TryGetValue(connectionId, out Vec3 axis)) continue;
                    ranked.Add((loopIndex, connectionId, Vec3.Dot(average, axis)));
                }
            }
            ranked.Sort((a, b) => b.Score.CompareTo(a.Score));
            var usedLoops = new HashSet<int>();
            var usedConnections = new HashSet<int>();
            foreach (var candidate in ranked)
            {
                if (candidate.Score < 0.22) break;
                if (!usedLoops.Add(candidate.Loop)) continue;
                if (!usedConnections.Add(candidate.Connection))
                {
                    usedLoops.Remove(candidate.Loop);
                    continue;
                }
                node.ConnectionLoops[candidate.Connection] = loops[candidate.Loop];
            }
        }

        private static void BridgeLoops(MeshData mesh, List<Node> nodes, Connection connection,
            List<int> localLoopA, List<int> localLoopB)
        {
            Node nodeA = nodes[connection.A], nodeB = nodes[connection.B];
            List<int> loopA = ToGlobalLoop(nodeA, localLoopA, mesh);
            List<int> loopB = ToGlobalLoop(nodeB, localLoopB, mesh);
            if (loopA.Count < 3 || loopB.Count < 3) return;

            Vec3 centerA = Average(mesh, loopA);
            Vec3 centerB = Average(mesh, loopB);
            Vec3 axis = (centerB - centerA).NormalizedOr((nodeB.Center - nodeA.Center).NormalizedOr(Vec3.UnitX));
            Vec3 guide = Math.Abs(Vec3.Dot(axis, Vec3.UnitY)) < 0.9 ? Vec3.UnitY : Vec3.UnitX;
            Vec3 u = Vec3.Cross(axis, guide).NormalizedOr(Vec3.UnitZ);
            Vec3 v = Vec3.Cross(axis, u).NormalizedOr(Vec3.UnitY);
            SortLoop(mesh, loopA, centerA, u, v);
            SortLoop(mesh, loopB, centerB, u, v);
            AlignLoopPhase(mesh, loopA, centerA, loopB, centerB, u, v);

            double radiusA = AverageRadius(mesh, loopA, centerA);
            double radiusB = AverageRadius(mesh, loopB, centerB);
            var previous = loopA;
            for (int step = 1; step <= BridgeSteps; step++)
            {
                double t = step / (double)(BridgeSteps + 1);
                Vec3 center = Vec3.Lerp(centerA, centerB, t);
                double radius = DMath.Lerp(radiusA, radiusB, t);
                var ring = new List<int>(loopA.Count);
                for (int i = 0; i < loopA.Count; i++)
                {
                    Vec3 p = mesh.Vertices[loopA[i]];
                    Vec3 radial = p - centerA;
                    double angle = Math.Atan2(Vec3.Dot(radial, v), Vec3.Dot(radial, u));
                    Vec3 point = center + u * (Math.Cos(angle) * radius) + v * (Math.Sin(angle) * radius);
                    ring.Add(mesh.Vertices.Count);
                    mesh.Vertices.Add(point);
                }
                AddRingBridge(mesh, previous, ring, centerA, centerB, connection.Material);
                previous = ring;
            }
            ZipLoops(mesh, previous, loopB, centerA, centerB, connection.Material);
        }

        private static List<int> ToGlobalLoop(Node node, List<int> localLoop, MeshData mesh)
        {
            var result = new List<int>(localLoop.Count);
            foreach (int local in localLoop) result.Add(node.VertexIds[local]);
            return result;
        }

        private static Vec3 Average(MeshData mesh, List<int> loop)
        {
            Vec3 sum = Vec3.Zero;
            foreach (int index in loop) sum += mesh.Vertices[index];
            return sum / loop.Count;
        }

        private static double AverageRadius(MeshData mesh, List<int> loop, Vec3 center)
        {
            double total = 0;
            foreach (int index in loop) total += (mesh.Vertices[index] - center).Length;
            return total / loop.Count;
        }

        private static void SortLoop(MeshData mesh, List<int> loop, Vec3 center, Vec3 u, Vec3 v)
        {
            loop.Sort((a, b) =>
            {
                Vec3 da = mesh.Vertices[a] - center, db = mesh.Vertices[b] - center;
                double aa = Math.Atan2(Vec3.Dot(da, v), Vec3.Dot(da, u));
                double ab = Math.Atan2(Vec3.Dot(db, v), Vec3.Dot(db, u));
                return aa.CompareTo(ab);
            });
        }

        private static void AlignLoopPhase(MeshData mesh, List<int> loopA, Vec3 centerA,
            List<int> loopB, Vec3 centerB, Vec3 u, Vec3 v)
        {
            if (loopA.Count == 0 || loopB.Count == 0) return;
            Vec3 da = mesh.Vertices[loopA[0]] - centerA;
            double angleA = Math.Atan2(Vec3.Dot(da, v), Vec3.Dot(da, u));
            int best = 0;
            double bestDelta = double.PositiveInfinity;
            for (int i = 0; i < loopB.Count; i++)
            {
                Vec3 db = mesh.Vertices[loopB[i]] - centerB;
                double angleB = Math.Atan2(Vec3.Dot(db, v), Vec3.Dot(db, u));
                double delta = Math.Abs(WrapAngle(angleB - angleA));
                if (delta < bestDelta) { bestDelta = delta; best = i; }
            }
            if (best == 0) return;
            var rotated = loopB.Skip(best).Concat(loopB.Take(best)).ToList();
            loopB.Clear();
            loopB.AddRange(rotated);
        }

        private static double WrapAngle(double angle)
        {
            while (angle > Math.PI) angle -= Math.PI * 2;
            while (angle < -Math.PI) angle += Math.PI * 2;
            return angle;
        }

        private static void AddRingBridge(MeshData mesh, List<int> a, List<int> b,
            Vec3 lineA, Vec3 lineB, MaterialSlot material)
        {
            int count = Math.Min(a.Count, b.Count);
            if (count < 3) return;
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                AddBridgeTriangle(mesh, a[i], a[next], b[i], lineA, lineB, material);
                AddBridgeTriangle(mesh, a[next], b[next], b[i], lineA, lineB, material);
            }
        }

        private static void ZipLoops(MeshData mesh, List<int> a, List<int> b,
            Vec3 lineA, Vec3 lineB, MaterialSlot material)
        {
            int na = a.Count, nb = b.Count;
            int i = 0, j = 0;
            while (i < na || j < nb)
            {
                double nextA = i < na ? (i + 1) / (double)na : double.PositiveInfinity;
                double nextB = j < nb ? (j + 1) / (double)nb : double.PositiveInfinity;
                if (nextA <= nextB)
                {
                    AddBridgeTriangle(mesh, a[i % na], a[(i + 1) % na], b[j % nb], lineA, lineB, material);
                    i++;
                }
                else
                {
                    AddBridgeTriangle(mesh, a[i % na], b[(j + 1) % nb], b[j % nb], lineA, lineB, material);
                    j++;
                }
            }
        }

        private static void AddBridgeTriangle(MeshData mesh, int a, int b, int c,
            Vec3 lineA, Vec3 lineB, MaterialSlot material)
        {
            Vec3 pa = mesh.Vertices[a], pb = mesh.Vertices[b], pc = mesh.Vertices[c];
            if (Vec3.Cross(pb - pa, pc - pa).LengthSquared < 1e-12) return;
            Vec3 centroid = (pa + pb + pc) / 3.0;
            Vec3 axis = lineB - lineA;
            double lengthSquared = axis.LengthSquared;
            double t = lengthSquared > 1e-12 ? Vec3.Dot(centroid - lineA, axis) / lengthSquared : 0.5;
            t = Math.Max(0, Math.Min(1, t));
            Vec3 outward = centroid - (lineA + axis * t);
            if (Vec3.Dot(Vec3.Cross(pb - pa, pc - pa), outward) < 0)
            {
                int swap = b; b = c; c = swap;
            }
            mesh.Faces.Add(new Face(a, b, c, material));
        }

        private static void ComputeDiagnostics(MeshData mesh)
        {
            mesh.BoundaryEdgeCount = 0;
            mesh.NonManifoldEdgeCount = 0;
            mesh.ConnectedComponentCount = 0;
            var edgeUse = new Dictionary<long, int>();
            var usedVertices = new HashSet<int>();
            foreach (Face face in mesh.Faces)
            {
                CountEdge(edgeUse, face.A, face.B);
                CountEdge(edgeUse, face.B, face.C);
                CountEdge(edgeUse, face.C, face.A);
                usedVertices.Add(face.A); usedVertices.Add(face.B); usedVertices.Add(face.C);
            }
            foreach (int count in edgeUse.Values)
            {
                if (count == 1) mesh.BoundaryEdgeCount++;
                else if (count != 2) mesh.NonManifoldEdgeCount++;
            }

            var sets = new DisjointSet(mesh.Vertices.Count);
            foreach (Face face in mesh.Faces)
            {
                sets.Join(face.A, face.B);
                sets.Join(face.B, face.C);
            }
            var roots = new HashSet<int>();
            foreach (int vertex in usedVertices) roots.Add(sets.Find(vertex));
            mesh.ConnectedComponentCount = roots.Count;
        }

        private static void CountEdge(Dictionary<long, int> edges, int a, int b)
        {
            long key = EdgeKey(a, b);
            edges.TryGetValue(key, out int count);
            edges[key] = count + 1;
        }

        private static long EdgeKey(int a, int b)
        {
            int low = Math.Min(a, b), high = Math.Max(a, b);
            return ((long)low << 32) | (uint)high;
        }

        private static int EdgeA(long key) => (int)(key >> 32);
        private static int EdgeB(long key) => (int)(key & 0xffffffffL);
    }
}
