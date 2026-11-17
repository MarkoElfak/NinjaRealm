using NinjaRealm.Core.Definitions;
using NinjaRealm.Presentation.Battle;
using UnityEngine;

namespace NinjaRealm.Presentation.Village
{
    /// <summary>
    /// A small stage far from the village where one ninja stands and turns, filmed by its own
    /// camera into a texture. The village's panels draw that texture, so a panel can show a
    /// ninja's model: the Dojo's hall of all ninjas, and the details of a ninja on the plaza.
    /// </summary>
    public sealed class NinjaPreview : MonoBehaviour
    {
        /// <summary>Where the stage stands: far to the side, out of the village camera's sight.</summary>
        public static readonly Vector3 StagePosition = new Vector3(600f, 0f, 0f);

        public const int TextureWidth = 512;
        public const int TextureHeight = 640;

        /// <summary>The ninja starts turned a little to one side, which shows more of the model.</summary>
        public const float StartYaw = 25f;

        /// <summary>Degrees a second the ninja turns by itself.</summary>
        public const float TurnSpeed = 20f;

        /// <summary>Seconds the ninja stays still after the player turned it by hand.</summary>
        const float HoldSeconds = 2.5f;

        static readonly Color Backdrop = new Color(0.11f, 0.13f, 0.18f);

        Transform stage;
        Camera stageCamera;
        RenderTexture texture;
        UnitView view;
        float hold;

        /// <summary>The ninja on the stage, or null.</summary>
        public NinjaDefinition Shown { get; private set; }

        /// <summary>The ninja's view on the stage, or null.</summary>
        public UnitView View => view;

        /// <summary>The picture of the stage. Null until a ninja was shown.</summary>
        public Texture Texture => texture;

        /// <summary>How far the ninja is turned away from facing the camera, in degrees.</summary>
        public float Yaw { get; private set; }

        /// <summary>Puts this ninja on the stage and starts filming. Showing the same ninja again changes nothing.</summary>
        public void Show(NinjaDefinition ninja)
        {
            if (ninja == null)
            {
                Hide();
                return;
            }

            EnsureStage();
            stageCamera.enabled = true;
            if (ninja == Shown && view != null) return;

            if (view != null) PrototypeVisuals.DestroySafe(view.gameObject);
            Shown = ninja;
            var accent = ninja.Element != null ? ninja.Element.Color : Color.grey;
            view = UnitView.Create(stage, 0, 0, ninja.DisplayName, accent, stage.position, ninja.ModelPrefab);
            view.SetBarsVisible(false);
            view.SetRingVisible(false);
            Yaw = StartYaw;
            hold = 0f;
            ApplyYaw();
        }

        /// <summary>Clears the stage and stops filming.</summary>
        public void Hide()
        {
            Shown = null;
            if (view != null) PrototypeVisuals.DestroySafe(view.gameObject);
            view = null;
            if (stageCamera != null) stageCamera.enabled = false;
        }

        /// <summary>Turns the ninja by hand. It stays put for a moment, then turns by itself again.</summary>
        public void Turn(float degrees)
        {
            Yaw = Mathf.Repeat(Yaw + degrees, 360f);
            hold = HoldSeconds;
            ApplyYaw();
        }

        void Update() => Tick(Time.deltaTime);

        /// <summary>Advances the idle animation and the slow turn. Also called by tests and captures outside play mode.</summary>
        public void Tick(float deltaTime)
        {
            if (view == null) return;
            view.Tick(deltaTime, null);
            if (hold > 0f)
            {
                hold -= deltaTime;
                return;
            }

            Yaw = Mathf.Repeat(Yaw + TurnSpeed * deltaTime, 360f);
            ApplyYaw();
        }

        // The camera looks along +z, so a ninja facing -z looks into it.
        void ApplyYaw()
        {
            if (view != null) view.transform.rotation = Quaternion.Euler(0f, 180f + Yaw, 0f);
        }

        void EnsureStage()
        {
            if (stage != null) return;

            stage = new GameObject("Stage").transform;
            stage.SetParent(transform, false);
            stage.position = StagePosition;
            PrototypeVisuals.Part(PrimitiveType.Cylinder, stage, new Vector3(0f, -0.05f, 0f), new Vector3(2.2f, 0.05f, 2.2f),
                PrototypeVisuals.Lit(new Color(0.3f, 0.31f, 0.35f)), "Floor");

            texture = new RenderTexture(TextureWidth, TextureHeight, 24) { name = "Ninja Preview", antiAliasing = 4 };

            var cameraObject = new GameObject("Stage Camera");
            cameraObject.transform.SetParent(stage, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.05f, -3.4f);
            cameraObject.transform.LookAt(stage.position + Vector3.up * (UnitView.ModelHeight * 0.52f));
            stageCamera = cameraObject.AddComponent<Camera>();
            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = Backdrop;
            stageCamera.fieldOfView = 28f;
            stageCamera.nearClipPlane = 0.1f;
            stageCamera.farClipPlane = 20f;
            stageCamera.targetTexture = texture;
        }

        void OnDestroy() => Release();

        /// <summary>Frees the texture. Called when the preview is destroyed; tests call it themselves.</summary>
        public void Release()
        {
            if (stageCamera != null) stageCamera.targetTexture = null;
            if (texture == null) return;
            texture.Release();
            PrototypeVisuals.DestroySafe(texture);
            texture = null;
        }
    }
}
