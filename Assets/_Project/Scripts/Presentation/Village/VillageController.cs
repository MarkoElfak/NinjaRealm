using System;
using System.Collections.Generic;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Progression;
using NinjaRealm.Presentation.Audio;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NinjaRealm.Presentation.Village
{
    /// <summary>
    /// The village hub, where the game starts. It loads the save, moves the camera (drag to pan,
    /// scroll to zoom), and opens what is clicked: a building's panel (the Command Hall goes to
    /// battle; the Dojo, Shrine, Blacksmith, Mint and Steel Mine have their own) or the details
    /// of a ninja on the plaza. <see cref="VillageScreen"/> draws the panels.
    /// </summary>
    public sealed class VillageController : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional. Falls back to GameBootstrap.Content.")]
        ContentDatabase content;

        [SerializeField] Camera viewCamera;

        [Header("Camera")]
        [SerializeField] Vector3 focus = new Vector3(0f, 0f, 3f);
        [SerializeField, Range(20f, 80f)] float pitch = 48f;
        [SerializeField] float yaw;
        [SerializeField, Min(4f)] float distance = 34f;
        [SerializeField] Vector2 distanceRange = new Vector2(16f, 44f);
        [SerializeField, Tooltip("How far the focus may move from its start, in metres (x and z).")]
        Vector2 panLimit = new Vector2(12f, 9f);

        [Header("Ninjas")]
        [SerializeField, Tooltip("The middle of the plaza, where the player's ninjas stand.")]
        Vector3 plazaCentre = new Vector3(0f, 0.02f, 2f);

        const float DragThreshold = 6f;

        readonly List<VillageBuilding> buildings = new List<VillageBuilding>();
        Vector3 startFocus;
        Vector2 pressPosition;
        Vector3 pressFocus;
        bool pressing;
        bool dragging;
        bool initialized;

        public ContentDatabase Content => content != null ? content : GameBootstrap.Content;

        /// <summary>The player's save. Null outside play mode or without progression content.</summary>
        public ProfileService Profile { get; private set; }

        public IReadOnlyList<VillageBuilding> Buildings => buildings;

        /// <summary>The player's ninjas standing on the plaza.</summary>
        public VillageNinjas Ninjas { get; private set; }

        public Camera ViewCamera => viewCamera != null ? viewCamera : Camera.main;

        /// <summary>The stage that shows one ninja's model in a panel.</summary>
        public NinjaPreview Preview { get; private set; }

        /// <summary>The building under the mouse, if any.</summary>
        public VillageBuilding Hovered { get; private set; }

        /// <summary>The plaza ninja under the mouse, if any.</summary>
        public NinjaDefinition HoveredNinja { get; private set; }

        /// <summary>The building whose panel is open, or null.</summary>
        public VillageBuilding Opened { get; private set; }

        /// <summary>The plaza ninja whose details are open, or null.</summary>
        public NinjaDefinition OpenedNinja { get; private set; }

        /// <summary>True while a building's panel or a ninja's details cover the village.</summary>
        public bool PanelOpen => Opened != null || OpenedNinja != null;

        /// <summary>True while the new game screen asks for the clan's name and home village. Set by <see cref="NewGameScreen"/>.</summary>
        public bool Founding { get; set; }

        /// <summary>Raised when a panel opens or closes, with the building (null for a ninja's details or a close).</summary>
        public event Action<VillageBuilding> OpenedChanged;

        /// <summary>Raised when a ninja says a line: the ninja, the line, and how long to show it in seconds.</summary>
        public event Action<NinjaDefinition, string, float> Spoke;

        /// <summary>The ninjas' voice lines.</summary>
        public NinjaVoices Voices { get; private set; }

        void Awake()
        {
            if (GetComponent<VillageScreen>() == null) gameObject.AddComponent<VillageScreen>();
            if (GetComponent<NewGameScreen>() == null) gameObject.AddComponent<NewGameScreen>();
            Voices = GetComponent<NinjaVoices>();
            if (Voices == null) Voices = gameObject.AddComponent<NinjaVoices>();
        }

        void Start()
        {
            Initialize();
            ApplyCamera();
            MusicPlayer.Play(MusicPlayer.Village);
        }

        /// <summary>Loads the content, the save and the buildings. Safe to call more than once.</summary>
        public bool Initialize()
        {
            if (initialized) return true;
            startFocus = focus;

            var database = Content;
            if (database == null)
            {
                Debug.LogError("[Village] No ContentDatabase. Assign one, or add a GameBootstrap to the scene.", this);
                enabled = false;
                return false;
            }

            if (!database.IsInitialized) database.Initialize();
            if (Application.isPlaying)
            {
                Profile = GameProfile.Load(database);
                Profile?.RefreshDojoIfNewDay(DateTime.Now);
                Profile?.StartIncome(DateTime.UtcNow);
            }

            buildings.Clear();
            buildings.AddRange(FindObjectsByType<VillageBuilding>(FindObjectsSortMode.None));
            buildings.Sort((a, b) => a.Kind.CompareTo(b.Kind));
            foreach (var building in buildings) building.EnsureBuilt();

            if (Ninjas == null)
            {
                Ninjas = new GameObject("Ninjas").AddComponent<VillageNinjas>();
                Ninjas.transform.SetParent(transform, false);
            }

            if (Preview == null)
            {
                Preview = new GameObject("Ninja Preview").AddComponent<NinjaPreview>();
                Preview.transform.SetParent(transform, false);
            }

            initialized = true;
            RefreshNinjas();
            ApplyHomeVillage();
            return true;
        }

        /// <summary>
        /// Stands the player's ninjas on the plaza: the ones they own, or every ninja when there
        /// is no save (outside play mode). Call after the roster changes.
        /// </summary>
        public void RefreshNinjas()
        {
            if (!initialized || Ninjas == null) return;
            var roster = Profile != null ? Profile.OwnedNinjas() : AllNinjas();
            Ninjas.Show(roster, plazaCentre);
        }

        /// <summary>Every ninja in the content, owned or not, sorted by rarity then ID. For the Dojo's hall.</summary>
        public List<NinjaDefinition> AllNinjas()
        {
            var all = new List<NinjaDefinition>();
            var database = Content;
            if (database == null) return all;
            all.AddRange(database.GetAll<NinjaDefinition>());
            all.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : string.CompareOrdinal(a.Id, b.Id));
            return all;
        }

        /// <summary>The first building of a kind, or null.</summary>
        public VillageBuilding Find(VillageBuildingKind kind) => buildings.Find(b => b != null && b.Kind == kind);

        /// <summary>Opens a building's panel. Null closes whatever is open.</summary>
        public void Open(VillageBuilding building)
        {
            if (building == Opened && OpenedNinja == null) return;
            Opened = building;
            OpenedNinja = null;
            if (building != null && building.Kind == VillageBuildingKind.Dojo) Profile?.RefreshDojoIfNewDay(DateTime.Now);

            // Closing: a recruit from the Dojo joins the others on the plaza, and the stage empties.
            if (building == null)
            {
                RefreshNinjas();
                if (Preview != null) Preview.Hide();
            }

            OpenedChanged?.Invoke(building);
        }

        /// <summary>Opens the details of a ninja on the plaza. Null closes whatever is open.</summary>
        public void OpenNinja(NinjaDefinition ninja)
        {
            if (ninja == null)
            {
                Close();
                return;
            }

            Speak(ninja);
            if (ninja == OpenedNinja) return;
            Opened = null;
            OpenedNinja = ninja;
            OpenedChanged?.Invoke(null);
        }

        public void Close() => Open(null);

        /// <summary>Has a ninja say one of its voice lines (Audio/Resources/Voices/VoiceLines.txt).</summary>
        public void Speak(NinjaDefinition ninja)
        {
            if (Voices == null || ninja == null) return;
            var line = Voices.Speak(ninja, out var seconds);
            if (line != null) Spoke?.Invoke(ninja, line, seconds);
        }

        /// <summary>Leaves the village for the battle scene, where the lineup is set up.</summary>
        public void GoToBattle()
        {
            if (!GameScenes.Load(GameScenes.Battle))
                Debug.LogError($"[Village] {GameScenes.PathOf(GameScenes.Battle)} is not in the build settings. Run Ninja Realm > Scenes > Create Village Scene.");
        }

        /// <summary>Where each home village's banner texture lives, in a Resources folder: Banner_&lt;village id&gt;.</summary>
        public const string BannerFolder = "VillageBanners";

        /// <summary>
        /// Hangs the home village's banners: every object named Banner... shows
        /// Resources/VillageBanners/Banner_&lt;village id&gt; instead of its own texture, or its
        /// own again when the clan has no home village yet. Call after the clan is founded.
        /// </summary>
        public void ApplyHomeVillage()
        {
            var village = Profile?.HomeVillage;
            var texture = village != null ? Resources.Load<Texture2D>($"{BannerFolder}/Banner_{village.id}") : null;
            var block = new MaterialPropertyBlock();
            foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var part in root.GetComponentsInChildren<Transform>(true))
            {
                if (!part.name.StartsWith("Banner", StringComparison.Ordinal)) continue;
                foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
                {
                    if (texture == null)
                    {
                        renderer.SetPropertyBlock(null);
                        continue;
                    }

                    block.Clear();
                    block.SetTexture(BaseMap, texture);
                    block.SetTexture(MainTex, texture);
                    renderer.SetPropertyBlock(block);
                }
            }
        }

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int MainTex = Shader.PropertyToID("_MainTex");

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (!initialized) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && PanelOpen) Close();

            // A panel or the new game screen is open: the village waits behind it.
            if (PanelOpen || Founding)
            {
                Hovered = null;
                HoveredNinja = null;
                pressing = dragging = false;
                return;
            }

            var mouse = Mouse.current;
            var cam = ViewCamera;
            if (mouse == null || cam == null) return;

            var position = mouse.position.ReadValue();
            var under = ColliderAt(cam, position);
            Hovered = under != null ? under.GetComponentInParent<VillageBuilding>() : null;
            HoveredNinja = Hovered == null && Ninjas != null ? Ninjas.NinjaOf(under) : null;

            var scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.01f)
            {
                distance = Mathf.Clamp(distance - Mathf.Sign(scrollY) * 1.5f, distanceRange.x, distanceRange.y);
                ApplyCamera();
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                pressing = true;
                dragging = false;
                pressPosition = position;
                pressFocus = focus;
            }

            if (pressing && mouse.leftButton.isPressed)
            {
                if (!dragging && (position - pressPosition).magnitude > DragThreshold) dragging = true;
                if (dragging) Pan(cam, position);
            }

            if (pressing && mouse.leftButton.wasReleasedThisFrame)
            {
                pressing = false;
                if (!dragging && HoveredNinja != null) OpenNinja(HoveredNinja);
                else if (!dragging && Hovered != null) Open(Hovered);
                dragging = false;
            }
        }

        /// <summary>Moves the focus so the ground point under the press follows the mouse.</summary>
        void Pan(Camera cam, Vector2 position)
        {
            var ground = new Plane(Vector3.up, Vector3.zero);
            focus = pressFocus;
            ApplyCamera();
            if (!RayToGround(cam, ground, pressPosition, out var from) || !RayToGround(cam, ground, position, out var to)) return;
            var offset = from - to;
            focus = ClampFocus(pressFocus + new Vector3(offset.x, 0f, offset.z));
            ApplyCamera();
        }

        static bool RayToGround(Camera cam, Plane ground, Vector2 screen, out Vector3 point)
        {
            var ray = cam.ScreenPointToRay(screen);
            point = default;
            if (!ground.Raycast(ray, out var hit) || hit > 500f) return false;
            point = ray.GetPoint(hit);
            return true;
        }

        Vector3 ClampFocus(Vector3 value) => new Vector3(
            Mathf.Clamp(value.x, startFocus.x - panLimit.x, startFocus.x + panLimit.x),
            startFocus.y,
            Mathf.Clamp(value.z, startFocus.z - panLimit.y, startFocus.z + panLimit.y));

        /// <summary>The nearest collider under a screen point: a building's or a plaza ninja's.</summary>
        public static Collider ColliderAt(Camera cam, Vector2 screen)
        {
            if (cam == null) return null;
            var ray = cam.ScreenPointToRay(screen);
            return Physics.Raycast(ray, out var hit, 500f) ? hit.collider : null;
        }

        /// <summary>The clickable building under a screen point.</summary>
        public static VillageBuilding BuildingAt(Camera cam, Vector2 screen)
        {
            var under = ColliderAt(cam, screen);
            return under != null ? under.GetComponentInParent<VillageBuilding>() : null;
        }

        /// <summary>Places the camera at its pitch and distance from the focus point.</summary>
        public void ApplyCamera()
        {
            var cam = ViewCamera;
            if (cam == null) return;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        }
    }
}
