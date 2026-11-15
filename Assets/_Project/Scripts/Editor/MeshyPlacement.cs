using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Places Meshy models in a layout: finds a model by name, fits it to a size, stands it on
    /// the ground centred on its spot, and turns it. Shared by the battle arena and the village.
    /// </summary>
    public static class MeshyPlacement
    {
        public enum Fit
        {
            /// <summary>Scale so the model is this tall.</summary>
            Height,

            /// <summary>Scale so the model's long side is this long, turned to run along x. For fences.</summary>
            Length,
        }

        /// <summary>Path of a Meshy model by name: Props/Name/Name.fbx or Buildings/Name/Name.fbx, or null when there is none yet.</summary>
        public static string ModelPath(string name) =>
            new[] { "Props", "Buildings" }
                .Select(folder => $"{MeshyArtImporter.Root}/{folder}/{name}/{name}.fbx")
                .FirstOrDefault(path => AssetDatabase.LoadAssetAtPath<GameObject>(path) != null);

        /// <summary>
        /// Places a model under <paramref name="root"/> in a holder named <paramref name="name"/>
        /// at (x, 0, z), fitted to <paramref name="size"/> and turned by <paramref name="yaw"/>.
        /// Returns the holder.
        /// </summary>
        public static Transform Place(Transform root, GameObject model, string name, float x, float z, float size, float yaw = 0f, Fit fit = Fit.Height)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(root, false);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, holder);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            var bounds = BoundsOf(instance);
            float measured;
            if (fit == Fit.Length)
            {
                if (bounds.size.z > bounds.size.x)
                {
                    instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    bounds = BoundsOf(instance);
                }

                measured = bounds.size.x;
            }
            else
            {
                measured = bounds.size.y;
            }

            if (measured > 0.0001f) instance.transform.localScale = Vector3.one * (size / measured);
            bounds = BoundsOf(instance);
            instance.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            holder.localPosition = new Vector3(x, 0f, z);
            holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return holder;
        }

        public static Bounds BoundsOf(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(instance.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
