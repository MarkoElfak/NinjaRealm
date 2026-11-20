using System.Collections.Generic;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Progression;
using NinjaRealm.Presentation.Battle;
using UnityEngine;

namespace NinjaRealm.Presentation.Village
{
    /// <summary>
    /// The Shrine's altar in the village: a low plinth for every altar slot in an arc in front of
    /// the Shrine. A lit slot holds a dark standing stone; a set rune's stone shows its glyph
    /// glowing in the rune's colour, and a Legendary one floats. It sits beside the building, not
    /// under it, so the Shrine's click box stays the same.
    /// </summary>
    public sealed class ShrineAltar : MonoBehaviour
    {
        const float Radius = 1.6f;
        const float ArcDegrees = 110f;

        static readonly Color Plinth = new Color(0.46f, 0.45f, 0.43f);
        static readonly Color Stone = new Color(0.2f, 0.22f, 0.26f);

        readonly List<Transform> floating = new List<Transform>();

        /// <summary>Stands the altar between the Shrine and the plaza.</summary>
        public void Place(VillageBuilding shrine, Vector3 plazaCentre)
        {
            var bounds = VillageBuilding.BoundsOf(shrine.gameObject);
            var centre = new Vector3(bounds.center.x, 0f, bounds.center.z);
            var toPlaza = plazaCentre - centre;
            toPlaza.y = 0f;
            if (toPlaza.sqrMagnitude < 0.01f) toPlaza = Vector3.back;
            toPlaza.Normalize();

            var reach = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.8f;
            transform.position = centre + toPlaza * (reach + Radius + 0.4f);
            transform.rotation = Quaternion.LookRotation(-toPlaza, Vector3.up);
        }

        /// <summary>Builds the stones for the save's altar. Without a save every slot shows lit and empty.</summary>
        public void Show(ProfileService profile, int slotCount)
        {
            for (var i = transform.childCount - 1; i >= 0; i--) PrototypeVisuals.DestroySafe(transform.GetChild(i).gameObject);
            floating.Clear();

            var count = profile != null ? profile.RuneSlotCount : slotCount;
            for (var i = 0; i < count; i++)
            {
                var t = count == 1 ? 0.5f : i / (float)(count - 1);
                var angle = Mathf.Lerp(-ArcDegrees / 2f, ArcDegrees / 2f, t) * Mathf.Deg2Rad;
                // The arc bows toward the Shrine, so the stones face the plaza in a half ring.
                var position = new Vector3(Mathf.Sin(angle) * Radius, 0f, Mathf.Cos(angle) * Radius - Radius);
                var lit = profile == null || i < profile.RuneSlotsLit;
                var rune = profile?.RuneInSlot(i);
                var rank = rune != null ? profile.RuneRankOf(rune) : 0;
                Slot(i, position, Mathf.Rad2Deg * angle, lit, rune, rank);
            }
        }

        void Slot(int index, Vector3 position, float yaw, bool lit, RuneDefinition rune, int rank)
        {
            var slot = new GameObject($"Slot {index + 1}").transform;
            slot.SetParent(transform, false);
            slot.localPosition = position;
            slot.localRotation = Quaternion.Euler(0f, yaw + 180f, 0f);

            PrototypeVisuals.Part(PrimitiveType.Cylinder, slot, new Vector3(0f, 0.06f, 0f), new Vector3(0.5f, 0.06f, 0.5f), PrototypeVisuals.Lit(Plinth), "Plinth");
            if (!lit) return;

            var holder = new GameObject("Stone").transform;
            holder.SetParent(slot, false);
            var legendary = rune != null && rune.Rarity == Rarity.Legendary;
            holder.localPosition = new Vector3(0f, legendary ? 0.24f : 0.12f, 0f);
            if (legendary) floating.Add(holder);

            // A chunky stone, narrower at the top, tilted a little like it was set by hand.
            var stoneMaterial = PrototypeVisuals.Lit(Stone);
            var body = PrototypeVisuals.Part(PrimitiveType.Cube, holder, new Vector3(0f, 0.26f, 0f), new Vector3(0.3f, 0.5f, 0.16f), stoneMaterial, "Body");
            body.transform.localRotation = Quaternion.Euler(0f, 0f, (index % 2 == 0 ? 1f : -1f) * 3f);
            var cap = PrototypeVisuals.Part(PrimitiveType.Cube, holder, new Vector3(0f, 0.55f, 0f), new Vector3(0.22f, 0.12f, 0.14f), stoneMaterial, "Cap");
            cap.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
            if (rune == null) return;

            // The glyph: a glowing inlay on the stone's front, brighter at higher ranks.
            var glow = Color.Lerp(rune.Color, Color.white, 0.15f + 0.05f * rank);
            var inlay = PrototypeVisuals.Unlit(glow);
            var front = -0.085f;
            PrototypeVisuals.Part(PrimitiveType.Cube, holder, new Vector3(0f, 0.3f, front), new Vector3(0.05f, 0.28f, 0.01f), inlay, "Glyph");
            PrototypeVisuals.Part(PrimitiveType.Cube, holder, new Vector3(0f, 0.34f, front), new Vector3(0.18f, 0.04f, 0.01f), inlay, "Glyph Bar");
            for (var r = 0; r < rank; r++)
                PrototypeVisuals.Part(PrimitiveType.Cube, holder, new Vector3(-0.1f + r * 0.05f, 0.07f, front), new Vector3(0.025f, 0.025f, 0.01f), inlay, "Rank");
        }

        void Update()
        {
            var bob = Mathf.Sin(Time.time * 1.6f) * 0.04f;
            foreach (var stone in floating)
                if (stone != null)
                {
                    stone.localPosition = new Vector3(0f, 0.24f + bob, 0f);
                    stone.Rotate(0f, 20f * Time.deltaTime, 0f, Space.Self);
                }
        }
    }
}
