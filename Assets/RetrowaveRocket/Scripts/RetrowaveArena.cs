using Unity.Netcode;
using UnityEngine;

namespace RetrowaveRocket
{
    public enum RetrowaveTeam
    {
        Blue = 0,
        Orange = 1,
    }

    public enum RetrowaveLobbyRole
    {
        Spectator = 0,
        Blue = 1,
        Orange = 2,
    }

    public enum RetrowavePowerUpType
    {
        BoostRefill = 0,
        SpeedBurst = 1,
    }

    public static class RetrowaveArenaConfig
    {
        public const float FlatHalfWidth = 38f;
        public const float FlatHalfLength = 58f;
        public const float GoalHalfWidth = 10f;
        public const float GoalDepth = 10f;
        public const float GoalHeight = 7f;
        public const float OuterHalfWidth = 52f;
        public const float OuterHalfLength = 74f;
        public const float RampWidth = OuterHalfWidth - FlatHalfWidth;
        public const float RampDepth = OuterHalfLength - FlatHalfLength;
        public const float RampHeight = 12f;
        public const float CeilingHeight = 34f;
        public const float MaxBoost = 100f;
        public const float StartingBoost = 55f;
        public const float PowerUpRespawnSeconds = 8f;
        public const float SpeedBurstMultiplier = 1.4f;
        public const float SpeedBurstDuration = 4.5f;
        public const float PassiveBoostRegen = 6f;

        public static readonly Vector3 BallSpawnPoint = new Vector3(0f, 1.35f, 0f);

        public static readonly Vector3[] PowerUpPositions =
        {
            new Vector3(-24f, 1.2f, -18f),
            new Vector3(24f, 1.2f, -18f),
            new Vector3(-24f, 1.2f, 18f),
            new Vector3(24f, 1.2f, 18f),
            new Vector3(0f, 1.2f, -38f),
            new Vector3(0f, 1.2f, 38f),
        };

        public static Vector3 GetSpawnPoint(RetrowaveTeam team, int slot)
        {
            var row = slot / 3;
            var column = slot % 3;
            var lateral = column switch
            {
                0 => -14f,
                1 => 14f,
                _ => 0f,
            };
            var depth = team == RetrowaveTeam.Blue ? -34f - row * 8f : 34f + row * 8f;
            return new Vector3(lateral, 1.35f, depth);
        }

        public static Quaternion GetSpawnRotation(RetrowaveTeam team)
        {
            return team == RetrowaveTeam.Blue ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
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
        public static Color OrangeBase => new Color(1f, 0.36f, 0.18f);
        public static Color OrangeGlow => new Color(1f, 0.24f, 0.75f);
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
            return team == RetrowaveTeam.Blue ? BlueBase : OrangeBase;
        }

        public static Color GetTeamGlow(RetrowaveTeam team)
        {
            return team == RetrowaveTeam.Blue ? BlueGlow : OrangeGlow;
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

        public static void EnsureBuilt()
        {
            if (_arenaRoot != null)
            {
                return;
            }

            _arenaRoot = new GameObject("Retrowave Arena");
            Object.DontDestroyOnLoad(_arenaRoot);

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
            BuildGoal(parent, RetrowaveTeam.Orange, 1f);
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

            for (var i = 0; i < 24; i++)
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = $"Skyline Block {i}";
                block.transform.SetParent(skyline.transform, false);

                var x = -55f + i * 4.6f;
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

            var sky = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sky.name = "Sky Dome";
            sky.transform.SetParent(parent, false);
            sky.transform.position = Vector3.zero;
            sky.transform.localScale = Vector3.one * 320f;
            sky.GetComponent<SphereCollider>().enabled = false;

            var skyMaterial = RetrowaveStyle.CreateLitMaterial(
                new Color(0.03f, 0.01f, 0.08f),
                new Color(0.01f, 0.02f, 0.06f),
                1f,
                0f);
            skyMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Front);
            sky.GetComponent<MeshRenderer>().sharedMaterial = skyMaterial;
            DisableCollider(sky);
        }

        private static void ConfigureLighting()
        {
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.05f, 0.02f, 0.08f);
            RenderSettings.fogDensity = 0.012f;
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
