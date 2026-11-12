using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using MuseDesk.Phase2;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Diagrams
{
    /// <summary>
    /// Builds a spatial architecture diagram from STRUCTURED data (nodes + directed edges) using
    /// the Antigravity diagram node/arrow/label prefabs. Deterministic layered layout, runtime
    /// labels, connector arrows, node selection, and reset/dismiss. It never invents arbitrary
    /// meshes — it only instances the supplied node prefabs for the node types present in the
    /// spec. Feed it real backend graph data via Build(spec); ShowExample() renders the canonical
    /// Frontend -> API -> Auth -> Database walkthrough.
    /// </summary>
    public class ArchitectureDiagramController : MonoBehaviour
    {
        [Serializable] public class NodeSpec
        {
            public string id;
            public string label;
            public string type;     // frontend | apigateway | auth | database | server | storage | queue | cloud | process
            public string details;  // shown when the node is selected
        }
        [Serializable] public class EdgeSpec { public string from; public string to; }
        [Serializable] public class DiagramSpec
        {
            public string title = "Architecture";
            public List<NodeSpec> nodes = new List<NodeSpec>();
            public List<EdgeSpec> edges = new List<EdgeSpec>();
        }
        [Serializable] public class TypePrefab { public string type; public GameObject prefab; }

        [Header("Prefabs (type -> node mesh)")]
        public List<TypePrefab> nodePrefabs = new List<TypePrefab>();
        public GameObject connectorArrowPrefab;
        public GameObject labelPlaquePrefab;
        public GameObject selectionHighlightPrefab;
        public GameObject displayPlatformPrefab;

        [Header("Layout")]
        public Transform contentRoot;
        public float nodeSpacing = 0.14f;
        public float nodeScale = 0.6f;
        public float labelYOffset = 0.07f;

        readonly List<GameObject> _spawned = new List<GameObject>();
        readonly Dictionary<string, Transform> _nodeTx = new Dictionary<string, Transform>();
        GameObject _highlight;
        DiagramSpec _current;
        int _selected = -1;
        Phase2Bootstrap _phase2;
        CancellationTokenSource _cts;
        int _idem;

        void OnDestroy()
        {
            try { _cts?.Cancel(); _cts?.Dispose(); } catch { }
        }

        Transform Root => contentRoot != null ? contentRoot : transform;

        public void Clear()
        {
            foreach (var g in _spawned) if (g != null) Destroy(g);
            _spawned.Clear();
            _nodeTx.Clear();
            _highlight = null;
            _selected = -1;
            _current = null;
        }

        public void Dismiss() { Clear(); gameObject.SetActive(false); }

        /// <summary>Poke hook: show the diagram (from the backend when available, else the built-in
        /// example), or dismiss it if already shown.</summary>
        public void ToggleExample()
        {
            if (gameObject.activeSelf && _spawned.Count > 0) { Dismiss(); return; }
            gameObject.SetActive(true);
            _ = ShowFromBackendOrExampleAsync();
        }

        async Task ShowFromBackendOrExampleAsync()
        {
            bool built = false;
            try { built = await BuildFromBackendAsync(DiagramTemplates.AuthFlow); }
            catch (Exception ex) { Debug.LogWarning($"[MuseDesk] diagram backend fetch failed: {ex.Message}"); }
            if (!built && this != null) ShowExample();   // offline / no backend → deterministic example
        }

        /// <summary>
        /// Fetch a STRUCTURED diagram from the backend (Phase 2 CreateDiagram) and build it from the
        /// real nodes/edges. Returns false when no board/backend is available or the graph is empty,
        /// so the caller can fall back. Never invents a graph.
        /// </summary>
        public async Task<bool> BuildFromBackendAsync(string template)
        {
            var gw = ResolveGateway();
            if (gw == null) return false;
            _cts ??= new CancellationTokenSource();
            var dv2 = await gw.CreateDiagramAsync(new DiagramCommand
            {
                Template = template,
                IdempotencyKey = $"diagram-{SystemInfo.deviceUniqueIdentifier[..6]}-{++_idem:D4}",
            }, _cts.Token);
            if (this == null) return false;
            if (dv2 == null || dv2.Nodes == null || dv2.Nodes.Count == 0) return false;
            gameObject.SetActive(true);
            Build(ToSpec(dv2));
            return true;
        }

        IPhase2Gateway ResolveGateway()
        {
            if (_phase2 == null) _phase2 = UnityEngine.Object.FindAnyObjectByType<Phase2Bootstrap>();
            return _phase2 != null ? _phase2.Gateway : null;
        }

        static DiagramSpec ToSpec(DiagramV2 d)
        {
            var spec = new DiagramSpec { title = PrettyTemplate(d.Template) };
            foreach (var n in d.Nodes)
                spec.nodes.Add(new NodeSpec { id = n.Id, label = n.Label, type = IconToType(n.Icon), details = n.EvidenceRef });
            if (d.Edges != null)
                foreach (var e in d.Edges)
                    spec.edges.Add(new EdgeSpec { from = e.Source, to = e.Target });
            return spec;
        }

        // Backend node icons -> the node-prefab type keys this controller instances.
        static string IconToType(string icon) => (icon ?? "").ToLowerInvariant() switch
        {
            "user" => "frontend",
            "shield" => "auth",
            "database" => "database",
            "service" => "apigateway",
            "queue" => "queue",
            "code" => "server",
            "file" => "process",
            "test" => "process",
            _ => "process",
        };

        static string PrettyTemplate(string t) => t switch
        {
            DiagramTemplates.AuthFlow => "Authentication Flow",
            DiagramTemplates.TaskDependencyGraph => "Task Dependencies",
            DiagramTemplates.ModuleDependencyGraph => "Module Dependencies",
            _ => "Architecture",
        };

        public void ShowExample()
        {
            var spec = new DiagramSpec { title = "Authentication Flow" };
            spec.nodes.Add(new NodeSpec { id = "fe",  label = "Frontend", type = "frontend",   details = "Quest client UI\nsends sign-in request" });
            spec.nodes.Add(new NodeSpec { id = "api", label = "API",      type = "apigateway", details = "Gateway routes\n/auth/login" });
            spec.nodes.Add(new NodeSpec { id = "auth",label = "Auth",     type = "auth",       details = "Validates credentials\nissues a token" });
            spec.nodes.Add(new NodeSpec { id = "db",  label = "Database", type = "database",   details = "Stores users\n+ sessions" });
            spec.edges.Add(new EdgeSpec { from = "fe",  to = "api" });
            spec.edges.Add(new EdgeSpec { from = "api", to = "auth" });
            spec.edges.Add(new EdgeSpec { from = "auth",to = "db" });
            gameObject.SetActive(true);
            Build(spec);
        }

        public void Build(DiagramSpec spec)
        {
            Clear();
            if (spec == null || spec.nodes == null || spec.nodes.Count == 0) return;
            _current = spec;

            // Deterministic row layout, centred on the content root.
            int n = spec.nodes.Count;
            float x0 = -(n - 1) * nodeSpacing * 0.5f;

            for (int i = 0; i < n; i++)
            {
                var ns = spec.nodes[i];
                var prefab = PrefabFor(ns.type);
                var local = new Vector3(x0 + i * nodeSpacing, 0f, 0f);

                var node = prefab != null ? Instantiate(prefab, Root) : NewMarker(ns.label);
                node.name = "Node_" + (string.IsNullOrEmpty(ns.id) ? i.ToString() : ns.id);
                node.transform.localPosition = local;
                node.transform.localRotation = Quaternion.identity;
                node.transform.localScale = Vector3.one * nodeScale;
                _spawned.Add(node);
                _nodeTx[ns.id ?? node.name] = node.transform;

                SpawnLabel(ns.label, local + Vector3.up * labelYOffset);
            }

            if (spec.edges != null)
                foreach (var e in spec.edges)
                    if (_nodeTx.TryGetValue(e.from ?? "", out var a) && _nodeTx.TryGetValue(e.to ?? "", out var b))
                        SpawnConnector(a.localPosition, b.localPosition);

            if (!string.IsNullOrEmpty(spec.title))
                SpawnLabel("<b>" + spec.title + "</b>", new Vector3(0f, labelYOffset * 2.4f, 0f), 1.4f);
        }

        /// <summary>Highlight + return the details of node i (poke/selection hook).</summary>
        public string SelectNode(int i)
        {
            if (_current == null || i < 0 || i >= _current.nodes.Count) return null;
            _selected = i;
            var ns = _current.nodes[i];
            if (_nodeTx.TryGetValue(ns.id ?? ("Node_" + i), out var tx))
            {
                if (_highlight == null && selectionHighlightPrefab != null)
                    _highlight = Instantiate(selectionHighlightPrefab, Root);
                if (_highlight != null)
                {
                    _highlight.transform.localPosition = tx.localPosition;
                    _highlight.transform.localScale = Vector3.one * nodeScale * 1.3f;
                    _highlight.SetActive(true);
                }
            }
            return ns.details;
        }

        GameObject PrefabFor(string type)
        {
            string key = (type ?? "").ToLowerInvariant();
            foreach (var tp in nodePrefabs)
                if (tp != null && tp.prefab != null && (tp.type ?? "").ToLowerInvariant() == key)
                    return tp.prefab;
            return nodePrefabs.Count > 0 ? nodePrefabs[0].prefab : null;
        }

        void SpawnConnector(Vector3 a, Vector3 b)
        {
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = b - a;
            float len = dir.magnitude;
            if (len < 1e-4f) return;

            GameObject arrow = connectorArrowPrefab != null ? Instantiate(connectorArrowPrefab, Root) : NewMarker("->");
            arrow.name = "Connector";
            arrow.transform.localPosition = mid;
            arrow.transform.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            // Stretch along local Z toward the target; keep thickness from the prefab.
            float gap = nodeScale * 0.08f;
            arrow.transform.localScale = new Vector3(nodeScale, nodeScale, Mathf.Max(0.01f, len - gap) * 3.2f);
            _spawned.Add(arrow);
        }

        void SpawnLabel(string text, Vector3 localPos, float scaleMul = 1f)
        {
            GameObject plaque = labelPlaquePrefab != null ? Instantiate(labelPlaquePrefab, Root) : new GameObject("Label");
            plaque.name = "Label";
            plaque.transform.localPosition = localPos;
            plaque.transform.localRotation = Quaternion.identity;
            plaque.transform.localScale = Vector3.one * nodeScale * scaleMul;
            _spawned.Add(plaque);

            var tmp = plaque.GetComponentInChildren<TMP_Text>();
            if (tmp == null)
            {
                var go = new GameObject("Text");
                go.transform.SetParent(plaque.transform, false);
                go.transform.localScale = Vector3.one * 0.02f;
                var t = go.AddComponent<TextMeshPro>();
                t.alignment = TextAlignmentOptions.Center;
                t.fontSize = 6f;
                t.rectTransform.sizeDelta = new Vector2(4f, 1.5f);
                tmp = t;
            }
            tmp.text = text;
        }

        GameObject NewMarker(string label)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(Root, false);
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            return go;
        }
    }
}
