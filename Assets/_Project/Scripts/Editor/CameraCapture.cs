using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NinjaRealm.Editor
{
    /// <summary>Renders a camera to a PNG outside play mode, for checks run from the command line.</summary>
    public static class CameraCapture
    {
        public static void Render(Camera camera, string output, int width, int height)
        {
            if (camera == null) throw new InvalidOperationException("No camera to capture.");

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = target;

            // Outside play mode nothing has rendered yet: update the ambient light and render a
            // few warm-up frames so shadows and lighting are settled before the one we keep.
            DynamicGI.UpdateEnvironment();
            for (var i = 0; i < 3; i++) camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;

            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
            File.WriteAllBytes(output, image.EncodeToPNG());

            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
        }

        /// <summary>The value after a command line argument, or the fallback.</summary>
        public static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
    }
}
