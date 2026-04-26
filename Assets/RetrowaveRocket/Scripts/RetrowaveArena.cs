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
        private const float FixedRampWidth = 18f;
        private const float FixedRampDepth = 24f;
        private const float FixedRampHeight = 14f;
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

        public Vector3 BallSpawnPoint => new Vector3(0f, 1.35f, 0f);

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
            var signature = (signatureSeed * 1000) + (settings.MaxPlayers * 10) + powerUpCount;

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
            var columns = Mathf.Max(2, _currentLayout.SpawnColumns);
            var row = slot / columns;
            var column = slot % columns;
            var columnProgress = columns <= 1 ? 0.5f : column / (float)(columns - 1);
            var lateral = Mathf.Lerp(-_currentLayout.SpawnLaneHalfWidth, _currentLayout.SpawnLaneHalfWidth, columnProgress);
            var depth = team == RetrowaveTeam.Blue
                ? -_currentLayout.SpawnStartDepth - row * _currentLayout.SpawnRowSpacing
                : _currentLayout.SpawnStartDepth + row * _currentLayout.SpawnRowSpacing;
            return new Vector3(lateral, 1.35f, depth);
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
        private static GameObject _arenaRoot;
        private static int _builtLayoutSignature = int.MinValue;

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
            var inGoalChannel = Mathf.Abs(z) > RetrowaveArenaConfig.FlatHalfLength && Mathf.Abs(x) <= RetrowaveArenaConfig.GoalHalfWidth;

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
            meshRenderer.sharedMaterial = RetrowaveStyle.CreateLitMaterial(
                RetrowaveStyle.ArenaBase,
                new Color(0.02f, 0.12f, 0.18f),
                0.9f,
                0.02f);

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
            var triangles = new int[widthSegments * lengthSegments * 6];

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

            var triangleIndex = 0;

            for (var z = 0; z < lengthSegments; z++)
            {
                for (var x = 0; x < widthSegments; x++)
                {
                    var root = z * verticesPerRow + x;
                    triangles[triangleIndex++] = root;
                    triangles[triangleIndex++] = root + verticesPerRow;
                    triangles[triangleIndex++] = root + 1;
                    triangles[triangleIndex++] = root + 1;
                    triangles[triangleIndex++] = root + verticesPerRow;
                    triangles[triangleIndex++] = root + verticesPerRow + 1;
                }
            }

            var mesh = new Mesh
            {
                name = "Retrowave Arena Surface",
                vertices = vertices,
                triangles = triangles,
                uv = uvs,
            };

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildFieldStrips(Transform parent)
        {
            for (var z = -30f; z <= 30f; z += 6f)
            {
                CreateStrip(
                    parent,
                    $"Lane Strip {z:0}",
                    new Vector3(0f, 0.03f, z),
                    new Vector3(RetrowaveArenaConfig.FlatHalfWidth * 1.9f, 0.05f, 0.18f),
                    RetrowaveStyle.CreateUnlitMaterial(new Color(0.05f, 0.7f, 1f, 0.95f)));
            }

            for (var x = -18f; x <= 18f; x += 6f)
            {
                CreateStrip(
                    parent,
                    $"Cross Strip {x:0}",
                    new Vector3(x, 0.03f, 0f),
                    new Vector3(0.18f, 0.05f, RetrowaveArenaConfig.FlatHalfLength * 1.7f),
                    RetrowaveStyle.CreateUnlitMaterial(new Color(1f, 0.18f, 0.74f, 0.9f)));
            }

            var centerDisk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            centerDisk.name = "Center Disk";
            centerDisk.transform.SetParent(parent, false);
            centerDisk.transform.position = new Vector3(0f, 0.04f, 0f);
            centerDisk.transform.localScale = new Vector3(9f, 0.03f, 9f);
            centerDisk.GetComponent<MeshRenderer>().sharedMaterial =
                RetrowaveStyle.CreateUnlitMaterial(new Color(1f, 0.25f, 0.65f, 0.9f));
            DisableCollider(centerDisk);
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
            var frameMaterial = RetrowaveStyle.CreateLitMaterial(
                RetrowaveStyle.GetTeamBase(team),
                teamGlow * 2.5f,
                0.92f,
                0.06f);

            var goalCenterZ = direction * (RetrowaveArenaConfig.FlatHalfLength + RetrowaveArenaConfig.GoalDepth * 0.55f);

            CreateStrip(
                root.transform,
                "Floor",
                new Vector3(0f, 0.03f, goalCenterZ),
                new Vector3(RetrowaveArenaConfig.GoalHalfWidth * 2.15f, 0.06f, RetrowaveArenaConfig.GoalDepth),
                frameMaterial);

            CreateStrip(
                root.transform,
                "Back Wall",
                new Vector3(0f, 2.6f, direction * (RetrowaveArenaConfig.FlatHalfLength + RetrowaveArenaConfig.GoalDepth)),
                new Vector3(RetrowaveArenaConfig.GoalHalfWidth * 2.1f, RetrowaveArenaConfig.GoalHeight, 0.35f),
                frameMaterial);

            var sideX = RetrowaveArenaConfig.GoalHalfWidth + 0.1f;
            CreateStrip(
                root.transform,
                "Left Wall",
                new Vector3(-sideX, 2.6f, goalCenterZ),
                new Vector3(0.35f, RetrowaveArenaConfig.GoalHeight, RetrowaveArenaConfig.GoalDepth),
                frameMaterial);
            CreateStrip(
                root.transform,
                "Right Wall",
                new Vector3(sideX, 2.6f, goalCenterZ),
                new Vector3(0.35f, RetrowaveArenaConfig.GoalHeight, RetrowaveArenaConfig.GoalDepth),
                frameMaterial);
            CreateStrip(
                root.transform,
                "Top Bar",
                new Vector3(0f, RetrowaveArenaConfig.GoalHeight + 0.1f, goalCenterZ),
                new Vector3(RetrowaveArenaConfig.GoalHalfWidth * 2.1f, 0.35f, RetrowaveArenaConfig.GoalDepth),
                frameMaterial);

            var trigger = new GameObject("Goal Trigger");
            trigger.transform.SetParent(root.transform, false);
            trigger.transform.position = new Vector3(0f, RetrowaveArenaConfig.GoalHeight * 0.45f, goalCenterZ);
            var boxCollider = trigger.AddComponent<BoxCollider>();
            boxCollider.isTrigger = true;
            boxCollider.size = new Vector3(
                RetrowaveArenaConfig.GoalHalfWidth * 1.95f,
                RetrowaveArenaConfig.GoalHeight * 0.9f,
                RetrowaveArenaConfig.GoalDepth * 0.85f);

            var goalVolume = trigger.AddComponent<RetrowaveGoalVolume>();
            goalVolume.Team = team;

            var lightObject = new GameObject("Glow");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.position = new Vector3(0f, 4f, goalCenterZ - direction * 1.2f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 18f;
            light.intensity = 9f;
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

        private static void CreateBarrierVolume(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            var barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrier.name = name;
            barrier.transform.SetParent(parent, false);
            barrier.transform.position = position;
            barrier.transform.localScale = scale;

            var renderer = barrier.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                renderer.enabled = false;
            }
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
