using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// Placeholder meshes and materials for the battle prototype, built in code so the scene
    /// needs no art yet. Meshy models replace these later.
    /// </summary>
    public static class PrototypeVisuals
    {
        static readonly Dictionary<(Color, Texture), Material> LitCache = new Dictionary<(Color, Texture), Material>();
        static readonly Dictionary<Color, Material> UnlitCache = new Dictionary<Color, Material>();
        static Mesh hexTile;

        public static readonly Color TeamColor0 = new Color(0.25f, 0.58f, 1f);
        public static readonly Color TeamColor1 = new Color(0.95f, 0.3f, 0.25f);

        public static Color TeamColor(int team) => team == 0 ? TeamColor0 : TeamColor1;

        /// <summary>
        /// A shared lit material in the given colour, based on the pipeline's default material.
        /// With a texture, the colour tints it.
        /// </summary>
        public static Material Lit(Color color, Texture texture = null)
        {
            if (LitCache.TryGetValue((color, texture), out var material) && material != null) return material;
            material = NewLit(color, texture);
            LitCache[(color, texture)] = material;
            return material;
        }

        /// <summary>A new lit material the caller owns, for colours that change (hit flashes) or its own tiling.</summary>
        public static Material NewLit(Color color, Texture texture = null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Material material;
            if (shader != null)
            {
                material = new Material(shader);
            }
            else
            {
                var source = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
                material = source != null ? new Material(source) : new Material(Shader.Find("Standard"));
            }

            material.name = "Prototype Lit";
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            SetColor(material, color);
            if (texture != null)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                material.mainTexture = texture;
            }

            return material;
        }

        /// <summary>Sets the colour on URP shaders (_BaseColor) and older ones (_Color).</summary>
        public static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        /// <summary>The colour a material was made with (_BaseColor or _Color), white if it has neither.</summary>
        public static Color ColorOf(Material material)
        {
            if (material == null) return Color.white;
            if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color")) return material.GetColor("_Color");
            return Color.white;
        }

        /// <summary>A shared unlit material, for health bars that should not be shaded.</summary>
        public static Material Unlit(Color color)
        {
            if (UnlitCache.TryGetValue(color, out var material) && material != null) return material;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return Lit(color);

            material = new Material(shader) { name = "Prototype Unlit" };
            SetColor(material, color);
            UnlitCache[color] = material;
            return material;
        }

        /// <summary>A primitive with its collider removed, parented and coloured.</summary>
        public static GameObject Part(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, string name)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            DestroySafe(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return part;
        }

        /// <summary>
        /// A low poly hex tile: flat top at y = 0, sides going down to -height. The top maps the
        /// whole texture across the hex; the sides map a strip of it.
        /// </summary>
        public static Mesh HexTile()
        {
            if (hexTile != null) return hexTile;

            const float radius = HexLayout.Radius * 0.94f;
            const float height = 0.22f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            Vector2 TopUv(Vector3 point) => new Vector2(point.x / (2f * radius) + 0.5f, point.z / (2f * radius) + 0.5f);

            void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, System.Func<Vector3, Vector2> uv)
            {
                // Unity treats clockwise triangles as front faces; flip if needed so the face points outward.
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
                {
                    var swap = b;
                    b = c;
                    c = swap;
                }

                var start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                uvs.Add(uv(a));
                uvs.Add(uv(b));
                uvs.Add(uv(c));
                normals.Add(outward);
                normals.Add(outward);
                normals.Add(outward);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }

            var down = Vector3.down * height;
            for (var i = 0; i < 6; i++)
            {
                var a = HexLayout.Corner(i, radius);
                var b = HexLayout.Corner((i + 1) % 6, radius);
                Triangle(Vector3.zero, a, b, Vector3.up, TopUv);

                // Each side shows a thin strip along the top's edge, so the stone wraps over it.
                var outward = ((a + b) * 0.5f).normalized;
                Vector2 SideUv(Vector3 point)
                {
                    var along = Vector3.Dot(point - a, (b - a).normalized) / radius;
                    return new Vector2(along, 0.85f + point.y / height * 0.15f);
                }

                Triangle(a, b, b + down, outward, SideUv);
                Triangle(a, b + down, a + down, outward, SideUv);
            }

            hexTile = new Mesh { name = "Hex Tile" };
            hexTile.SetVertices(vertices);
            hexTile.SetNormals(normals);
            hexTile.SetUVs(0, uvs);
            hexTile.SetTriangles(triangles, 0);
            hexTile.RecalculateBounds();
            return hexTile;
        }

        public static void DestroySafe(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
