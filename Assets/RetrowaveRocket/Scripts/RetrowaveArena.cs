using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    public enum RetrowaveTeam
    {
        Blue = 0,
        Pink = 1,
    }

    public enum RetrowaveLobbyRole
    {
        Spectator = 0,
        Blue = 1,
        Pink = 2,
    }

    public enum RetrowavePowerUpType
    {
        BoostRefill = 0,
        SpeedBurst = 1,
    }

    public enum RetrowaveArenaSizePreset
    {
        Auto = 0,
        Compact = 1,
        Standard = 2,
        Expanded = 3,
        Stadium = 4,
        Mega = 5,
    }

    public readonly struct RetrowaveMatchSettings
    {
        public RetrowaveMatchSettings(int roundDurationSeconds, int roundCount, int maxPlayers, RetrowaveArenaSizePreset arenaSizePreset)
        {
            RoundDurationSeconds = Mathf.Clamp(roundDurationSeconds, 60, 900);
            RoundCount = Mathf.Clamp(roundCount, 1, 12);
            MaxPlayers = Mathf.Clamp(maxPlayers, 2, 40);
            ArenaSizePreset = arenaSizePreset;
        }

        public int RoundDurationSeconds { get; }
        public int RoundCount { get; }
        public int MaxPlayers { get; }
        public RetrowaveArenaSizePreset ArenaSizePreset { get; }

        public static RetrowaveMatchSettings Default => new RetrowaveMatchSettings(300, 3, 4, RetrowaveArenaSizePreset.Auto);
    }

    public readonly struct RetrowaveArenaLayout
    {
        private const float FixedGoalHalfWidth = 12f;
        private const float FixedGoalDepth = 12f;
        private const float FixedGoalHeight = 7.5f;
        private const float FixedRampWidth = 9f;
        private const float FixedRampDepth = 10f;
        private const float FixedRampHeight = 7.5f;
        private const float FixedCeilingHeight = 36f;

        public RetrowaveArenaLayout(
            float flatHalfWidth,
            float flatHalfLength,
            float goalHalfWidth,
            float goalDepth,
            float goalHeight,
            float outerHalfWidth,
            float outerHalfLength,
            float rampHeight,
            float ceilingHeight,
            int spawnColumns,
            float spawnLaneHalfWidth,
            float spawnStartDepth,
            float spawnRowSpacing,
            float spectatorHeight,
            float spectatorDepth,
            Vector3[] powerUpPositions,
            int signature)
        {
            FlatHalfWidth = flatHalfWidth;
            FlatHalfLength = flatHalfLength;
            GoalHalfWidth = goalHalfWidth;
            GoalDepth = goalDepth;
            GoalHeight = goalHeight;
            OuterHalfWidth = outerHalfWidth;
            OuterHalfLength = outerHalfLength;
            RampWidth = outerHalfWidth - flatHalfWidth;
            RampDepth = outerHalfLength - flatHalfLength;
            RampHeight = rampHeight;
            CeilingHeight = ceilingHeight;
            SpawnColumns = Mathf.Max(2, spawnColumns);
            SpawnLaneHalfWidth = spawnLaneHalfWidth;
            SpawnStartDepth = spawnStartDepth;
            SpawnRowSpacing = spawnRowSpacing;
            SpectatorHeight = spectatorHeight;
            SpectatorDepth = spectatorDepth;
            PowerUpPositions = powerUpPositions ?? new Vector3[0];
            Signature = signature;
        }

        public float FlatHalfWidth { get; }
        public float FlatHalfLength { get; }
        public float GoalHalfWidth { get; }
        public float GoalDepth { get; }
        public float GoalHeight { get; }
        public float OuterHalfWidth { get; }
        public float OuterHalfLength { get; }
        public float RampWidth { get; }
        public float RampDepth { get; }
        public float RampHeight { get; }
        public float CeilingHeight { get; }
        public int SpawnColumns { get; }
        public float SpawnLaneHalfWidth { get; }
        public float SpawnStartDepth { get; }
        public float SpawnRowSpacing { get; }
        public float SpectatorHeight { get; }
        public float SpectatorDepth { get; }
        public Vector3[] PowerUpPositions { get; }
        public int Signature { get; }

        public Vector3 BallSpawnPoint => new Vector3(0f, RetrowaveArenaConfig.GetBallSpawnHeight(0f, 0f), 0f);

        public static RetrowaveArenaLayout Resolve(RetrowaveMatchSettings settings)
        {
            var minimumPreset = RecommendPreset(settings.MaxPlayers);
            var finalPreset = settings.ArenaSizePreset == RetrowaveArenaSizePreset.Auto
                ? minimumPreset
                : (RetrowaveArenaSizePreset)Mathf.Max((int)minimumPreset, (int)settings.ArenaSizePreset);
            var teamCapacity = Mathf.Max(1, Mathf.CeilToInt(settings.MaxPlayers * 0.5f));
            var spawnColumns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(teamCapacity)), 2, 6);
            var powerUpCount = settings.MaxPlayers switch
            {
                <= 6 => 6,
                <= 12 => 8,
                <= 20 => 10,
                <= 30 => 12,
                _ => 14,
            };

            return finalPreset switch
            {
                RetrowaveArenaSizePreset.Compact => BuildLayout(
                    settings,
                    40f,
                    60f,
                    spawnColumns,
                    powerUpCount,
                    (int)RetrowaveArenaSizePreset.Compact),
                RetrowaveArenaSizePreset.Standard => BuildLayout(
                    settings,
                    54f,
                    80f,
                    spawnColumns,
                    powerUpCount,
                    (int)RetrowaveArenaSizePreset.Standard),
                RetrowaveArenaSizePreset.Expanded => BuildLayout(
                    settings,
                    72f,
                    106f,
                    spawnColumns,
                    powerUpCount,
                    (int)RetrowaveArenaSizePreset.Expanded),
                RetrowaveArenaSizePreset.Stadium => BuildLayout(
                    settings,
                    92f,
                    136f,
                    spawnColumns,
                    powerUpCount,
                    (int)RetrowaveArenaSizePreset.Stadium),
                _ => BuildLayout(
                    settings,
                    120f,
                    176f,
                    spawnColumns,
                    powerUpCount,
                    (int)RetrowaveArenaSizePreset.Mega),
            };
        }

        public static RetrowaveArenaSizePreset RecommendPreset(int maxPlayers)
        {
            return maxPlayers switch
            {
                <= 4 => RetrowaveArenaSizePreset.Compact,
                <= 10 => RetrowaveArenaSizePreset.Standard,
                <= 18 => RetrowaveArenaSizePreset.Expanded,
                <= 28 => RetrowaveArenaSizePreset.Stadium,
                _ => RetrowaveArenaSizePreset.Mega,
            };
        }

        private static RetrowaveArenaLayout BuildLayout(
            RetrowaveMatchSettings settings,
            float flatHalfWidth,
            float flatHalfLength,
            int spawnColumns,
            int powerUpCount,
            int signatureSeed)
        {
            var outerHalfWidth = flatHalfWidth + FixedRampWidth;
            var outerHalfLength = flatHalfLength + FixedRampDepth;
            var spawnLaneHalfWidth = flatHalfWidth * 0.68f;
            var spawnStartDepth = flatHalfLength * 0.58f;
            var spawnRowSpacing = Mathf.Clamp(outerHalfLength * 0.12f, 8f, 18f);
            var spectatorHeight = FixedCeilingHeight + 8f;
            var spectatorDepth = -flatHalfLength * 0.12f;
            var powerUps = BuildPowerUpPositions(flatHalfWidth, flatHalfLength, powerUpCount);
            var rampSignature = Mathf.RoundToInt((FixedRampWidth * 100f) + (FixedRampDepth * 10f) + FixedRampHeight);
            var signature = (signatureSeed * 10000) + (settings.MaxPlayers * 100) + (powerUpCount * 10) + rampSignature;

            return new RetrowaveArenaLayout(
                flatHalfWidth,
                flatHalfLength,
                FixedGoalHalfWidth,
                FixedGoalDepth,
                FixedGoalHeight,
                outerHalfWidth,
                outerHalfLength,
                FixedRampHeight,
                FixedCeilingHeight,
                spawnColumns,
                spawnLaneHalfWidth,
                spawnStartDepth,
                spawnRowSpacing,
                spectatorHeight,
                spectatorDepth,
                powerUps,
                signature);
        }

        private static Vector3[] BuildPowerUpPositions(float flatHalfWidth, float flatHalfLength, int powerUpCount)
        {
            var positions = new System.Collections.Generic.List<Vector3>();
            var sideX = flatHalfWidth * 0.62f;
            var midZ = flatHalfLength * 0.32f;
            var deepZ = flatHalfLength * 0.64f;
            positions.AddRange(new[]
            {
                new Vector3(-sideX, 1.2f, -midZ),
                new Vector3(sideX, 1.2f, -midZ),
                new Vector3(-sideX, 1.2f, midZ),
                new Vector3(sideX, 1.2f, midZ),
                new Vector3(0f, 1.2f, -deepZ),
                new Vector3(0f, 1.2f, deepZ),
            });

            if (powerUpCount <= 6)
            {
                return positions.ToArray();
            }

            var wideX = flatHalfWidth * 0.34f;
            var wideZ = flatHalfLength * 0.8f;
            positions.AddRange(new[]
            {
                new Vector3(-wideX, 1.2f, wideZ),
                new Vector3(wideX, 1.2f, -wideZ),
            });

            if (powerUpCount <= 8)
            {
                return positions.ToArray();
            }

            var centerWideX = flatHalfWidth * 0.76f;
            var centerWideZ = flatHalfLength * 0.18f;
            positions.AddRange(new[]
            {
                new Vector3(-centerWideX, 1.2f, -centerWideZ),
                new Vector3(centerWideX, 1.2f, centerWideZ),
            });

            if (powerUpCount <= 10)
            {
                return positions.ToArray();
            }

            var ringX = flatHalfWidth * 0.22f;
            var ringZ = flatHalfLength * 0.92f;
            positions.AddRange(new[]
            {
                new Vector3(-ringX, 1.2f, ringZ),
                new Vector3(ringX, 1.2f, -ringZ),
            });

            if (powerUpCount <= 12)
            {
                return positions.ToArray();
            }

            var outerX = flatHalfWidth * 0.84f;
            var outerZ = flatHalfLength * 0.48f;
            positions.AddRange(new[]
            {
                new Vector3(-outerX, 1.2f, outerZ),
                new Vector3(outerX, 1.2f, -outerZ),
            });

            return positions.ToArray();
        }
    }

    public static class RetrowaveArenaConfig
    {
        public const float MaxBoost = 100f;
        public const float StartingBoost = 55f;
        public const float PowerUpRespawnSeconds = 8f;
        public const float SpeedBurstMultiplier = 1.4f;
        public const float SpeedBurstDuration = 4.5f;
        public const float PassiveBoostRegen = 2.5f;
        private const float VehicleSpawnClearance = 1.05f;
        private const float BallSpawnClearance = 1.35f;

        private static RetrowaveMatchSettings _currentSettings = RetrowaveMatchSettings.Default;
        private static RetrowaveArenaLayout _currentLayout = RetrowaveArenaLayout.Resolve(RetrowaveMatchSettings.Default);

        public static float FlatHalfWidth => _currentLayout.FlatHalfWidth;
        public static float FlatHalfLength => _currentLayout.FlatHalfLength;
        public static float GoalHalfWidth => _currentLayout.GoalHalfWidth;
        public static float GoalDepth => _currentLayout.GoalDepth;
        public static float GoalHeight => _currentLayout.GoalHeight;
        public static float OuterHalfWidth => _currentLayout.OuterHalfWidth;
        public static float OuterHalfLength => _currentLayout.OuterHalfLength;
        public static float RampWidth => _currentLayout.RampWidth;
        public static float RampDepth => _currentLayout.RampDepth;
        public static float RampHeight => _currentLayout.RampHeight;
        public static float CeilingHeight => _currentLayout.CeilingHeight;
        public static Vector3 BallSpawnPoint => _currentLayout.BallSpawnPoint;
        public static Vector3[] PowerUpPositions => _currentLayout.PowerUpPositions;
        public static RetrowaveMatchSettings CurrentSettings => _currentSettings;
        public static RetrowaveArenaLayout CurrentLayout => _currentLayout;

        public static void ApplyMatchSettings(RetrowaveMatchSettings settings)
        {
            _currentSettings = settings;
            _currentLayout = RetrowaveArenaLayout.Resolve(settings);
        }

        public static Vector3 GetSpawnPoint(RetrowaveTeam team, int slot)
        {
            return GetSpawnPoint(team, slot, Mathf.Max(slot + 1, 1));
        }

        public static Vector3 GetSpawnPoint(RetrowaveTeam team, int slot, int teamPlayerCount)
        {
            var count = Mathf.Max(1, teamPlayerCount);
            var columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(count)), 1, 8);
            var row = slot / columns;
            var rowStart = row * columns;
            var rowCount = Mathf.CeilToInt(count / (float)columns);
            var playersInRow = Mathf.Clamp(count - rowStart, 1, columns);
            var column = Mathf.Clamp(slot - rowStart, 0, playersInRow - 1);
            var columnProgress = playersInRow <= 1 ? 0.5f : column / (float)(playersInRow - 1);
            var laneHalfWidth = Mathf.Min(_currentLayout.SpawnLaneHalfWidth, _currentLayout.FlatHalfWidth - 8f);
            var lateral = Mathf.Lerp(-laneHalfWidth, laneHalfWidth, columnProgress);
            var nearDepth = Mathf.Max(16f, _currentLayout.FlatHalfLength * 0.24f);
            var farDepth = Mathf.Max(nearDepth + 6f, _currentLayout.FlatHalfLength - Mathf.Max(14f, _currentLayout.RampDepth + 6f));
            var rowProgress = rowCount <= 1 ? 0.58f : row / (float)(rowCount - 1);
            var depthMagnitude = Mathf.Lerp(nearDepth, farDepth, rowProgress);
            var depth = team == RetrowaveTeam.Blue ? -depthMagnitude : depthMagnitude;
            return ClampToPlayableSpawn(new Vector3(lateral, GetVehicleSpawnHeight(lateral, depth), depth), team);
        }

        public static Quaternion GetSpawnRotation(RetrowaveTeam team)
        {
            return team == RetrowaveTeam.Blue ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
        }

        public static Vector3 GetSpectatorStagingPoint(ulong clientId)
        {
            var lane = (int)(clientId % 4ul);
            var row = (int)(clientId / 4ul);
            var laneWidth = Mathf.Max(10f, _currentLayout.FlatHalfWidth * 0.3f);
            return new Vector3(
                -laneWidth * 1.5f + lane * laneWidth,
                _currentLayout.SpectatorHeight + row * 4f,
                _currentLayout.SpectatorDepth);
        }

        public static Vector3 ClampToPlayableSpawn(Vector3 position, RetrowaveTeam team)
        {
            var safeX = Mathf.Max(0f, _currentLayout.FlatHalfWidth - 8f);
            var nearDepth = Mathf.Max(12f, _currentLayout.FlatHalfLength * 0.18f);
            var farDepth = Mathf.Max(nearDepth + 4f, _currentLayout.FlatHalfLength - Mathf.Max(12f, _currentLayout.RampDepth + 5f));
            var depthMagnitude = Mathf.Clamp(Mathf.Abs(position.z), nearDepth, farDepth);
            var signedDepth = team == RetrowaveTeam.Blue ? -depthMagnitude : depthMagnitude;
            var clampedX = Mathf.Clamp(position.x, -safeX, safeX);
            return new Vector3(
                clampedX,
                GetVehicleSpawnHeight(clampedX, signedDepth),
                signedDepth);
        }

        public static float GetSurfaceHeight(float x, float z)
        {
            return RetrowaveArenaBuilder.EvaluateHeight(x, z);
        }

        public static float GetSurfaceSpawnHeight(float x, float z)
        {
            return GetVehicleSpawnHeight(x, z);
        }

        public static float GetVehicleSpawnHeight(float x, float z)
        {
            return GetSurfaceHeight(x, z) + VehicleSpawnClearance;
        }

        public static float GetBallSpawnHeight(float x, float z)
        {
            return GetSurfaceHeight(x, z) + BallSpawnClearance;
        }

        public static bool IsWithinArenaRecoveryBounds(Vector3 position)
        {
            return position.y >= -12f
                   && position.y <= _currentLayout.CeilingHeight + 2f
                   && Mathf.Abs(position.x) <= _currentLayout.OuterHalfWidth + 5f
                   && Mathf.Abs(position.z) <= _currentLayout.OuterHalfLength + 5f;
        }
    }

    public static class RetrowavePodiumLayout
    {
        public static Vector3 Center
        {
            get
            {
                var z = -Mathf.Min(28f, RetrowaveArenaConfig.FlatHalfLength * 0.2f);
                return new Vector3(0f, 0f, z);
            }
        }

        public static Quaternion VehicleRotation => Quaternion.LookRotation(Vector3.back, Vector3.up);
        public static Vector3 CameraLookPoint => Center + new Vector3(0f, 2.7f, 1.6f);
        public static Vector3 CameraPosition => Center + new Vector3(0f, 8.5f, -23f);

        public static Vector3 GetPlatformPosition(int rank)
        {
            var offset = rank switch
            {
                0 => new Vector3(0f, 1.1f, 0f),
                1 => new Vector3(-5.4f, 0.72f, 1.35f),
                2 => new Vector3(5.4f, 0.52f, 1.35f),
                _ => Vector3.zero,
            };

            return Center + offset;
        }

        public static Vector3 GetPlatformScale(int rank)
        {
            return rank switch
            {
                0 => new Vector3(4.8f, 2.2f, 4.2f),
                1 => new Vector3(4.4f, 1.44f, 3.9f),
                2 => new Vector3(4.4f, 1.04f, 3.9f),
                _ => Vector3.one,
            };
        }

        public static Vector3 GetVehiclePosition(int rank, int totalCount)
        {
            if (rank < 3)
            {
                var platformPosition = GetPlatformPosition(rank);
                var platformScale = GetPlatformScale(rank);
                return new Vector3(
                    platformPosition.x,
                    platformPosition.y + platformScale.y * 0.5f + 1.35f,
                    platformPosition.z);
            }

            var extraIndex = rank - 3;
            var extraCount = Mathf.Max(1, totalCount - 3);
            var columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(extraCount)), 3, 8);
            var row = extraIndex / columns;
            var column = extraIndex % columns;
            var rowCount = Mathf.Min(columns, extraCount - row * columns);
            var centeredColumn = column - (rowCount - 1) * 0.5f;
            var offset = new Vector3(centeredColumn * 4.5f, 1.35f, 6.2f + row * 4.5f);
            return Center + offset;
        }
    }

    public static class RetrowaveStyle
    {
        private const string RuntimeLitTemplateResourcePath = "RetrowaveRocket/RuntimeLitTemplate";

        private static Shader _litShader;
        private static Shader _unlitShader;
        private static Material _litTemplate;

        public static Color BlueBase => new Color(0.07f, 0.44f, 0.93f);
        public static Color BlueGlow => new Color(0.1f, 0.95f, 1f);
        public static Color PinkBase => new Color(1f, 0.22f, 0.72f);
        public static Color PinkGlow => new Color(1f, 0.24f, 0.75f);
        public static Color ArenaBase => new Color(0.05f, 0.02f, 0.11f);
        public static Color ArenaGlow => new Color(0.06f, 0.78f, 1f);

        public static Material CreateLitMaterial(Color baseColor, Color emissionColor, float smoothness = 0.7f, float metallic = 0.05f)
        {
            var material = CreateMaterialInstance(ref _litTemplate, ref _litShader, ResolveLitShader());

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", baseColor);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", baseColor);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emissionColor);
            }

            return material;
        }

        public static Material CreateUnlitMaterial(Color color)
        {
            var material = CreateMaterialInstance(ref _litTemplate, ref _unlitShader, ResolveUnlitShader());

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }

        public static Color GetTeamBase(RetrowaveTeam team)
        {
            return team == RetrowaveTeam.Blue ? BlueBase : PinkBase;
        }

        public static Color GetTeamGlow(RetrowaveTeam team)
        {
            return team == RetrowaveTeam.Blue ? BlueGlow : PinkGlow;
        }

        private static Material CreateMaterialInstance(ref Material template, ref Shader shaderCache, Shader resolvedShader)
        {
            shaderCache = resolvedShader;

            if (template == null)
            {
                template = Resources.Load<Material>(RuntimeLitTemplateResourcePath);

                if (template == null)
                {
                    template = new Material(resolvedShader);
                }
            }

            return new Material(template);
        }

        private static Shader ResolveLitShader()
        {
            if (_litShader != null)
            {
                return _litShader;
            }

            _litShader = Shader.Find("Universal Render Pipeline/Lit");

            if (_litShader != null)
            {
                return _litShader;
            }

            var template = Resources.Load<Material>(RuntimeLitTemplateResourcePath);

            if (template != null && template.shader != null)
            {
                Debug.LogWarning("RetrowaveStyle: falling back to the runtime lit template shader for build compatibility.");
                _litShader = template.shader;
                return _litShader;
            }

            _litShader = Shader.Find("Standard");

            if (_litShader == null)
            {
                Debug.LogError("RetrowaveStyle: unable to resolve a lit shader.");
            }

            return _litShader;
        }

        private static Shader ResolveUnlitShader()
        {
            if (_unlitShader != null)
            {
                return _unlitShader;
            }

            _unlitShader = Shader.Find("Universal Render Pipeline/Unlit");

            if (_unlitShader != null)
            {
                return _unlitShader;
            }

            Debug.LogWarning("RetrowaveStyle: URP Unlit shader not found, falling back to lit shader so runtime materials still render in builds.");
            _unlitShader = ResolveLitShader();
            return _unlitShader;
        }
    }

    public static class RetrowaveArenaBuilder
    {
        private const float GoalRampClearance = 1.25f;
        private static GameObject _arenaRoot;
        private static int _builtLayoutSignature = int.MinValue;
        private static PhysicsMaterial _perimeterBounceMaterial;

        public static void EnsureBuilt()
        {
            var requiredSignature = RetrowaveArenaConfig.CurrentLayout.Signature;

            if (_arenaRoot != null && _builtLayoutSignature == requiredSignature)
            {
                return;
            }

            if (_arenaRoot != null)
            {
                Object.Destroy(_arenaRoot);
            }

            _arenaRoot = new GameObject("Retrowave Arena");
            Object.DontDestroyOnLoad(_arenaRoot);
            _builtLayoutSignature = requiredSignature;

            BuildArenaSurface(_arenaRoot.transform);
            BuildGoals(_arenaRoot.transform);
            BuildFieldStrips(_arenaRoot.transform);
            BuildArenaCage(_arenaRoot.transform);
            BuildBackdrop(_arenaRoot.transform);
            ConfigureLighting();
        }

        public static void SetActive(bool isActive)
        {
            if (_arenaRoot == null)
            {
                return;
            }

            _arenaRoot.SetActive(isActive);
        }

        public static float EvaluateHeight(float x, float z)
        {
            var outsideX = Mathf.Max(0f, Mathf.Abs(x) - RetrowaveArenaConfig.FlatHalfWidth);
            var outsideZ = Mathf.Max(0f, Mathf.Abs(z) - RetrowaveArenaConfig.FlatHalfLength);
            var inGoalChannel = Mathf.Abs(z) > RetrowaveArenaConfig.FlatHalfLength && Mathf.Abs(x) <= GoalClearanceHalfWidth;

            if (inGoalChannel)
            {
                outsideZ = 0f;
            }

            var curveX = outsideX / Mathf.Max(0.001f, RetrowaveArenaConfig.RampWidth);
            var curveZ = outsideZ / Mathf.Max(0.001f, RetrowaveArenaConfig.RampDepth);
            var curveAmount = Mathf.Clamp01(new Vector2(curveX, curveZ).magnitude);
            var eased = curveAmount * curveAmount * (3f - 2f * curveAmount);

            return eased * RetrowaveArenaConfig.RampHeight;
        }

        private static void BuildArenaSurface(Transform parent)
        {
            var surface = new GameObject("Arena Surface");
            surface.transform.SetParent(parent, false);

            var meshFilter = surface.AddComponent<MeshFilter>();
            var meshRenderer = surface.AddComponent<MeshRenderer>();
            var meshCollider = surface.AddComponent<MeshCollider>();

            var mesh = GenerateArenaMesh(72, 104);
            meshFilter.sharedMesh = mesh;
            meshCollider.sharedMesh = mesh;
            meshRenderer.sharedMaterials = new[]
            {
                RetrowaveStyle.CreateLitMaterial(
                    new Color(0.035f, 0.12f, 0.17f),
                    new Color(0.02f, 0.2f, 0.26f),
                    0.92f,
                    0.02f),
                RetrowaveStyle.CreateLitMaterial(
                    new Color(0.018f, 0.035f, 0.08f),
                    new Color(0.04f, 0.34f, 0.42f),
                    0.82f,
                    0.02f),
            };

            var underlay = GameObject.CreatePrimitive(PrimitiveType.Cube);
            underlay.name = "Arena Underlay";
            underlay.transform.SetParent(parent, false);
            underlay.transform.position = new Vector3(0f, -2.5f, 0f);
            underlay.transform.localScale = new Vector3(
                RetrowaveArenaConfig.OuterHalfWidth * 2.1f,
                4f,
                RetrowaveArenaConfig.OuterHalfLength * 2.1f);
            underlay.GetComponent<MeshRenderer>().sharedMaterial = RetrowaveStyle.CreateLitMaterial(
                new Color(0.02f, 0.01f, 0.05f),
                new Color(0.12f, 0.02f, 0.08f),
                0.55f,
                0f);
            DisableCollider(underlay);
        }

        private static Mesh GenerateArenaMesh(int widthSegments, int lengthSegments)
        {
            var verticesPerRow = widthSegments + 1;
            var vertexCount = verticesPerRow * (lengthSegments + 1);
            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var flatTriangles = new System.Collections.Generic.List<int>(widthSegments * lengthSegments * 3);
            var rampTriangles = new System.Collections.Generic.List<int>(widthSegments * lengthSegments * 3);

            var index = 0;

            for (var z = 0; z <= lengthSegments; z++)
            {
                var zPercent = z / (float)lengthSegments;
                var worldZ = Mathf.Lerp(-RetrowaveArenaConfig.OuterHalfLength, RetrowaveArenaConfig.OuterHalfLength, zPercent);

                for (var x = 0; x <= widthSegments; x++)
                {
                    var xPercent = x / (float)widthSegments;
                    var worldX = Mathf.Lerp(-RetrowaveArenaConfig.OuterHalfWidth, RetrowaveArenaConfig.OuterHalfWidth, xPercent);
                    var worldY = EvaluateHeight(worldX, worldZ);

                    vertices[index] = new Vector3(worldX, worldY, worldZ);
                    uvs[index] = new Vector2(worldX * 0.08f, worldZ * 0.08f);
                    index++;
                }
            }

            for (var z = 0; z < lengthSegments; z++)
            {
                for (var x = 0; x < widthSegments; x++)
                {
                    var root = z * verticesPerRow + x;
                    var centerXPercent = (x + 0.5f) / widthSegments;
                    var centerZPercent = (z + 0.5f) / lengthSegments;
                    var centerX = Mathf.Lerp(-RetrowaveArenaConfig.OuterHalfWidth, RetrowaveArenaConfig.OuterHalfWidth, centerXPercent);
                    var centerZ = Mathf.Lerp(-RetrowaveArenaConfig.OuterHalfLength, RetrowaveArenaConfig.OuterHalfLength, centerZPercent);
                    var targetTriangles = IsFlatFieldSurface(centerX, centerZ) ? flatTriangles : rampTriangles;

                    targetTriangles.Add(root);
                    targetTriangles.Add(root + verticesPerRow);
                    targetTriangles.Add(root + 1);
                    targetTriangles.Add(root + 1);
                    targetTriangles.Add(root + verticesPerRow);
                    targetTriangles.Add(root + verticesPerRow + 1);
                }
            }

            var mesh = new Mesh
            {
                name = "Retrowave Arena Surface",
                vertices = vertices,
                uv = uvs,
            };

            mesh.subMeshCount = 2;
            mesh.SetTriangles(flatTriangles, 0);
            mesh.SetTriangles(rampTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool IsFlatFieldSurface(float x, float z)
        {
            var inFlatField = Mathf.Abs(x) <= RetrowaveArenaConfig.FlatHalfWidth
                              && Mathf.Abs(z) <= RetrowaveArenaConfig.FlatHalfLength;
            var inGoalChannel = Mathf.Abs(z) > RetrowaveArenaConfig.FlatHalfLength
                                && Mathf.Abs(x) <= GoalClearanceHalfWidth;
            return inFlatField || inGoalChannel;
        }

        private static float GoalClearanceHalfWidth => RetrowaveArenaConfig.GoalHalfWidth + GoalRampClearance;

        private static void BuildFieldStrips(Transform parent)
        {
            for (var z = -30f; z <= 30f; z += 6f)
            {
                CreateFloorDecal(
                    parent,
                    $"Lane Strip {z:0}",
                    new Vector3(0f, 0.016f, z),
                    new Vector2(RetrowaveArenaConfig.FlatHalfWidth * 1.9f, 0.18f),
                    RetrowaveStyle.CreateUnlitMaterial(new Color(0.05f, 0.7f, 1f, 0.95f)));
            }

            for (var x = -18f; x <= 18f; x += 6f)
            {
                CreateFloorDecal(
                    parent,
                    $"Cross Strip {x:0}",
                    new Vector3(x, 0.018f, 0f),
                    new Vector2(0.18f, RetrowaveArenaConfig.FlatHalfLength * 1.7f),
                    RetrowaveStyle.CreateUnlitMaterial(new Color(1f, 0.18f, 0.74f, 0.9f)));
            }

            CreateFloorDisc(
                parent,
                "Center Disk",
                new Vector3(0f, 0.02f, 0f),
                4.5f,
                72,
                RetrowaveStyle.CreateUnlitMaterial(new Color(1f, 0.25f, 0.65f, 0.9f)));

            BuildRampTransitionAccents(parent);
        }

        private static void BuildRampTransitionAccents(Transform parent)
        {
            var sideMaterial = RetrowaveStyle.CreateUnlitMaterial(new Color(0.12f, 0.95f, 1f, 0.96f));
            var endMaterial = RetrowaveStyle.CreateUnlitMaterial(new Color(1f, 0.2f, 0.74f, 0.94f));

            DisableCollider(CreateStrip(
                parent,
                "Left Ramp Seam",
                new Vector3(-RetrowaveArenaConfig.FlatHalfWidth, 0.08f, 0f),
                new Vector3(0.22f, 0.08f, RetrowaveArenaConfig.FlatHalfLength * 2f),
                sideMaterial));
            DisableCollider(CreateStrip(
                parent,
                "Right Ramp Seam",
                new Vector3(RetrowaveArenaConfig.FlatHalfWidth, 0.08f, 0f),
                new Vector3(0.22f, 0.08f, RetrowaveArenaConfig.FlatHalfLength * 2f),
                sideMaterial));

            var goalClearanceHalfWidth = GoalClearanceHalfWidth;
            var endSegmentWidth = Mathf.Max(0f, RetrowaveArenaConfig.FlatHalfWidth - goalClearanceHalfWidth);

            if (endSegmentWidth <= 0.01f)
            {
                return;
            }

            var leftSegmentCenter = -(goalClearanceHalfWidth + endSegmentWidth * 0.5f);
            var rightSegmentCenter = goalClearanceHalfWidth + endSegmentWidth * 0.5f;

            for (var direction = -1; direction <= 1; direction += 2)
            {
                var seamZ = direction * RetrowaveArenaConfig.FlatHalfLength;
                DisableCollider(CreateStrip(
                    parent,
                    $"End Ramp Seam Left {direction}",
                    new Vector3(leftSegmentCenter, 0.08f, seamZ),
                    new Vector3(endSegmentWidth, 0.08f, 0.22f),
                    endMaterial));
                DisableCollider(CreateStrip(
                    parent,
                    $"End Ramp Seam Right {direction}",
                    new Vector3(rightSegmentCenter, 0.08f, seamZ),
                    new Vector3(endSegmentWidth, 0.08f, 0.22f),
                    endMaterial));
            }
        }

        private static void BuildGoals(Transform parent)
        {
            BuildGoal(parent, RetrowaveTeam.Blue, -1f);
            BuildGoal(parent, RetrowaveTeam.Pink, 1f);
        }

        private static void BuildArenaCage(Transform parent)
        {
            var root = new GameObject("Arena Cage");
            root.transform.SetParent(parent, false);

            var halfWidth = RetrowaveArenaConfig.OuterHalfWidth + 0.9f;
            var halfLength = RetrowaveArenaConfig.OuterHalfLength + 0.9f;
            var wallHeight = RetrowaveArenaConfig.CeilingHeight;
            var wallMaterial = RetrowaveStyle.CreateLitMaterial(
                new Color(0.03f, 0.04f, 0.07f),
                new Color(0.08f, 0.75f, 1f) * 1.5f,
                0.78f,
                0.02f);
            var accentMaterial = RetrowaveStyle.CreateUnlitMaterial(new Color(0.95f, 0.22f, 0.7f, 0.9f));

            CreateBarrierVolume(root.transform, "Left Barrier", new Vector3(-halfWidth, wallHeight * 0.5f, 0f), new Vector3(1.2f, wallHeight, halfLength * 2.05f));
            CreateBarrierVolume(root.transform, "Right Barrier", new Vector3(halfWidth, wallHeight * 0.5f, 0f), new Vector3(1.2f, wallHeight, halfLength * 2.05f));
            CreateBarrierVolume(root.transform, "North Barrier", new Vector3(0f, wallHeight * 0.5f, halfLength), new Vector3(halfWidth * 2.05f, wallHeight, 1.2f));
            CreateBarrierVolume(root.transform, "South Barrier", new Vector3(0f, wallHeight * 0.5f, -halfLength), new Vector3(halfWidth * 2.05f, wallHeight, 1.2f));
            CreateBarrierVolume(root.transform, "Ceiling Barrier", new Vector3(0f, wallHeight + 0.6f, 0f), new Vector3(halfWidth * 2.05f, 1.2f, halfLength * 2.05f));

            for (var z = -halfLength; z <= halfLength; z += 8f)
            {
                DisableCollider(CreateStrip(root.transform, $"Left Cage Post {z:0}", new Vector3(-halfWidth, wallHeight * 0.5f, z), new Vector3(0.24f, wallHeight, 0.24f), wallMaterial));
                DisableCollider(CreateStrip(root.transform, $"Right Cage Post {z:0}", new Vector3(halfWidth, wallHeight * 0.5f, z), new Vector3(0.24f, wallHeight, 0.24f), wallMaterial));
            }

            for (var x = -halfWidth; x <= halfWidth; x += 8f)
            {
                DisableCollider(CreateStrip(root.transform, $"North Cage Post {x:0}", new Vector3(x, wallHeight * 0.5f, halfLength), new Vector3(0.24f, wallHeight, 0.24f), wallMaterial));
                DisableCollider(CreateStrip(root.transform, $"South Cage Post {x:0}", new Vector3(x, wallHeight * 0.5f, -halfLength), new Vector3(0.24f, wallHeight, 0.24f), wallMaterial));
            }

            for (var y = 4f; y <= wallHeight; y += 5f)
            {
                DisableCollider(CreateStrip(root.transform, $"Left Cage Rail {y:0}", new Vector3(-halfWidth, y, 0f), new Vector3(0.16f, 0.16f, halfLength * 2f), accentMaterial));
                DisableCollider(CreateStrip(root.transform, $"Right Cage Rail {y:0}", new Vector3(halfWidth, y, 0f), new Vector3(0.16f, 0.16f, halfLength * 2f), accentMaterial));
                DisableCollider(CreateStrip(root.transform, $"North Cage Rail {y:0}", new Vector3(0f, y, halfLength), new Vector3(halfWidth * 2f, 0.16f, 0.16f), accentMaterial));
                DisableCollider(CreateStrip(root.transform, $"South Cage Rail {y:0}", new Vector3(0f, y, -halfLength), new Vector3(halfWidth * 2f, 0.16f, 0.16f), accentMaterial));
            }

            for (var z = -halfLength + 6f; z <= halfLength - 6f; z += 10f)
            {
                DisableCollider(CreateStrip(root.transform, $"Roof Span {z:0}", new Vector3(0f, wallHeight + 0.12f, z), new Vector3(halfWidth * 2f, 0.12f, 0.12f), wallMaterial));
            }

            for (var x = -halfWidth + 6f; x <= halfWidth - 6f; x += 10f)
            {
                DisableCollider(CreateStrip(root.transform, $"Roof Rib {x:0}", new Vector3(x, wallHeight + 0.12f, 0f), new Vector3(0.12f, 0.12f, halfLength * 2f), wallMaterial));
            }
        }

        private static void BuildGoal(Transform parent, RetrowaveTeam team, float direction)
        {
            var root = new GameObject($"{team} Goal");
            root.transform.SetParent(parent, false);

            var teamGlow = RetrowaveStyle.GetTeamGlow(team);
            var teamBase = RetrowaveStyle.GetTeamBase(team);
            var frameMaterial = RetrowaveStyle.CreateLitMaterial(
                Color.Lerp(teamBase, Color.white, 0.18f),
                teamGlow * 2.5f,
                0.92f,
                0.06f);
            var interiorMaterial = RetrowaveStyle.CreateLitMaterial(
                new Color(0.025f, 0.035f, 0.075f),
                teamGlow * 0.28f,
                0.78f,
                0.01f);
            var floorMaterial = RetrowaveStyle.CreateLitMaterial(
                new Color(0.035f, 0.055f, 0.09f),
                teamGlow * 0.38f,
                0.88f,
                0.02f);
            var shadowMaterial = RetrowaveStyle.CreateLitMaterial(
                new Color(0.012f, 0.012f, 0.032f),
                teamGlow * 0.12f,
                0.65f,
                0f);
            var trimMaterial = RetrowaveStyle.CreateUnlitMaterial(Color.Lerp(teamGlow, Color.white, 0.16f));

            var cageWallCenterZ = RetrowaveArenaConfig.OuterHalfLength + 0.9f;
            var cageWallInnerFaceZ = cageWallCenterZ - 0.6f;
            var goalFrontZ = RetrowaveArenaConfig.FlatHalfLength - 0.18f;
            var goalBackZ = cageWallInnerFaceZ + 0.18f;
            var goalDepth = Mathf.Max(RetrowaveArenaConfig.GoalDepth, goalBackZ - goalFrontZ);
            var goalCenterZ = direction * ((goalFrontZ + goalBackZ) * 0.5f);
            var goalBackWallZ = direction * goalBackZ;
            var goalFrontWorldZ = direction * goalFrontZ;
            var goalFaceOffset = -direction * 0.32f;
            var goalHalfWidth = RetrowaveArenaConfig.GoalHalfWidth;
            var goalHeight = RetrowaveArenaConfig.GoalHeight;
            var sideX = goalHalfWidth + 0.14f;

            CreateStrip(
                root.transform,
                "Interior Floor",
                new Vector3(0f, 0.035f, goalCenterZ),
                new Vector3(goalHalfWidth * 2.05f, 0.07f, goalDepth),
                floorMaterial);

            CreateStrip(
                root.transform,
                "Back Shadow Panel",
                new Vector3(0f, goalHeight * 0.45f, goalBackWallZ),
                new Vector3(goalHalfWidth * 2.05f, goalHeight * 0.9f, 0.5f),
                shadowMaterial);

            CreateStrip(
                root.transform,
                "Left Interior Wall",
                new Vector3(-sideX, goalHeight * 0.45f, goalCenterZ),
                new Vector3(0.36f, goalHeight * 0.9f, goalDepth),
                interiorMaterial);
            CreateStrip(
                root.transform,
                "Right Interior Wall",
                new Vector3(sideX, goalHeight * 0.45f, goalCenterZ),
                new Vector3(0.36f, goalHeight * 0.9f, goalDepth),
                interiorMaterial);
            CreateStrip(
                root.transform,
                "Interior Ceiling",
                new Vector3(0f, goalHeight + 0.12f, goalCenterZ),
                new Vector3(goalHalfWidth * 2.05f, 0.32f, goalDepth),
                interiorMaterial);

            CreateStrip(
                root.transform,
                "Front Left Post",
                new Vector3(-sideX, goalHeight * 0.5f, goalFrontWorldZ),
                new Vector3(0.62f, goalHeight + 0.46f, 0.62f),
                frameMaterial);
            CreateStrip(
                root.transform,
                "Front Right Post",
                new Vector3(sideX, goalHeight * 0.5f, goalFrontWorldZ),
                new Vector3(0.62f, goalHeight + 0.46f, 0.62f),
                frameMaterial);
            CreateStrip(
                root.transform,
                "Front Crossbar",
                new Vector3(0f, goalHeight + 0.24f, goalFrontWorldZ),
                new Vector3(goalHalfWidth * 2.35f, 0.52f, 0.62f),
                frameMaterial);
            CreateStrip(
                root.transform,
                "Front Base Rail",
                new Vector3(0f, 0.22f, goalFrontWorldZ),
                new Vector3(goalHalfWidth * 2.25f, 0.22f, 0.44f),
                frameMaterial);

            for (var i = -2; i <= 2; i++)
            {
                DisableCollider(CreateStrip(
                    root.transform,
                    $"Back Grid Vertical {i}",
                    new Vector3(i * (goalHalfWidth * 0.36f), goalHeight * 0.48f, goalBackWallZ + goalFaceOffset),
                    new Vector3(0.11f, goalHeight * 0.74f, 0.08f),
                    trimMaterial));
            }

            for (var i = 1; i <= 3; i++)
            {
                DisableCollider(CreateStrip(
                    root.transform,
                    $"Back Grid Horizontal {i}",
                    new Vector3(0f, i * (goalHeight * 0.22f), goalBackWallZ + goalFaceOffset),
                    new Vector3(goalHalfWidth * 1.72f, 0.1f, 0.08f),
                    trimMaterial));
            }

            for (var i = 1; i <= 3; i++)
            {
                var depthOffset = Mathf.Lerp(goalFrontZ + 1.8f, goalBackZ - 1.2f, i / 4f);
                DisableCollider(CreateStrip(
                    root.transform,
                    $"Ceiling Depth Rib {i}",
                    new Vector3(0f, goalHeight + 0.34f, direction * depthOffset),
                    new Vector3(goalHalfWidth * 1.82f, 0.08f, 0.12f),
                    trimMaterial));
            }

            var trigger = new GameObject("Goal Trigger");
            trigger.transform.SetParent(root.transform, false);
            trigger.transform.position = new Vector3(0f, goalHeight * 0.45f, goalCenterZ);
            var boxCollider = trigger.AddComponent<BoxCollider>();
            boxCollider.isTrigger = true;
            boxCollider.size = new Vector3(
                goalHalfWidth * 1.95f,
                goalHeight * 0.9f,
                goalDepth * 0.92f);

            var goalVolume = trigger.AddComponent<RetrowaveGoalVolume>();
            goalVolume.Team = team;

            var lightObject = new GameObject("Glow");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.position = new Vector3(0f, goalHeight * 0.56f, goalCenterZ - direction * 1.2f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 20f;
            light.intensity = 5.6f;
            light.color = teamGlow;
        }

        private static void BuildBackdrop(Transform parent)
        {
            var skyline = new GameObject("Backdrop Skyline");
            skyline.transform.SetParent(parent, false);
            var backdropZ = -RetrowaveArenaConfig.OuterHalfLength - 34f;
            var skylineWidth = Mathf.Max(110f, RetrowaveArenaConfig.OuterHalfWidth * 1.6f);
            var skylineStartX = -skylineWidth * 0.5f;
            var skylineSpacing = skylineWidth / 23f;

            for (var i = 0; i < 24; i++)
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = $"Skyline Block {i}";
                block.transform.SetParent(skyline.transform, false);

                var x = skylineStartX + i * skylineSpacing;
                var height = 6f + Mathf.PingPong(i * 3.1f, 12f);
                block.transform.position = new Vector3(x, height * 0.5f - 0.5f, backdropZ);
                block.transform.localScale = new Vector3(3.2f, height, 3.2f);
                block.GetComponent<MeshRenderer>().sharedMaterial = RetrowaveStyle.CreateLitMaterial(
                    new Color(0.03f, 0.02f, 0.09f),
                    new Color(0.55f, 0.08f, 0.42f),
                    0.55f,
                    0f);
                DisableCollider(block);
            }

            var sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sun.name = "Synth Sun";
            sun.transform.SetParent(parent, false);
            sun.transform.position = new Vector3(0f, 22f, backdropZ - 28f);
            sun.transform.localScale = new Vector3(22f, 22f, 3f);
            sun.GetComponent<MeshRenderer>().sharedMaterial = RetrowaveStyle.CreateUnlitMaterial(new Color(1f, 0.42f, 0.16f, 1f));
            DisableCollider(sun);
        }

        private static void ConfigureLighting()
        {
            var visibilityScale = Mathf.InverseLerp(84f, 200f, RetrowaveArenaConfig.OuterHalfLength);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.05f, 0.02f, 0.08f);
            RenderSettings.fogDensity = Mathf.Lerp(0.012f, 0.0052f, visibilityScale);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.17f, 0.09f, 0.22f);
            RenderSettings.reflectionIntensity = 0.45f;

            var light = Object.FindFirstObjectByType<Light>();

            if (light == null)
            {
                var lightObject = new GameObject("Retrowave Directional Light");
                light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
            }

            light.color = new Color(0.46f, 0.66f, 1f);
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(42f, -24f, 0f);
        }

        private static GameObject CreateStrip(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = name;
            strip.transform.SetParent(parent, false);
            strip.transform.position = position;
            strip.transform.localScale = scale;
            strip.GetComponent<MeshRenderer>().sharedMaterial = material;
            return strip;
        }

        private static GameObject CreateFloorDecal(Transform parent, string name, Vector3 position, Vector2 size, Material material)
        {
            var decal = GameObject.CreatePrimitive(PrimitiveType.Plane);
            decal.name = name;
            decal.transform.SetParent(parent, false);
            decal.transform.position = position;
            decal.transform.localScale = new Vector3(size.x * 0.1f, 1f, size.y * 0.1f);
            decal.GetComponent<MeshRenderer>().sharedMaterial = material;
            DisableCollider(decal);
            return decal;
        }

        private static GameObject CreateFloorDisc(Transform parent, string name, Vector3 position, float radius, int segments, Material material)
        {
            var clampedSegments = Mathf.Max(12, segments);
            var disc = new GameObject(name);
            disc.transform.SetParent(parent, false);
            disc.transform.position = position;

            var vertices = new Vector3[clampedSegments + 1];
            var triangles = new int[clampedSegments * 3];
            vertices[0] = Vector3.zero;

            for (var i = 0; i < clampedSegments; i++)
            {
                var angle = (i / (float)clampedSegments) * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }

            for (var i = 0; i < clampedSegments; i++)
            {
                var next = (i + 1) % clampedSegments;
                var triangleIndex = i * 3;
                triangles[triangleIndex] = 0;
                triangles[triangleIndex + 1] = next + 1;
                triangles[triangleIndex + 2] = i + 1;
            }

            var mesh = new Mesh { name = $"{name} Mesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            disc.AddComponent<MeshFilter>().sharedMesh = mesh;
            disc.AddComponent<MeshRenderer>().sharedMaterial = material;
            return disc;
        }

        private static void CreateBarrierVolume(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            var barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrier.name = name;
            barrier.transform.SetParent(parent, false);
            barrier.transform.position = position;
            barrier.transform.localScale = scale;

            var collider = barrier.GetComponent<Collider>();

            if (collider != null)
            {
                collider.material = ResolvePerimeterBounceMaterial();
            }

            var renderer = barrier.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }

        private static PhysicsMaterial ResolvePerimeterBounceMaterial()
        {
            if (_perimeterBounceMaterial != null)
            {
                return _perimeterBounceMaterial;
            }

            _perimeterBounceMaterial = new PhysicsMaterial("RT_PerimeterBounce")
            {
                bounciness = 0.34f,
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };

            return _perimeterBounceMaterial;
        }

        private static void DisableCollider(GameObject gameObject)
        {
            var collider = gameObject.GetComponent<Collider>();

            if (collider != null)
            {
                collider.enabled = false;
            }
        }
    }

    public sealed class RetrowaveGoalVolume : MonoBehaviour
    {
        public RetrowaveTeam Team;

        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            if (!other.TryGetComponent<RetrowaveBall>(out _))
            {
                return;
            }

            RetrowaveMatchManager.Instance?.HandleGoal(Team);
        }
    }
}
