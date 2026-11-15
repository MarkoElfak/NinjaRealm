using System.Collections.Generic;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Presentation.Battle;
using UnityEngine;

namespace NinjaRealm.Presentation.Village
{
    /// <summary>
    /// The player's ninjas standing on the plaza in the middle of the village: every ninja they
    /// own, so every ninja they can put in a fight. Each one is a <see cref="UnitView"/> without
    /// its ring and bars, a little bigger than on the board, facing the camera and idling.
    /// </summary>
    public sealed class VillageNinjas : MonoBehaviour
    {
        /// <summary>How much bigger a ninja stands in the village than on the battle board.</summary>
        public const float Scale = 2.4f;

        /// <summary>Metres between ninjas, and between rows.</summary>
        public const float Spacing = 2.8f;

        /// <summary>Ninjas in a full row. The plaza is about 13 m across.</summary>
        public const int PerRow = 5;

        /// <summary>Radius of a ninja's click capsule, before <see cref="Scale"/>.</summary>
        public const float ClickRadius = 0.4f;

        readonly List<NinjaDefinition> ninjas = new List<NinjaDefinition>();
        readonly List<UnitView> views = new List<UnitView>();

        /// <summary>The ninjas standing here, in the order they stand.</summary>
        public IReadOnlyList<NinjaDefinition> Ninjas => ninjas;

        /// <summary>The view of each ninja, in the same order as <see cref="Ninjas"/>.</summary>
        public IReadOnlyList<UnitView> Views => views;

        /// <summary>
        /// Stands these ninjas in rows centred on a point, the first row nearest the camera.
        /// Does nothing when the same ninjas already stand there.
        /// </summary>
        public void Show(IReadOnlyList<NinjaDefinition> roster, Vector3 centre)
        {
            if (Same(roster)) return;

            foreach (var view in views)
                if (view != null) PrototypeVisuals.DestroySafe(view.gameObject);
            views.Clear();
            ninjas.Clear();

            foreach (var ninja in roster)
                if (ninja != null) ninjas.Add(ninja);

            for (var i = 0; i < ninjas.Count; i++)
            {
                var ninja = ninjas[i];
                var accent = ninja.Element != null ? ninja.Element.Color : Color.grey;
                var view = UnitView.Create(transform, i, 0, ninja.DisplayName, accent, PositionOf(i, ninjas.Count, centre), ninja.ModelPrefab);
                view.SetBarsVisible(false);
                view.SetRingVisible(false);

                // The village camera looks along +z, so the ninjas face -z to look at it.
                view.transform.rotation = Quaternion.LookRotation(Vector3.back);
                view.transform.localScale = Vector3.one * Scale;

                // A capsule round the model, so the ninja can be clicked.
                var capsule = view.gameObject.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, UnitView.ModelHeight * 0.5f, 0f);
                capsule.height = UnitView.ModelHeight;
                capsule.radius = ClickRadius;
                views.Add(view);
            }
        }

        bool Same(IReadOnlyList<NinjaDefinition> roster)
        {
            var count = 0;
            foreach (var ninja in roster)
            {
                if (ninja == null) continue;
                if (count >= ninjas.Count || ninjas[count] != ninja || views[count] == null) return false;
                count++;
            }

            return count == ninjas.Count;
        }

        /// <summary>
        /// Where ninja number <paramref name="index"/> of <paramref name="count"/> stands: rows of
        /// <see cref="PerRow"/>, each row centred, the rows centred on the point front to back.
        /// </summary>
        public static Vector3 PositionOf(int index, int count, Vector3 centre)
        {
            var rows = Mathf.Max(1, (count + PerRow - 1) / PerRow);
            var row = index / PerRow;
            var inRow = row < rows - 1 ? PerRow : count - (rows - 1) * PerRow;
            var column = index % PerRow;
            return centre + new Vector3((column - (inRow - 1) * 0.5f) * Spacing, 0f, (row - (rows - 1) * 0.5f) * Spacing);
        }

        /// <summary>The ninja a collider belongs to, or null. For clicks on the plaza.</summary>
        public NinjaDefinition NinjaOf(Collider collider)
        {
            if (collider == null) return null;
            var view = collider.GetComponentInParent<UnitView>();
            var index = view != null ? views.IndexOf(view) : -1;
            return index >= 0 ? ninjas[index] : null;
        }

        /// <summary>Where a ninja's name goes: above its head.</summary>
        public Vector3 LabelPosition(int index) => views[index].transform.position + Vector3.up * (UnitView.ModelHeight * Scale + 0.35f);

        void Update() => Tick(Time.deltaTime);

        /// <summary>Advances the idle animations. Also called by captures outside play mode.</summary>
        public void Tick(float deltaTime)
        {
            foreach (var view in views)
                if (view != null) view.Tick(deltaTime, null);
        }
    }
}
