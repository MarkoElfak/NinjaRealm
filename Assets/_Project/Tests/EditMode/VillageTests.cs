using System;
using System.Collections.Generic;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Progression;
using NinjaRealm.Presentation.Village;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NinjaRealm.Tests
{
    /// <summary>Village buildings and plaza ninjas can be clicked, and the preview stage shows one ninja.</summary>
    public class VillageTests
    {
        GameObject building;
        GameObject cameraObject;

        readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void DestroyObjects()
        {
            foreach (var item in created)
                if (item != null) Object.DestroyImmediate(item);
            created.Clear();
            if (building != null) Object.DestroyImmediate(building);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
        }

        VillageBuilding Create(VillageBuildingKind kind, Vector3 position)
        {
            building = new GameObject("Building");
            building.transform.position = position;
            var component = building.AddComponent<VillageBuilding>();
            component.SetKind(kind);
            component.EnsureBuilt();
            return component;
        }

        [Test]
        public void ABuildingWithoutAModelGetsAPlaceholderAndACollider()
        {
            var dojo = Create(VillageBuildingKind.Dojo, new Vector3(3f, 0f, 2f));

            Assert.That(dojo.GetComponentsInChildren<Renderer>(), Is.Not.Empty);
            var box = dojo.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null);
            Assert.That(box.size.y, Is.EqualTo(4.5f).Within(0.3f), "The placeholder is about 4.5 m tall by default.");
            Assert.That(dojo.LabelPosition.y, Is.GreaterThan(4.5f), "The label sits above the roof.");
        }

        [Test]
        public void EnsureBuiltIsSafeToCallAgain()
        {
            var shrine = Create(VillageBuildingKind.Shrine, Vector3.zero);
            var parts = shrine.GetComponentsInChildren<Renderer>().Length;

            shrine.EnsureBuilt();

            Assert.That(shrine.GetComponentsInChildren<Renderer>().Length, Is.EqualTo(parts));
            Assert.That(shrine.GetComponents<Collider>().Length, Is.EqualTo(1));
        }

        [Test]
        public void ClickingABuildingFindsIt()
        {
            var hall = Create(VillageBuildingKind.CommandHall, new Vector3(0f, 0f, 10f));
            cameraObject = new GameObject("Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 2f, 0f), Quaternion.identity);
            Physics.SyncTransforms();

            var centre = camera.WorldToScreenPoint(hall.GetComponent<BoxCollider>().bounds.center);

            Assert.That(VillageController.BuildingAt(camera, centre), Is.SameAs(hall));
            Assert.That(VillageController.BuildingAt(camera, new Vector2(1f, 1f)), Is.Null);
        }

        [Test]
        public void NinjasStandInCentredRows()
        {
            var centre = new Vector3(0f, 0f, 2f);

            // Seven ninjas: a full row of five in front, two behind.
            Assert.That(VillageNinjas.PositionOf(2, 7, centre).x, Is.EqualTo(0f).Within(0.001f), "The middle of the front row is on the centre line.");
            Assert.That(VillageNinjas.PositionOf(0, 7, centre).x, Is.EqualTo(-2f * VillageNinjas.Spacing).Within(0.001f));
            Assert.That(VillageNinjas.PositionOf(0, 7, centre).z, Is.LessThan(centre.z), "The first row is nearest the camera.");
            Assert.That(VillageNinjas.PositionOf(5, 7, centre).z, Is.GreaterThan(centre.z));
            Assert.That(VillageNinjas.PositionOf(5, 7, centre).x, Is.EqualTo(-VillageNinjas.PositionOf(6, 7, centre).x).Within(0.001f), "The short row is centred too.");

            // One ninja stands on the centre.
            Assert.That(VillageNinjas.PositionOf(0, 1, centre), Is.EqualTo(centre));
        }

        [Test]
        public void TheVillageShowsEachNinjaOnceFacingTheCamera()
        {
            var holder = new GameObject("Ninjas");
            created.Add(holder);
            var ninjas = holder.AddComponent<VillageNinjas>();
            var roster = new List<NinjaDefinition>();
            for (var i = 0; i < 3; i++)
            {
                var ninja = ScriptableObject.CreateInstance<NinjaDefinition>();
                created.Add(ninja);
                roster.Add(ninja);
            }

            ninjas.Show(roster, Vector3.zero);
            var first = ninjas.Views[0];
            ninjas.Show(roster, Vector3.zero);

            Assert.That(ninjas.Views.Count, Is.EqualTo(3));
            Assert.That(ninjas.Views[0], Is.SameAs(first), "Showing the same ninjas again keeps them.");
            Assert.That(Vector3.Dot(first.transform.forward, Vector3.back), Is.EqualTo(1f).Within(0.001f));
            Assert.That(holder.transform.childCount, Is.EqualTo(3));

            roster.RemoveAt(2);
            ninjas.Show(roster, Vector3.zero);
            Assert.That(ninjas.Views.Count, Is.EqualTo(2));
        }

        [Test]
        public void EveryKindHasANameAndDescription()
        {
            foreach (VillageBuildingKind kind in Enum.GetValues(typeof(VillageBuildingKind)))
            {
                Assert.That(VillageBuilding.NameOf(kind), Is.Not.Empty, kind.ToString());
                Assert.That(VillageBuilding.DescriptionOf(kind), Is.Not.Empty, kind.ToString());
            }

            Assert.That(VillageBuilding.IsOpenKind(VillageBuildingKind.Dojo), Is.True);
            Assert.That(VillageBuilding.IsOpenKind(VillageBuildingKind.Mint), Is.True, "The Mint is open now that it makes Gold.");
        }

        [Test]
        public void OnlyTheMintAndTheSteelMineHaveIncome()
        {
            Assert.That(VillageBuilding.IncomeIdOf(VillageBuildingKind.Mint), Is.EqualTo(IncomeIds.Mint));
            Assert.That(VillageBuilding.IncomeIdOf(VillageBuildingKind.SteelMine), Is.EqualTo(IncomeIds.SteelMine));
            foreach (VillageBuildingKind kind in Enum.GetValues(typeof(VillageBuildingKind)))
                if (kind != VillageBuildingKind.Mint && kind != VillageBuildingKind.SteelMine)
                    Assert.That(VillageBuilding.IncomeIdOf(kind), Is.Null, kind.ToString());
        }

        [Test]
        public void WaitingTimesReadInHoursAndMinutes()
        {
            Assert.That(VillageScreen.Duration(TimeSpan.FromSeconds(20)), Is.EqualTo("less than a minute"));
            Assert.That(VillageScreen.Duration(TimeSpan.FromMinutes(42)), Is.EqualTo("42 min"));
            Assert.That(VillageScreen.Duration(new TimeSpan(7, 5, 59)), Is.EqualTo("7 h 5 min"));
        }

        [Test]
        public void APlazaNinjaCanBeClicked()
        {
            var holder = new GameObject("Ninjas");
            created.Add(holder);
            var ninjas = holder.AddComponent<VillageNinjas>();
            var roster = new List<NinjaDefinition>();
            for (var i = 0; i < 2; i++)
            {
                var ninja = ScriptableObject.CreateInstance<NinjaDefinition>();
                created.Add(ninja);
                roster.Add(ninja);
            }

            // Two ninjas stand either side of the centre, in front of a camera looking along +z.
            ninjas.Show(roster, new Vector3(0f, 0f, 10f));
            cameraObject = new GameObject("Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, 0f), Quaternion.identity);
            Physics.SyncTransforms();

            var capsule = ninjas.Views[1].GetComponent<CapsuleCollider>();
            Assert.That(capsule, Is.Not.Null);
            var under = VillageController.ColliderAt(camera, camera.WorldToScreenPoint(capsule.bounds.center));

            Assert.That(ninjas.NinjaOf(under), Is.SameAs(roster[1]));
            Assert.That(ninjas.NinjaOf(null), Is.Null);
            Assert.That(VillageController.BuildingAt(camera, camera.WorldToScreenPoint(capsule.bounds.center)), Is.Null, "A ninja is not a building.");
        }

        [Test]
        public void ThePreviewShowsOneNinjaAtATime()
        {
            var holder = new GameObject("Preview");
            created.Add(holder);
            var preview = holder.AddComponent<NinjaPreview>();
            var first = ScriptableObject.CreateInstance<NinjaDefinition>();
            var second = ScriptableObject.CreateInstance<NinjaDefinition>();
            created.Add(first);
            created.Add(second);

            preview.Show(first);
            var view = preview.View;
            preview.Show(first);

            Assert.That(preview.Shown, Is.SameAs(first));
            Assert.That(preview.View, Is.SameAs(view), "Showing the same ninja again keeps it.");
            Assert.That(preview.Texture, Is.Not.Null);
            Assert.That(preview.Texture.width, Is.EqualTo(NinjaPreview.TextureWidth));
            Assert.That(Vector3.Distance(view.transform.position, NinjaPreview.StagePosition), Is.LessThan(0.001f), "The stage is far from the village.");

            // It turns by itself, and by hand.
            preview.Tick(1f);
            Assert.That(preview.Yaw, Is.EqualTo(NinjaPreview.StartYaw + NinjaPreview.TurnSpeed).Within(0.001f));
            preview.Turn(90f);
            preview.Tick(1f);
            Assert.That(preview.Yaw, Is.EqualTo(NinjaPreview.StartYaw + NinjaPreview.TurnSpeed + 90f).Within(0.001f), "It holds still after a turn by hand.");

            preview.Show(second);
            Assert.That(preview.Shown, Is.SameAs(second));
            Assert.That(preview.View, Is.Not.SameAs(view));
            Assert.That(preview.Yaw, Is.EqualTo(NinjaPreview.StartYaw));

            preview.Hide();
            Assert.That(preview.Shown, Is.Null);
            Assert.That(preview.View, Is.Null);
            preview.Release();
        }
    }
}
