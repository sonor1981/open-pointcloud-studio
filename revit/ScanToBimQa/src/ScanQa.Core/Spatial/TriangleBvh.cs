using System;
using System.Collections.Generic;
using ScanQa.Core.Geometry;

namespace ScanQa.Core.Spatial
{
    /// <summary>Result of a nearest-surface query.</summary>
    public struct SurfaceHit
    {
        public bool Found;
        public int Owner;           // index of the element that owns the triangle
        public double Distance;     // model units (feet)
        public Vec3 ClosestPoint;   // on the element surface
    }

    /// <summary>
    /// Bounding volume hierarchy over the triangles of every model element in the check region.
    /// Node bounding boxes prune the search; the reported distance is always the exact
    /// point-to-triangle distance of the true surface tessellation.
    /// Thread-safe for concurrent queries after construction.
    /// </summary>
    public sealed class TriangleBvh
    {
        private const int LeafSize = 8;

        private readonly Triangle[] _tris;
        private readonly int[] _owner;
        private readonly int[] _order;
        private readonly List<Node> _nodes = new List<Node>();

        private struct Node
        {
            public Aabb Box;
            public int Left;   // child index, or -1 for leaf
            public int Right;
            public int Start;  // leaf range into _order
            public int Count;
        }

        public int TriangleCount => _tris.Length;

        public TriangleBvh(IReadOnlyList<Triangle> triangles, IReadOnlyList<int> owners)
        {
            if (triangles.Count != owners.Count) throw new ArgumentException("Triangle and owner counts differ.");
            _tris = new Triangle[triangles.Count];
            _owner = new int[triangles.Count];
            _order = new int[triangles.Count];
            var centroids = new Vec3[triangles.Count];
            for (int i = 0; i < triangles.Count; i++)
            {
                _tris[i] = triangles[i];
                _owner[i] = owners[i];
                _order[i] = i;
                centroids[i] = (triangles[i].A + triangles[i].B + triangles[i].C) / 3.0;
            }
            if (_tris.Length > 0) Build(0, _tris.Length, centroids);
        }

        private int Build(int start, int count, Vec3[] centroids)
        {
            var box = Aabb.Empty;
            var cbox = Aabb.Empty;
            for (int i = start; i < start + count; i++)
            {
                box = box.Union(_tris[_order[i]].Bounds);
                cbox = cbox.Include(centroids[_order[i]]);
            }
            int index = _nodes.Count;
            _nodes.Add(new Node { Box = box, Left = -1, Right = -1, Start = start, Count = count });
            if (count <= LeafSize) return index;

            var size = cbox.Size;
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : (size.Y >= size.Z ? 1 : 2);
            double Key(int t) => axis == 0 ? centroids[t].X : axis == 1 ? centroids[t].Y : centroids[t].Z;
            Array.Sort(_order, start, count, Comparer<int>.Create((a, b) => Key(a).CompareTo(Key(b))));
            int half = count / 2;
            int left = Build(start, half, centroids);
            int right = Build(start + half, count - half, centroids);
            var n = _nodes[index];
            n.Left = left;
            n.Right = right;
            n.Count = 0;
            _nodes[index] = n;
            return index;
        }

        /// <summary>
        /// Nearest triangle within <paramref name="maxDistance"/> whose owner passes <paramref name="accept"/>.
        /// </summary>
        public SurfaceHit Nearest(Vec3 p, double maxDistance, Func<int, bool> accept = null)
        {
            var hit = new SurfaceHit { Found = false, Owner = -1, Distance = maxDistance };
            if (_nodes.Count == 0) return hit;
            double best2 = maxDistance * maxDistance;
            var stack = new Stack<int>();
            stack.Push(0);
            while (stack.Count > 0)
            {
                var node = _nodes[stack.Pop()];
                if (node.Box.DistanceSquared(p) > best2) continue;
                if (node.Left < 0)
                {
                    for (int i = node.Start; i < node.Start + node.Count; i++)
                    {
                        int t = _order[i];
                        double tb2 = _tris[t].Bounds.DistanceSquared(p);
                        if (tb2 > best2) continue;
                        if (accept != null && !accept(_owner[t])) continue;
                        var cp = _tris[t].ClosestPoint(p);
                        double d2 = cp.DistanceSquaredTo(p);
                        if (d2 <= best2)
                        {
                            best2 = d2;
                            hit.Found = true;
                            hit.Owner = _owner[t];
                            hit.ClosestPoint = cp;
                        }
                    }
                    continue;
                }
                var l = _nodes[node.Left];
                var r = _nodes[node.Right];
                double dl = l.Box.DistanceSquared(p), dr = r.Box.DistanceSquared(p);
                // Visit the nearer child first (pushed last).
                if (dl < dr)
                {
                    if (dr <= best2) stack.Push(node.Right);
                    if (dl <= best2) stack.Push(node.Left);
                }
                else
                {
                    if (dl <= best2) stack.Push(node.Left);
                    if (dr <= best2) stack.Push(node.Right);
                }
            }
            if (hit.Found) hit.Distance = Math.Sqrt(best2);
            return hit;
        }
    }
}
