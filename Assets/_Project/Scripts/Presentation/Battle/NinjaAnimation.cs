using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// The animator parameters and states a ninja model uses. The editor tool that builds the
    /// shared ninja animator controller and <see cref="UnitView"/> both read these names.
    /// </summary>
    public static class NinjaAnimation
    {
        // Parameters.
        public const string Moving = "Moving";
        public const string Attack = "Attack";
        public const string Cast = "Cast";
        public const string Hit = "Hit";
        public const string Die = "Die";

        // States, named after the clip files under Art/Meshy/Animations.
        public const string IdleState = "Idle";
        public const string RunState = "Run";
        public const string AttackState = "Attack";
        public const string CastState = "Cast";
        public const string HitState = "Hit";
        public const string DeathState = "Death";

        /// <summary>Every clip the controller can use, in the order it lays out the states.</summary>
        public static readonly string[] States = { IdleState, RunState, AttackState, CastState, HitState, DeathState };

        /// <summary>
        /// How long one-shot clips should take on the board, in seconds at normal playback speed,
        /// so a long Meshy clip doesn't lag behind the fight.
        /// </summary>
        public static float TargetSeconds(string state)
        {
            switch (state)
            {
                case AttackState: return 0.6f;
                case CastState: return 0.9f;
                case HitState: return 0.35f;
                case DeathState: return 1.2f;
                default: return 0f;
            }
        }

        public static readonly int MovingHash = Animator.StringToHash(Moving);
        public static readonly int AttackHash = Animator.StringToHash(Attack);
        public static readonly int CastHash = Animator.StringToHash(Cast);
        public static readonly int HitHash = Animator.StringToHash(Hit);
        public static readonly int DieHash = Animator.StringToHash(Die);
    }
}
