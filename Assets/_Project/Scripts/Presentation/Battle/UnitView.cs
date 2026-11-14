using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// A placeholder ninja on the board: a chibi body in dark cloth with the element colour
    /// on the scarf and headband, a team ring, and health, shield and mana bars. It only
    /// animates what the simulation already decided.
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        const float BarWidth = 0.9f;
        const float BarHeight = 1.5f;
        const float HitFlashSeconds = 0.12f;
        const float AttackSeconds = 0.25f;
        const float CastSeconds = 0.45f;
        const float DeathSeconds = 0.6f;

        static readonly Color ClothColor = new Color(0.16f, 0.16f, 0.2f);
        static readonly Color SkinColor = new Color(0.96f, 0.8f, 0.66f);
        static readonly Color SteelColor = new Color(0.75f, 0.77f, 0.8f);

        public int Index { get; private set; }

        public int Team { get; private set; }

        public string DisplayName { get; private set; }

        public bool IsDead { get; private set; }

        /// <summary>Where the bars sit, for labels and floating numbers.</summary>
        public Vector3 BarsPosition => bars.position;

        Transform visual;
        Transform ring;
        Transform bars;
        Transform healthFill;
        Transform shieldFill;
        Transform manaFill;
        Material clothMaterial;

        Vector3 moveFrom;
        Vector3 moveTo;
        float moveTime = 1f;
        float moveDuration = 1f;

        Vector3 attackDirection;
        float attackTime = AttackSeconds;
        float flashTime = HitFlashSeconds;
        float castTime = CastSeconds;
        float deathTime;

        public static UnitView Create(Transform parent, int index, int team, string displayName, Color accent, Vector3 position)
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
            view.BuildBody(accent);
            view.BuildBars();
            return view;
        }

        void BuildBody(Color accent)
        {
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);

            clothMaterial = PrototypeVisuals.NewLit(ClothColor);
            var accentMaterial = PrototypeVisuals.Lit(accent);

            ring = PrototypeVisuals.Part(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.95f, 0.01f, 0.95f),
                PrototypeVisuals.Lit(PrototypeVisuals.TeamColor(Team)), "Team Ring").transform;

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
        }

        public void Hit() => flashTime = 0f;

        public void Cast() => castTime = 0f;

        public void Die()
        {
            IsDead = true;
            deathTime = 0f;
            bars.gameObject.SetActive(false);
            ring.gameObject.SetActive(false);
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
                clothMaterial.color = flashTime < HitFlashSeconds ? Color.white : ClothColor;
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
    }
}
