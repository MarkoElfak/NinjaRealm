using NinjaRealm.Presentation.Battle;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace NinjaRealm.Tests
{
    /// <summary>A unit shows its model when it has one, fits it to the board, and drives its animator.</summary>
    public class UnitViewTests
    {
        GameObject parent;
        GameObject model;

        [SetUp]
        public void CreateParent()
        {
            parent = new GameObject("Units");
        }

        [TearDown]
        public void DestroyObjects()
        {
            Object.DestroyImmediate(parent);
            if (model != null) Object.DestroyImmediate(model);
        }

        /// <summary>A stand-in for a Meshy model: 3 m tall, its pivot off to one side and above its feet.</summary>
        GameObject CreateModel()
        {
            model = new GameObject("Test Model");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(model.transform, false);
            body.transform.localPosition = new Vector3(0.5f, 2f, 0f);
            body.transform.localScale = new Vector3(1f, 3f, 1f);
            return model;
        }

        UnitView Create(GameObject prefab, Vector3 position) =>
            UnitView.Create(parent.transform, 0, 0, "Kaito", Color.red, position, prefab);

        [Test]
        public void WithoutModel_ShowsThePlaceholder()
        {
            var view = Create(null, Vector3.zero);

            Assert.IsFalse(view.HasModel);
            Assert.IsNotNull(view.transform.Find("Visual/Body"));
        }

        [Test]
        public void WithModel_FitsItToTheBoard()
        {
            var position = new Vector3(2f, 0f, -1f);
            var view = Create(CreateModel(), position);

            Assert.IsTrue(view.HasModel);
            Assert.IsNull(view.transform.Find("Visual/Body"));

            var bounds = view.transform.Find("Visual/Model").GetComponentInChildren<Renderer>().bounds;
            Assert.AreEqual(UnitView.ModelHeight, bounds.size.y, 0.001f, "height");
            Assert.AreEqual(position.y, bounds.min.y, 0.001f, "feet on the ground");
            Assert.AreEqual(position.x, bounds.center.x, 0.001f, "centred on x");
            Assert.AreEqual(position.z, bounds.center.z, 0.001f, "centred on z");
        }

        [Test]
        public void WithModel_SinksAwayAfterDying()
        {
            var view = Create(CreateModel(), Vector3.zero);
            view.Attack(Vector3.forward);
            view.Hit();
            view.Cast();
            view.Die();

            for (var i = 0; i < 40; i++) view.Tick(0.1f, null);

            Assert.IsTrue(view.IsDead);
            Assert.IsFalse(view.transform.Find("Visual").gameObject.activeSelf);
        }

        [Test]
        public void Animator_PlaysAttackAndIgnoresHitsMidSwing()
        {
            var controller = new AnimatorController();
            controller.AddLayer("Base");
            controller.AddParameter(NinjaAnimation.Attack, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(NinjaAnimation.Hit, AnimatorControllerParameterType.Trigger);
            var machine = controller.layers[0].stateMachine;
            var idle = machine.AddState(NinjaAnimation.IdleState);
            var attack = machine.AddState(NinjaAnimation.AttackState);
            var hit = machine.AddState(NinjaAnimation.HitState);
            idle.motion = attack.motion = hit.motion = new AnimationClip { name = "Still" };
            machine.defaultState = idle;
            var toAttack = machine.AddAnyStateTransition(attack);
            toAttack.AddCondition(AnimatorConditionMode.If, 0f, NinjaAnimation.Attack);
            toAttack.duration = 0f;
            var toHit = idle.AddTransition(hit);
            toHit.AddCondition(AnimatorConditionMode.If, 0f, NinjaAnimation.Hit);
            toHit.duration = 0f;

            var prefab = CreateModel();
            prefab.AddComponent<Animator>().runtimeAnimatorController = controller;
            var view = Create(prefab, Vector3.zero);
            try
            {
                view.Tick(0.05f, null);
                view.Attack(Vector3.forward);
                view.Tick(0.05f, null);
                view.Tick(0.05f, null);
                Assert.IsTrue(view.Animator.GetCurrentAnimatorStateInfo(0).IsName(NinjaAnimation.AttackState), "attacking");

                view.Hit();
                view.Tick(0.05f, null);
                view.Tick(0.05f, null);
                Assert.IsTrue(view.Animator.GetCurrentAnimatorStateInfo(0).IsName(NinjaAnimation.AttackState), "still attacking");
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }
    }
}
