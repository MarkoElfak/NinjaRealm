using System.Collections.Generic;
using System.Linq;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Prototype;
using NinjaRealm.Presentation.Battle;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace NinjaRealm.Editor
{
    /// <summary>
    /// Puts the Meshy ninja models into fights. Builds the shared ninja animator controller from
    /// the clips in <see cref="MeshyArtImporter.AnimationsRoot"/>, makes a prefab for every ninja
    /// with a model at Characters/Name/Name.fbx, and sets it as that ninja's model.
    /// </summary>
    public static class NinjaModelSetup
    {
        public const string ControllerPath = MeshyArtImporter.AnimationsRoot + "/NinjaAnimator.controller";

        const float BlendSeconds = 0.1f;

        [MenuItem("Ninja Realm/Art/Set Up Ninja Models")]
        public static void SetUp()
        {
            var controller = BuildController(out var missingClips);

            var linked = new List<string>();
            var withoutModel = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(NinjaDefinition)))
            {
                var ninja = AssetDatabase.LoadAssetAtPath<NinjaDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (ninja == null) continue;

                var prefab = BuildPrefab(ninja, controller);
                if (prefab == null)
                {
                    withoutModel.Add(ninja.DisplayName);
                    continue;
                }

                using (var serialized = new SerializedObject(ninja))
                {
                    serialized.FindProperty("modelPrefab").objectReferenceValue = prefab;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                EditorUtility.SetDirty(ninja);
                linked.Add(ninja.DisplayName);
            }

            AssetDatabase.SaveAssets();

            linked.Sort(System.StringComparer.Ordinal);
            withoutModel.Sort(System.StringComparer.Ordinal);
            Debug.Log($"[Meshy] Ninja models set for {linked.Count}: {string.Join(", ", linked)}." +
                      (withoutModel.Count > 0 ? $" No model yet for {string.Join(", ", withoutModel)}." : "") +
                      (controller == null ? " No Idle clip yet, so the models are not animated." :
                          missingClips.Count > 0 ? $" Missing clips: {string.Join(", ", missingClips)}." : ""));
        }

        /// <summary>Characters/Name/Name.fbx for a ninja, named like its content asset.</summary>
        public static string ModelPath(NinjaDefinition ninja)
        {
            var name = PrototypeContent.FileName(ninja.DisplayName);
            return $"{MeshyArtImporter.CharactersRoot}/{name}/{name}.fbx";
        }

        /// <summary>
        /// Saves Name.prefab next to the ninja's model, with the shared controller on its
        /// animator. Returns null when the ninja has no model yet.
        /// </summary>
        static GameObject BuildPrefab(NinjaDefinition ninja, RuntimeAnimatorController controller)
        {
            var modelPath = ModelPath(ninja);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                var animator = instance.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.runtimeAnimatorController = animator.avatar != null && animator.avatar.isValid ? controller : null;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                var prefabPath = System.IO.Path.ChangeExtension(modelPath, ".prefab");
                return PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Builds the controller in place, keeping the asset so prefabs stay linked. Returns null
        /// when there is no Idle clip, since every other state returns to Idle.
        /// </summary>
        static AnimatorController BuildController(out List<string> missingClips)
        {
            var clips = new Dictionary<string, AnimationClip>();
            missingClips = new List<string>();
            foreach (var state in NinjaAnimation.States)
            {
                var clip = LoadClip($"{MeshyArtImporter.AnimationsRoot}/{state}.fbx");
                if (clip != null) clips[state] = clip;
                else missingClips.Add(state);
            }

            if (!clips.ContainsKey(NinjaAnimation.IdleState)) return null;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            foreach (var parameter in controller.parameters) controller.RemoveParameter(parameter);
            controller.AddParameter(NinjaAnimation.Moving, AnimatorControllerParameterType.Bool);
            controller.AddParameter(NinjaAnimation.Attack, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(NinjaAnimation.Cast, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(NinjaAnimation.Hit, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(NinjaAnimation.Die, AnimatorControllerParameterType.Trigger);

            var machine = controller.layers[0].stateMachine;
            foreach (var transition in machine.anyStateTransitions) machine.RemoveAnyStateTransition(transition);
            foreach (var child in machine.states) machine.RemoveState(child.state);

            var states = new Dictionary<string, AnimatorState>();
            for (var i = 0; i < NinjaAnimation.States.Length; i++)
            {
                var name = NinjaAnimation.States[i];
                if (!clips.TryGetValue(name, out var clip)) continue;

                var state = machine.AddState(name, new Vector3(300f, i * 70f, 0f));
                state.motion = clip;
                var target = NinjaAnimation.TargetSeconds(name);
                if (target > 0f && clip.length > target) state.speed = clip.length / target;
                states[name] = state;
            }

            var idle = states[NinjaAnimation.IdleState];
            machine.defaultState = idle;

            if (states.TryGetValue(NinjaAnimation.RunState, out var run))
            {
                AddTransition(idle, run, NinjaAnimation.Moving, AnimatorConditionMode.If);
                AddTransition(run, idle, NinjaAnimation.Moving, AnimatorConditionMode.IfNot);
            }

            // Attacks and casts can start from anything but death (the dead get no triggers) and
            // restart themselves, since fast attackers swing again before the clip ends.
            foreach (var (stateName, trigger) in new[] { (NinjaAnimation.AttackState, NinjaAnimation.Attack), (NinjaAnimation.CastState, NinjaAnimation.Cast) })
            {
                if (!states.TryGetValue(stateName, out var state)) continue;
                var transition = machine.AddAnyStateTransition(state);
                transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                transition.hasExitTime = false;
                transition.duration = BlendSeconds;
                transition.canTransitionToSelf = true;
                ReturnToIdle(state, idle);
            }

            if (states.TryGetValue(NinjaAnimation.HitState, out var hit))
            {
                AddTransition(idle, hit, NinjaAnimation.Hit, AnimatorConditionMode.If);
                if (run != null) AddTransition(run, hit, NinjaAnimation.Hit, AnimatorConditionMode.If);
                ReturnToIdle(hit, idle);
            }

            if (states.TryGetValue(NinjaAnimation.DeathState, out var death))
            {
                var transition = machine.AddAnyStateTransition(death);
                transition.AddCondition(AnimatorConditionMode.If, 0f, NinjaAnimation.Die);
                transition.hasExitTime = false;
                transition.duration = BlendSeconds;
                transition.canTransitionToSelf = false;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        static void AddTransition(AnimatorState from, AnimatorState to, string parameter, AnimatorConditionMode mode)
        {
            var transition = from.AddTransition(to);
            transition.AddCondition(mode, 0f, parameter);
            transition.hasExitTime = false;
            transition.duration = BlendSeconds;
        }

        static void ReturnToIdle(AnimatorState state, AnimatorState idle)
        {
            var transition = state.AddTransition(idle);
            transition.hasExitTime = true;
            transition.exitTime = 0.9f;
            transition.duration = BlendSeconds;
        }

        static AnimationClip LoadClip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", System.StringComparison.Ordinal));
    }
}
