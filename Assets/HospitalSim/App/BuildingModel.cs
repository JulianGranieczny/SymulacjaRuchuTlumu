using System.Collections.Generic;
using HospitalSim.Import;
using UnityEngine;
using UnityEngine.Rendering;

namespace HospitalSim
{
    //budynek w scenie pociety na warstwy na wysokosci przekroju kazdego pietra
    //warstwa k to wszystko miedzy przekrojem pietra k minus 1 a przekrojem pietra k
    //jak pokazesz warstwy od 0 do k to masz widok pietra k z odcietym wszystkim co wyzej
    public class BuildingModel : MonoBehaviour
    {
        public RawModel Source { get; private set; }
        public int LayerCount => layers.Count;
        public bool HasColliders { get; private set; }
        public Bounds Bounds { get; private set; }

        readonly List<GameObject> layers = new List<GameObject>();
        readonly List<Mesh> meshes = new List<Mesh>();
        Material[] mats;
        int shownUpTo = -1;

        const int MaxVertsPerMesh = 900000;

        class Out
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<int> t = new List<int>();
            public int[] remap;
        }

        public static BuildingModel Create(RawModel src, Transform parent)
        {
            var go = new GameObject("Budynek");
            go.transform.SetParent(parent, false);
            var bm = go.AddComponent<BuildingModel>();
            bm.Source = src;
            return bm;
        }

        public void Rebuild(LayoutData L)
        {
            Clear();
            var cuts = new List<float>();
            if (L != null) for (int f = 0; f < L.floors; f++) cuts.Add(L.FloorY(f) + L.cutHeight);
            int layerCount = cuts.Count + 1;

            mats = new Material[Source.meshes.Count];
            for (int i = 0; i < mats.Length; i++)
            {
                var m = Source.meshes[i];
                mats[i] = MaterialUtil.Make(new Color(m.r, m.g, m.b), false, false, true);
            }

            for (int k = 0; k < layerCount; k++)
            {
                var lg = new GameObject(k < cuts.Count ? "Warstwa do przekroju " + L.FloorLabel(k) : "Warstwa najwyższa");
                lg.transform.SetParent(transform, false);
                layers.Add(lg);
            }

            bool any = false;
            var bmin = Vector3.one * float.MaxValue; var bmax = Vector3.one * float.MinValue;
            var poly = new List<Vector3>(8);
            var below = new List<Vector3>(8);
            var above = new List<Vector3>(8);

            for (int mi = 0; mi < Source.meshes.Count; mi++)
            {
                var src = Source.meshes[mi];
                int vc = src.VertexCount;
                var outs = new List<Out>[layerCount];
                for (int k = 0; k < layerCount; k++) outs[k] = new List<Out>();
                var p = src.pos; var idx = src.idx;

                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    int ia = idx[i], ib = idx[i + 1], ic = idx[i + 2];
                    if (ia < 0 || ib < 0 || ic < 0 || ia >= vc || ib >= vc || ic >= vc) continue;
                    var A = new Vector3(p[ia * 3], p[ia * 3 + 1], p[ia * 3 + 2]);
                    var B = new Vector3(p[ib * 3], p[ib * 3 + 1], p[ib * 3 + 2]);
                    var C = new Vector3(p[ic * 3], p[ic * 3 + 1], p[ic * 3 + 2]);
                    float lo = Mathf.Min(A.y, Mathf.Min(B.y, C.y)), hi = Mathf.Max(A.y, Mathf.Max(B.y, C.y));
                    int b0 = LayerOf(cuts, lo), b1 = LayerOf(cuts, hi);

                    if (b0 == b1)
                    {
                        var o = Current(outs[b0], vc);
                        o.t.Add(Shared(o, ia, A)); o.t.Add(Shared(o, ib, B)); o.t.Add(Shared(o, ic, C));
                        continue;
                    }

                    //trojkat przecina przekroj wiec tniemy go plaszczyznami
                    poly.Clear(); poly.Add(A); poly.Add(B); poly.Add(C);
                    for (int k = b0; k < b1; k++)
                    {
                        Split(poly, cuts[k], below, above);
                        EmitPoly(Current(outs[k], vc), below);
                        poly.Clear(); poly.AddRange(above);
                    }
                    EmitPoly(Current(outs[b1], vc), poly);
                }

                for (int k = 0; k < layerCount; k++)
                    foreach (var o in outs[k])
                    {
                        if (o.t.Count == 0) continue;
                        var mesh = new Mesh { name = src.name };
                        mesh.indexFormat = o.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                        mesh.SetVertices(o.v);
                        mesh.SetTriangles(o.t, 0);
                        mesh.RecalculateNormals();
                        mesh.RecalculateBounds();
                        meshes.Add(mesh);
                        var mb = mesh.bounds;
                        bmin = Vector3.Min(bmin, mb.min); bmax = Vector3.Max(bmax, mb.max);
                        any = true;

                        var go = new GameObject(src.name);
                        go.transform.SetParent(layers[k].transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = mats[mi];
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        mr.receiveShadows = false;
                    }
            }

            Bounds = any ? new Bounds((bmin + bmax) * 0.5f, bmax - bmin) : new Bounds(Vector3.zero, Vector3.one);
            HasColliders = false;
            shownUpTo = -1;
        }

        static int LayerOf(List<float> cuts, float y)
        {
            int n = 0;
            while (n < cuts.Count && y > cuts[n]) n++;
            return n;
        }

        static Out Current(List<Out> list, int vc)
        {
            if (list.Count == 0 || list[list.Count - 1].v.Count > MaxVertsPerMesh)
            {
                var o = new Out { remap = new int[vc] };
                for (int i = 0; i < vc; i++) o.remap[i] = -1;
                list.Add(o);
            }
            return list[list.Count - 1];
        }

        static int Shared(Out o, int src, Vector3 p)
        {
            int r = o.remap[src];
            if (r >= 0) return r;
            o.v.Add(p);
            r = o.v.Count - 1;
            o.remap[src] = r;
            return r;
        }

        static void EmitPoly(Out o, List<Vector3> poly)
        {
            if (poly.Count < 3) return;
            int b = o.v.Count;
            o.v.AddRange(poly);
            for (int k = 1; k + 1 < poly.Count; k++) { o.t.Add(b); o.t.Add(b + k); o.t.Add(b + k + 1); }
        }

        //dzielimy wypukly wielokat plaszczyzna y rowne c na czesc pod nia i nad nia
        static void Split(List<Vector3> poly, float c, List<Vector3> below, List<Vector3> above)
        {
            below.Clear(); above.Clear();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % n];
                float da = a.y - c, db = b.y - c;
                if (da <= 0) below.Add(a);
                if (da >= 0) above.Add(a);
                if ((da < 0 && db > 0) || (da > 0 && db < 0))
                {
                    var x = Vector3.Lerp(a, b, da / (da - db));
                    below.Add(x); above.Add(x);
                }
            }
        }

        //pokazuje warstwy do przekroju danego pietra ujemne pietro pokazuje caly budynek
        public void ShowUpTo(int floor)
        {
            if (floor == shownUpTo) return;
            shownUpTo = floor;
            for (int i = 0; i < layers.Count; i++) layers[i].SetActive(floor < 0 || i <= floor);
        }

        //kolizje sa potrzebne dopiero w symulacji do mapy scian
        public void EnsureColliders()
        {
            if (HasColliders) return;
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                var mc = mf.gameObject.GetComponent<MeshCollider>();
                if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
            HasColliders = true;
        }

        void Clear()
        {
            foreach (var l in layers) if (l != null) Destroy(l);
            layers.Clear();
            foreach (var m in meshes) if (m != null) Destroy(m);
            meshes.Clear();
            if (mats != null) foreach (var m in mats) if (m != null) Destroy(m);
            mats = null;
        }

        void OnDestroy() { Clear(); }
    }
}
