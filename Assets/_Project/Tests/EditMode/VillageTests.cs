using System;
using NinjaRealm.Presentation.Village;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NinjaRealm.Tests
{
    /// <summary>Village buildings get a placeholder and a collider, and can be clicked.</summary>
    public class VillageTests
    {
        GameObject building;
        GameObject cameraObject;

        [TearDown]
        public void DestroyObjects()
        {
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
        public void EveryKindHasANameAndDescription()
        {
            foreach (VillageBuildingKind kind in Enum.GetValues(typeof(VillageBuildingKind)))
            {
                Assert.That(VillageBuilding.NameOf(kind), Is.Not.Empty, kind.ToString());
                Assert.That(VillageBuilding.DescriptionOf(kind), Is.Not.Empty, kind.ToString());
            }

            Assert.That(VillageBuilding.IsOpenKind(VillageBuildingKind.Dojo), Is.True);
            Assert.That(VillageBuilding.IsOpenKind(VillageBuildingKind.Mint), Is.False);
        }
    }
}
