using System;
using System.Collections.Generic;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Progression;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NinjaRealm.Presentation.Village
{
    /// <summary>
    /// The village hub, where the game starts. It loads the save, moves the camera (drag to pan,
    /// scroll to zoom), and opens a building when it is clicked: the Command Hall goes to battle,
    /// the Dojo, Shrine and Blacksmith open their panels (<see cref="VillageScreen"/>).
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

        /// <summary>The building under the mouse, if any.</summary>
        public VillageBuilding Hovered { get; private set; }

        /// <summary>The building whose panel is open, or null.</summary>
        public VillageBuilding Opened { get; private set; }

        /// <summary>Raised when a building opens or closes (null).</summary>
        public event Action<VillageBuilding> OpenedChanged;

        void Awake()
        {
            if (GetComponent<VillageScreen>() == null) gameObject.AddComponent<VillageScreen>();
        }

        void Start()
        {
            Initialize();
            ApplyCamera();
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

            initialized = true;
            RefreshNinjas();
            return true;
        }

        /// <summary>
        /// Stands the player's ninjas on the plaza: the ones they own, or every ninja when there
        /// is no save (outside play mode). Call after the roster changes.
        /// </summary>
        public void RefreshNinjas()
        {
            if (!initialized || Ninjas == null) return;
            var roster = Profile != null ? Profile.OwnedNinjas() : new List<NinjaDefinition>(Content.GetAll<NinjaDefinition>());
            Ninjas.Show(roster, plazaCentre);
        }

        /// <summary>The first building of a kind, or null.</summary>
        public VillageBuilding Find(VillageBuildingKind kind) => buildings.Find(b => b != null && b.Kind == kind);

        public void Open(VillageBuilding building)
        {
            if (building == Opened) return;
            Opened = building;
            if (building != null && building.Kind == VillageBuildingKind.Dojo) Profile?.RefreshDojoIfNewDay(DateTime.Now);

            // Closing a building: a recruit from the Dojo joins the others on the plaza.
            if (building == null) RefreshNinjas();
            OpenedChanged?.Invoke(building);
        }

        public void Close() => Open(null);

        /// <summary>Leaves the village for the battle scene, where the lineup is set up.</summary>
        public void GoToBattle()
        {
            if (!GameScenes.Load(GameScenes.Battle))
                Debug.LogError($"[Village] {GameScenes.PathOf(GameScenes.Battle)} is not in the build settings. Run Ninja Realm > Scenes > Create Village Scene.");
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (!initialized) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && Opened != null) Close();

            // A panel is open: the village waits behind it.
            if (Opened != null)
            {
                Hovered = null;
                pressing = dragging = false;
                return;
            }

            var mouse = Mouse.current;
            var cam = ViewCamera;
            if (mouse == null || cam == null) return;

            var position = mouse.position.ReadValue();
            Hovered = BuildingAt(cam, position);

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
                if (!dragging && Hovered != null) Open(Hovered);
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

        /// <summary>The clickable building under a screen point.</summary>
        public static VillageBuilding BuildingAt(Camera cam, Vector2 screen)
        {
            if (cam == null) return null;
            var ray = cam.ScreenPointToRay(screen);
            return Physics.Raycast(ray, out var hit, 500f) ? hit.collider.GetComponentInParent<VillageBuilding>() : null;
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
