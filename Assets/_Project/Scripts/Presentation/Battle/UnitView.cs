using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// A ninja on the board: its model (or, without one, a placeholder chibi body in dark cloth
    /// with the element colour on the scarf and headband), a team ring, and health, shield and
    /// mana bars. It only animates what the simulation already decided. A model with an
    /// <see cref="Animator"/> plays the <see cref="NinjaAnimation"/> states.
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        const float BarWidth = 0.9f;
        const float BarHeight = 1.5f;
        const float HitFlashSeconds = 0.12f;
        const float AttackSeconds = 0.25f;
        const float CastSeconds = 0.45f;
        const float DeathSeconds = 0.6f;
        const float ModelDeathSeconds = 1.6f;
        const float ModelSinkSeconds = 0.5f;

        /// <summary>A model's height on the board in metres (docs/ART_STYLE.md).</summary>
        public const float ModelHeight = 1.2f;

        static readonly Color ClothColor = new Color(0.16f, 0.16f, 0.2f);
        static readonly Color SkinColor = new Color(0.96f, 0.8f, 0.66f);
        static readonly Color SteelColor = new Color(0.75f, 0.77f, 0.8f);
        static readonly Color ModelHitTint = new Color(1f, 0.55f, 0.5f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public int Index { get; private set; }

        public int Team { get; private set; }

        public string DisplayName { get; private set; }

        public bool IsDead { get; private set; }

        /// <summary>True when the ninja shows its model instead of the placeholder body.</summary>
        public bool HasModel => model != null;

        /// <summary>The model's animator, if it has one.</summary>
        public Animator Animator => animator;

        /// <summary>Speeds up model animations along with the fight's playback speed.</summary>
        public float AnimationSpeed { get; set; } = 1f;

        /// <summary>Where the bars sit, for labels and floating numbers.</summary>
        public Vector3 BarsPosition => bars.position;

        Transform visual;
        Transform ring;
        Transform bars;
        Transform healthFill;
        Transform shieldFill;
        Transform manaFill;
        Material clothMaterial;
        Transform model;
        Animator animator;
        Renderer[] modelRenderers;
        MaterialPropertyBlock tintBlock;
        readonly System.Collections.Generic.HashSet<int> animatorParameters = new System.Collections.Generic.HashSet<int>();
        bool animatorParametersRead;

        Vector3 moveFrom;
        Vector3 moveTo;
        float moveTime = 1f;
        float moveDuration = 1f;

        Vector3 attackDirection;
        float attackTime = AttackSeconds;
        float flashTime = HitFlashSeconds;
        float castTime = CastSeconds;
        float deathTime;

        /// <summary>Creates a unit. With a model prefab it shows that model; without one, the placeholder body.</summary>
        public static UnitView Create(Transform parent, int index, int team, string displayName, Color accent, Vector3 position,
            GameObject modelPrefab = null)
        {
            var root = new GameObject($"{index} {displayName}");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.LookRotation(team == 0 ? Vector3.forward : Vector3.back);

            var view = root.AddComponent<UnitView>();
            view.Index = index;
            view.Team = team;
            view.DisplayName = displayName;
            view.moveFrom = view.moveTo = position;
            view.BuildRing();
            if (modelPrefab != null) view.BuildModel(modelPrefab);
            else view.BuildBody(accent);
            view.BuildBars();
            return view;
        }

        void BuildRing()
        {
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);

            ring = PrototypeVisuals.Part(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.95f, 0.01f, 0.95f),
                PrototypeVisuals.Lit(PrototypeVisuals.TeamColor(Team)), "Team Ring").transform;
        }

        /// <summary>
        /// Puts a copy of the model under the visual, scaled to <see cref="ModelHeight"/>, standing
        /// on the ground and centred on the unit. Meshy models face +Z, which is the unit's forward.
        /// </summary>
        void BuildModel(GameObject prefab)
        {
            model = Instantiate(prefab, visual, false).transform;
            model.name = "Model";
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;

            modelRenderers = model.GetComponentsInChildren<Renderer>();
            foreach (var renderer in modelRenderers)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }

            if (modelRenderers.Length > 0)
            {
                var bounds = LocalBounds();
                if (bounds.size.y > 0.0001f) model.localScale *= ModelHeight / bounds.size.y;
                bounds = LocalBounds();
                model.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            }

            animator = model.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        /// <summary>The model's renderer bounds in the visual's space.</summary>
        Bounds LocalBounds()
        {
            var first = true;
            var bounds = new Bounds();
            foreach (var renderer in modelRenderers)
            {
                var world = renderer.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = world.center + Vector3.Scale(world.extents, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1) * 2f - Vector3.one);
                    var local = visual.InverseTransformPoint(corner);
                    if (first) bounds = new Bounds(local, Vector3.zero);
                    else bounds.Encapsulate(local);
                    first = false;
                }
            }

            return bounds;
        }

        void BuildBody(Color accent)
        {
            clothMaterial = PrototypeVisuals.NewLit(ClothColor);
            var accentMaterial = PrototypeVisuals.Lit(accent);

            // Chibi proportions: a short body and a big head.
            PrototypeVisuals.Part(PrimitiveType.Capsule, visual, new Vector3(0f, 0.42f, 0f), new Vector3(0.5f, 0.38f, 0.42f), clothMaterial, "Body");
            PrototypeVisuals.Part(PrimitiveType.Sphere, visual, new Vector3(0f, 0.98f, 0f), new Vector3(0.5f, 0.5f, 0.5f), clothMaterial, "Head");
            PrototypeVisuals.Part(PrimitiveType.Cube, visual, new Vector3(0f, 1.0f, 0.2f), new Vector3(0.34f, 0.1f, 0.14f), PrototypeVisuals.Lit(SkinColor), "Eyes");
            PrototypeVisuals.Part(PrimitiveType.Cylinder, visual, new Vector3(0f, 1.1f, 0f), new Vector3(0.52f, 0.035f, 0.52f), accentMaterial, "Headband");
            PrototypeVisuals.Part(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.74f, 0f), new Vector3(0.46f, 0.05f, 0.46f), accentMaterial, "Scarf");
            PrototypeVisuals.Part(PrimitiveType.Cube, visual, new Vector3(0.38f, 0.45f, 0.12f), new Vector3(0.05f, 0.05f, 0.7f), PrototypeVisuals.Lit(SteelColor), "Blade");
        }

        void BuildBars()
        {
            bars = new GameObject("Bars").transform;
            bars.SetParent(transform, false);
            bars.localPosition = new Vector3(0f, BarHeight, 0f);

            Transform Quad(string name, float y, float height, float z, Color color)
            {
                var quad = PrototypeVisuals.Part(PrimitiveType.Quad, bars, new Vector3(0f, y, z), new Vector3(BarWidth, height, 1f),
                    PrototypeVisuals.Unlit(color), name);
                quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return quad.transform;
            }

            Quad("Background", -0.015f, 0.17f, 0f, new Color(0.08f, 0.08f, 0.1f));
            healthFill = Quad("Health", 0.02f, 0.09f, -0.01f, Team == 0 ? new Color(0.3f, 0.85f, 0.35f) : new Color(0.9f, 0.25f, 0.2f));
            shieldFill = Quad("Shield", 0.02f, 0.09f, -0.01f, new Color(0.92f, 0.92f, 0.92f));
            manaFill = Quad("Mana", -0.065f, 0.04f, -0.01f, new Color(0.3f, 0.6f, 1f));
        }

        /// <summary>Bar fill as fractions: health and shield of max health, mana of max mana.</summary>
        public void SetBars(float health, float shield, float mana)
        {
            var total = Mathf.Max(1f, health + shield);
            var healthWidth = Mathf.Clamp01(health / total);
            var shieldWidth = Mathf.Clamp01(shield / total);
            SetFill(healthFill, 0f, healthWidth);
            SetFill(shieldFill, healthWidth, shieldWidth);
            SetFill(manaFill, 0f, Mathf.Clamp01(mana));
        }

        static void SetFill(Transform fill, float start, float width)
        {
            fill.gameObject.SetActive(width > 0.001f);
            var scale = fill.localScale;
            scale.x = BarWidth * width;
            fill.localScale = scale;
            var position = fill.localPosition;
            position.x = BarWidth * (start + width * 0.5f - 0.5f);
            fill.localPosition = position;
        }

        /// <summary>Puts the unit somewhere at once, without animating, for example while dragging it.</summary>
        public void Teleport(Vector3 position)
        {
            moveFrom = moveTo = position;
            moveTime = moveDuration;
            transform.position = position;
            if (visual != null) visual.position = position;
        }

        public void SetBarsVisible(bool visible) => bars.gameObject.SetActive(visible && !IsDead);

        public void MoveTo(Vector3 position, float seconds)
        {
            moveFrom = transform.position;
            moveTo = position;
            moveDuration = Mathf.Max(0.05f, seconds);
            moveTime = 0f;
            Face(position);
        }

        public void Attack(Vector3 targetPosition)
        {
            Face(targetPosition);
            attackDirection = targetPosition - moveTo;
            attackDirection.y = 0f;
            attackDirection = attackDirection.sqrMagnitude > 0.0001f ? attackDirection.normalized : transform.forward;
            attackTime = 0f;
            Trigger(NinjaAnimation.AttackHash);
        }

        public void Hit()
        {
            flashTime = 0f;

            // Only flinch from Idle or Run, so a hit doesn't cut an attack or cast short.
            if (HasParameter(NinjaAnimation.HitHash) && !animator.IsInTransition(0))
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName(NinjaAnimation.IdleState) || state.IsName(NinjaAnimation.RunState)) Trigger(NinjaAnimation.HitHash);
            }
        }

        public void Cast()
        {
            castTime = 0f;
            Trigger(NinjaAnimation.CastHash);
        }

        public void Die()
        {
            if (IsDead) return;
            IsDead = true;
            deathTime = 0f;
            Trigger(NinjaAnimation.DieHash);
            bars.gameObject.SetActive(false);
            ring.gameObject.SetActive(false);
        }

        /// <summary>Animated models only: sets a trigger the controller has. Others ignore it. The dead only die.</summary>
        void Trigger(int parameter)
        {
            if (!HasParameter(parameter)) return;
            if (IsDead && parameter != NinjaAnimation.DieHash) return;
            animator.SetTrigger(parameter);
        }

        /// <summary>
        /// True when the model's controller has this parameter. The animator only lists its
        /// parameters once it is initialized, so they are read on first use.
        /// </summary>
        bool HasParameter(int parameter)
        {
            if (animator == null || animator.runtimeAnimatorController == null || !animator.isInitialized) return false;
            if (!animatorParametersRead)
            {
                foreach (var entry in animator.parameters) animatorParameters.Add(entry.nameHash);
                animatorParametersRead = true;
            }

            return animatorParameters.Contains(parameter);
        }

        /// <summary>The hit flash: the placeholder turns white, a model takes a red tint.</summary>
        void SetHitFlash(bool on)
        {
            if (clothMaterial != null) PrototypeVisuals.SetColor(clothMaterial, on ? Color.white : ClothColor);
            if (modelRenderers == null) return;

            tintBlock ??= new MaterialPropertyBlock();
            foreach (var renderer in modelRenderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(tintBlock);
                tintBlock.SetColor(BaseColorId, on ? ModelHitTint : Color.white);
                renderer.SetPropertyBlock(tintBlock);
            }
        }

        void Face(Vector3 position)
        {
            var direction = position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(direction);
        }

        /// <summary>Advances the animations. Called by BattlePlayback, so it also works outside play mode.</summary>
        public void Tick(float deltaTime, Camera viewCamera)
        {
            if (model != null)
            {
                TickModel(deltaTime, viewCamera);
                return;
            }

            moveTime = Mathf.Min(moveDuration, moveTime + deltaTime);
            var t = moveTime / moveDuration;
            var position = Vector3.Lerp(moveFrom, moveTo, t);
            var hop = moveTime < moveDuration ? Mathf.Sin(t * Mathf.PI) * 0.15f : 0f;
            transform.position = position;

            var lunge = Vector3.zero;
            if (attackTime < AttackSeconds)
            {
                attackTime += deltaTime;
                lunge = attackDirection * (Mathf.Sin(Mathf.Clamp01(attackTime / AttackSeconds) * Mathf.PI) * 0.35f);
            }

            var castScale = 1f;
            if (castTime < CastSeconds)
            {
                castTime += deltaTime;
                var c = Mathf.Clamp01(castTime / CastSeconds);
                castScale = 1f + Mathf.Sin(c * Mathf.PI) * 0.25f;
                hop += Mathf.Sin(c * Mathf.PI) * 0.3f;
            }

            if (flashTime < HitFlashSeconds)
            {
                flashTime += deltaTime;
                SetHitFlash(flashTime < HitFlashSeconds);
            }

            var deathScale = 1f;
            if (IsDead)
            {
                deathTime += deltaTime;
                var d = Mathf.Clamp01(deathTime / DeathSeconds);
                deathScale = 1f - d;
                hop -= d * 0.5f;
                if (d >= 1f && visual.gameObject.activeSelf) visual.gameObject.SetActive(false);
            }

            visual.position = transform.position + lunge + Vector3.up * hop;
            visual.localScale = Vector3.one * (castScale * deathScale);

            if (viewCamera != null) bars.rotation = viewCamera.transform.rotation;
        }

        /// <summary>
        /// A model moves with the animator's Run state and a small lunge on attacks. Its clips do
        /// the rest. After death it lies down, then sinks into the board.
        /// </summary>
        void TickModel(float deltaTime, Camera viewCamera)
        {
            moveTime = Mathf.Min(moveDuration, moveTime + deltaTime);
            transform.position = Vector3.Lerp(moveFrom, moveTo, moveTime / moveDuration);

            var lunge = Vector3.zero;
            if (attackTime < AttackSeconds)
            {
                attackTime += deltaTime;
                lunge = attackDirection * (Mathf.Sin(Mathf.Clamp01(attackTime / AttackSeconds) * Mathf.PI) * 0.15f);
            }

            if (flashTime < HitFlashSeconds)
            {
                flashTime += deltaTime;
                SetHitFlash(flashTime < HitFlashSeconds);
            }

            var sink = 0f;
            if (IsDead)
            {
                deathTime += deltaTime;
                var lying = HasParameter(NinjaAnimation.DieHash) ? ModelDeathSeconds : DeathSeconds;
                var sunk = Mathf.Clamp01((deathTime - lying) / ModelSinkSeconds);
                sink = sunk * ModelHeight;
                if (sunk >= 1f && visual.gameObject.activeSelf) visual.gameObject.SetActive(false);
            }

            visual.position = transform.position + lunge + Vector3.down * sink;

            if (animator != null && animator.runtimeAnimatorController != null && visual.gameObject.activeSelf)
            {
                if (HasParameter(NinjaAnimation.MovingHash))
                    animator.SetBool(NinjaAnimation.MovingHash, !IsDead && moveTime < moveDuration);

                // Play mode updates the animator by itself; captures outside play mode step it here.
                animator.speed = AnimationSpeed;
                if (!Application.isPlaying) animator.Update(deltaTime);
            }

            if (viewCamera != null) bars.rotation = viewCamera.transform.rotation;
        }
    }
}
