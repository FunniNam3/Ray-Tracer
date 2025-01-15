using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEngine;

public class BVH
{
    public readonly NodeList allNodes;
    public readonly Triangle[] allTris;

    public Triangle[] GetTriangles() => allTris;
    readonly BVHTriangle[] AllTriangles;

    public BVH(Vector3[] verts, int[] indices, Vector3[] normals, Vector2[] uvs)
    {
        allNodes = new();
        AllTriangles = new BVHTriangle[indices.Length / 3];
        BoundingBox bounds = new BoundingBox();

        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = verts[indices[i + 0]];
            Vector3 b = verts[indices[i + 1]];
            Vector3 c = verts[indices[i + 2]];
            Vector3 center = (a + b + c) / 3;
            Vector3 max = Vector3.Max(Vector3.Max(a, b), c);
            Vector3 min = Vector3.Min(Vector3.Min(a, b), c);
            AllTriangles[i / 3] = new BVHTriangle(center, min, max, i);
            bounds.GrowToInclude(min, max);
        }

        allNodes.Add(new Node(bounds));
        Split(0, verts, 0, AllTriangles.Length);

        allTris = new Triangle[AllTriangles.Length];
        for (int i = 0; i < AllTriangles.Length; i++)
        {
            BVHTriangle buildTri = AllTriangles[i];
            Vector3 a = verts[indices[buildTri.Index]];
            Vector3 b = verts[indices[buildTri.Index + 1]];
            Vector3 c = verts[indices[buildTri.Index + 2]];
            Vector3 norm_a = normals[indices[buildTri.Index]];
            Vector3 norm_b = normals[indices[buildTri.Index + 1]];
            Vector3 norm_c = normals[indices[buildTri.Index + 2]];
            Vector2 uvA, uvB, uvC;
            if (uvs.Length > 0)
            {
                uvA = uvs[indices[buildTri.Index]];
                uvB = uvs[indices[buildTri.Index + 1]];
                uvC = uvs[indices[buildTri.Index + 2]];
            }
            else
            {
                uvA = Vector2.zero;
                uvB = Vector2.zero;
                uvC = Vector2.zero;
            }
            allTris[i] = new Triangle(a, b, c, norm_a, norm_b, norm_c, uvA, uvB, uvC);
        }
    }

    public void Split(int parentIndex, Vector3[] verts, int triGlobalStart, int triNum, int depth = 0)
    {
        const int MaxDepth = 64;
        Node parent = allNodes.Nodes[parentIndex];
        Vector3 size = parent.CalculateBoundsSize();
        float parentCost = NodeCost(size, triNum);

        (int splitAxis, float splitPos, float cost) = ChooseSplit(parent, triGlobalStart, triNum);

        if (cost < parentCost && depth < MaxDepth)
        {
            BoundingBox boundsLeft = new();
            BoundingBox boundsRight = new();
            int numOnLeft = 0;

            for (int i = triGlobalStart; i < triGlobalStart + triNum; i++)
            {
                BVHTriangle tri = AllTriangles[i];
                if (tri.Center[splitAxis] < splitPos)
                {
                    boundsLeft.GrowToInclude(tri.Min, tri.Max);

                    BVHTriangle swap = AllTriangles[triGlobalStart + numOnLeft];
                    AllTriangles[triGlobalStart + numOnLeft] = tri;
                    AllTriangles[i] = swap;
                    numOnLeft++;
                }
                else
                {
                    boundsRight.GrowToInclude(tri.Min, tri.Max);
                }
            }

            int numOnRight = triNum - numOnLeft;
            int triStartLeft = triGlobalStart + 0;
            int triStartRight = triGlobalStart + numOnLeft;

            int childIndexLeft = allNodes.Add(new(boundsLeft, triStartLeft, 0));
            int childIndexRight = allNodes.Add(new(boundsRight, triStartRight, 0));

            parent.StartIndex = childIndexLeft;
            allNodes.Nodes[parentIndex] = parent;

            Split(childIndexLeft, verts, triGlobalStart, numOnLeft, depth + 1);
            Split(childIndexRight, verts, triGlobalStart + numOnLeft, numOnRight, depth + 1);
        }
        else
        {
            parent.StartIndex = triGlobalStart;
            parent.TriangleCount = triNum;
            allNodes.Nodes[parentIndex] = parent;
        }
    }

    (int axis, float pos, float cost) ChooseSplit(Node node, int start, int count)
    {
        if (count <= 1) return (0, 0, float.PositiveInfinity);

        float bestSplitPos = 0;
        int bestSplitAxis = 0;
        const int numSplitTests = 5;

        float bestCost = float.MaxValue;

        for (int axis = 0; axis < 3; axis++)
        {
            for (int i = 0; i < numSplitTests; i++)
            {
                float splitT = (i + 1) / (numSplitTests + 1f);
                float splitPos = Mathf.Lerp(node.BoundsMin[axis], node.BoundsMax[axis], splitT);
                float cost = EvaluateSplit(axis, splitPos, start, count);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestSplitPos = splitPos;
                    bestSplitAxis = axis;
                }
            }
        }
        return (bestSplitAxis, bestSplitPos, bestCost);
    }

    float EvaluateSplit(int splitAxis, float splitPos, int start, int count)
    {
        BoundingBox boundsLeft = new();
        BoundingBox boundsRight = new();
        int numOnLeft = 0;
        int numOnRight = 0;

        for (int i = start; i < start + count; i++)
        {
            BVHTriangle tri = AllTriangles[i];
            if (tri.Center[splitAxis] < splitPos)
            {
                boundsLeft.GrowToInclude(tri.Min, tri.Max);
                numOnLeft++;
            }
            else
            {
                boundsRight.GrowToInclude(tri.Min, tri.Max);
                numOnRight++;
            }
        }

        float costA = NodeCost(boundsLeft.Size, numOnLeft);
        float costB = NodeCost(boundsRight.Size, numOnRight);
        return costA + costB;
    }

    static float NodeCost(Vector3 size, int numTriangles)
    {
        float halfArea = size.x * size.y + size.x * size.z + size.y * size.z;
        return halfArea * numTriangles;
    }

    public struct Node
    {
        public Vector3 BoundsMin;
        public Vector3 BoundsMax;
        public int StartIndex;
        public int TriangleCount;

        public Node(BoundingBox bounds) : this()
        {
            BoundsMin = bounds.Min;
            BoundsMax = bounds.Max;
            StartIndex = -1;
            TriangleCount = -1;
        }

        public Node(BoundingBox bounds, int startIndex, int triCount)
        {
            BoundsMin = bounds.Min;
            BoundsMax = bounds.Max;
            StartIndex = startIndex;
            TriangleCount = triCount;
        }

        public Vector3 CalculateBoundsSize() => BoundsMax - BoundsMin;
        public Vector3 CalculateBoundsCenter() => (BoundsMin + BoundsMax) / 2;
    }

    public struct BoundingBox
    {
        public Vector3 Min;
        public Vector3 Max;
        public Vector3 Center => (Min + Max) * 0.5f;
        public Vector3 Size => Max - Min;
        bool hasPoint;

        public void GrowToInclude(Vector3 min, Vector3 max)
        {
            if (hasPoint)
            {
                Min.x = min.x < Min.x ? min.x : Min.x;
                Min.y = min.y < Min.y ? min.y : Min.y;
                Min.z = min.z < Min.z ? min.z : Min.z;
                Max.x = max.x > Max.x ? max.x : Max.x;
                Max.y = max.y > Max.y ? max.y : Max.y;
                Max.z = max.z > Max.z ? max.z : Max.z;
            }
            else
            {
                hasPoint = true;
                Min = min;
                Max = max;
            }
        }
    }
    public readonly struct BVHTriangle
    {
        public readonly Vector3 Center;
        public readonly Vector3 Min;
        public readonly Vector3 Max;
        public readonly int Index;

        public BVHTriangle(Vector3 center, Vector3 min, Vector3 max, int index)
        {
            Center = center;
            Min = min;
            Max = max;
            Index = index;
        }
    }

    public Node[] GetNodes() => allNodes.Nodes.AsSpan(0, allNodes.NodeCount).ToArray();

    public class NodeList
    {
        public Node[] Nodes = new Node[256];
        int Index;

        public int Add(Node node)
        {
            if (Index >= Nodes.Length)
            {
                Array.Resize(ref Nodes, Nodes.Length * 2);
            }

            int nodeIndex = Index;
            Nodes[Index++] = node;
            return nodeIndex;
        }

        public int NodeCount => Index;
    }
}
