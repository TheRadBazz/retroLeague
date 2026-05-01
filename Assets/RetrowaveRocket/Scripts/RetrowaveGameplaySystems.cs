using UnityEngine;

namespace RetrowaveRocket
{
    public enum RetrowaveUtilityRole
    {
        Striker = 0,
        Defender = 1,
        Runner = 2,
        Disruptor = 3,
    }

    public enum RetrowaveBallState
    {
        Normal = 0,
        Charged = 1,
        Heavy = 2,
        Volatile = 3,
        Neon = 4,
    }

    public enum RetrowaveArenaObjectiveType
    {
        None = 0,
        BoostOverchargeZone = 1,
        WallGate = 2,
        MidfieldControlRing = 3,
    }

    public enum RetrowaveStyleEvent
    {
        ControlledTouch = 0,
        AerialTouch = 1,
        WallRide = 2,
        Drift = 3,
        TeamCombo = 4,
        Pass = 5,
        ObjectiveCapture = 6,
        PowerPlay = 7,
        ObjectiveHold = 8,
        AerialManeuver = 9,
        FlipTrick = 10,
        CleanLanding = 11,
    }

    public readonly struct RetrowaveBallTouchResult
    {
        public readonly bool Accepted;
        public readonly bool IsTeamCombo;
        public readonly ulong PreviousClientId;
        public readonly float HitMultiplier;

        public RetrowaveBallTouchResult(bool accepted, bool isTeamCombo, ulong previousClientId, float hitMultiplier)
        {
            Accepted = accepted;
            IsTeamCombo = isTeamCombo;
            PreviousClientId = previousClientId;
            HitMultiplier = Mathf.Max(0.01f, hitMultiplier);
        }

        public static RetrowaveBallTouchResult Ignored => new(false, false, ulong.MaxValue, 1f);
    }

    public static class RetrowaveUtilityRoleCatalog
    {
        public static string GetLabel(RetrowaveUtilityRole role)
        {
            return role switch
            {
                RetrowaveUtilityRole.Striker => "Striker",
                RetrowaveUtilityRole.Defender => "Defender",
                RetrowaveUtilityRole.Runner => "Runner",
                RetrowaveUtilityRole.Disruptor => "Disruptor",
                _ => "Striker",
            };
        }

        public static Color GetColor(RetrowaveUtilityRole role)
        {
            return role switch
            {
                RetrowaveUtilityRole.Striker => new Color(1f, 0.42f, 0.28f, 0.96f),
                RetrowaveUtilityRole.Defender => new Color(0.28f, 0.72f, 1f, 0.96f),
                RetrowaveUtilityRole.Runner => new Color(0.16f, 1f, 0.46f, 0.96f),
                RetrowaveUtilityRole.Disruptor => new Color(0.92f, 0.38f, 1f, 0.96f),
                _ => Color.white,
            };
        }

        public static float GetBallHitMultiplier(RetrowaveUtilityRole role)
        {
            return role switch
            {
                RetrowaveUtilityRole.Striker => 1.08f,
                RetrowaveUtilityRole.Defender => 1.04f,
                RetrowaveUtilityRole.Runner => 0.98f,
                RetrowaveUtilityRole.Disruptor => 1f,
                _ => 1f,
            };
        }

        public static float GetBoostDrainMultiplier(RetrowaveUtilityRole role)
        {
            return role == RetrowaveUtilityRole.Runner ? 0.88f : 1f;
        }

        public static float GetMaxSpeedMultiplier(RetrowaveUtilityRole role)
        {
            return role == RetrowaveUtilityRole.Runner ? 1.035f : 1f;
        }

        public static float GetGroundGripMultiplier(RetrowaveUtilityRole role)
        {
            return role == RetrowaveUtilityRole.Defender ? 1.08f : 1f;
        }

        public static float GetStatusDurationMultiplier(RetrowaveUtilityRole role)
        {
            return role == RetrowaveUtilityRole.Disruptor ? 1.16f : 1f;
        }

        public static float GetStyleGainMultiplier(RetrowaveUtilityRole role)
        {
            return role == RetrowaveUtilityRole.Striker ? 1.08f : 1f;
        }
    }

    public static class RetrowaveBallStateCatalog
    {
        public static string GetLabel(RetrowaveBallState state)
        {
            return state switch
            {
                RetrowaveBallState.Charged => "Charged",
                RetrowaveBallState.Heavy => "Heavy",
                RetrowaveBallState.Volatile => "Volatile",
                RetrowaveBallState.Neon => "Neon",
                _ => "Normal",
            };
        }

        public static Color GetColor(RetrowaveBallState state)
        {
            return state switch
            {
                RetrowaveBallState.Charged => new Color(1f, 0.92f, 0.2f, 1f),
                RetrowaveBallState.Heavy => new Color(0.55f, 0.62f, 0.72f, 1f),
                RetrowaveBallState.Volatile => new Color(1f, 0.22f, 0.12f, 1f),
                RetrowaveBallState.Neon => new Color(0.08f, 1f, 0.72f, 1f),
                _ => new Color(0.82f, 0.9f, 1f, 1f),
            };
        }
    }

    public static class RetrowaveArenaObjectiveCatalog
    {
        public static string GetLabel(RetrowaveArenaObjectiveType type)
        {
            return type switch
            {
                RetrowaveArenaObjectiveType.BoostOverchargeZone => "Overcharge Zone",
                RetrowaveArenaObjectiveType.WallGate => "Wall Gate",
                RetrowaveArenaObjectiveType.MidfieldControlRing => "Midfield Ring",
                _ => "Objective",
            };
        }

        public static Color GetColor(RetrowaveArenaObjectiveType type)
        {
            return type switch
            {
                RetrowaveArenaObjectiveType.BoostOverchargeZone => new Color(1f, 0.58f, 0.1f, 0.82f),
                RetrowaveArenaObjectiveType.WallGate => new Color(0.24f, 1f, 0.82f, 0.82f),
                RetrowaveArenaObjectiveType.MidfieldControlRing => new Color(0.42f, 0.7f, 1f, 0.82f),
                _ => new Color(0.65f, 0.9f, 1f, 0.7f),
            };
        }
    }

    public static class RetrowaveStyleEventCatalog
    {
        public static string GetLabel(RetrowaveStyleEvent styleEvent)
        {
            return styleEvent switch
            {
                RetrowaveStyleEvent.ControlledTouch => "Controlled touch",
                RetrowaveStyleEvent.AerialTouch => "Aerial touch",
                RetrowaveStyleEvent.WallRide => "Wall ride",
                RetrowaveStyleEvent.Drift => "Drift",
                RetrowaveStyleEvent.TeamCombo => "Team combo",
                RetrowaveStyleEvent.Pass => "Pass setup",
                RetrowaveStyleEvent.ObjectiveCapture => "Objective captured",
                RetrowaveStyleEvent.PowerPlay => "Power play",
                RetrowaveStyleEvent.ObjectiveHold => "Objective hold",
                RetrowaveStyleEvent.AerialManeuver => "Aerial maneuver",
                RetrowaveStyleEvent.FlipTrick => "Flip trick",
                RetrowaveStyleEvent.CleanLanding => "Clean landing",
                _ => "Style",
            };
        }
    }
}
