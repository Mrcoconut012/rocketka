using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural spaceport surroundings:
/// - Builds a terrain mesh using layered Perlin noise.
/// - Adds crater-shaped depressions and raised rims.
/// - Scatters angular low-poly rocks made from generated meshes.
/// - Avoids configured zones around the hangar, rails, launch pad and rocket flight path.
///
/// Setup:
/// 1. Create an empty GameObject and add this component.
/// 2. Set World Center, Terrain Size and Ground Y to match your scene.
/// 3. Assign trigger colliders to Exclusion Triggers to keep their areas clear.
/// 4. Use the component context menu: Generate Environment / Clear Generated Environment.
/// </summary>
[DisallowMultipleComponent]
public class PerlinSpaceportEnvironment : MonoBehaviour
{
    [Header("Terrain")]
    [Min(20f)] public float terrainWidth = 300f;
    [Min(20f)] public float terrainDepth = 300f;
    [Min(0.1f)] public float vertexSpacing = 2f;
    public Vector3 worldCenter = Vector3.zero;
    public float groundY = 0f;
    [Min(0f)] public float baseRelief = 2f;
    [Min(0f)] public float noiseHeight = 8f;
    [Min(0.0001f)] public float noiseScale = 0.025f;
    [Range(1, 6)] public int noiseOctaves = 4;
    [Range(0.1f, 1f)] public float persistence = 0.5f;
    [Min(1f)] public float lacunarity = 2f;
    public int seed = 12345;
    public Material terrainMaterial;

    [Header("Crater formation")]
    [Min(0)] public int craterCount = 18;
    [Min(2f)] public float craterMinRadius = 4f;
    [Min(2f)] public float craterMaxRadius = 14f;
    [Min(0f)] public float craterDepth = 3.5f;
    [Min(0f)] public float craterRimHeight = 1.5f;
    [Range(0.05f, 0.5f)] public float craterRimWidth = 0.18f;

    [Header("Procedural rocks")]
    [Min(0)] public int rockCount = 500;
    [Min(0.2f)] public float rockMinScale = 0.5f;
    [Min(0.2f)] public float rockMaxScale = 2.5f;
    [Range(0.05f, 1f)] public float rockAngularity = 0.25f;
    [Range(0, 3)]
    [Tooltip("Extra mesh subdivision for smoother, rounder rocks. 0 = low-poly, 1-2 = smoother, 3 = heavy.")]
    public int rockSubdivision = 1;
    [Range(0f, 1f)]
    [Tooltip("Blend the rock mesh normals for a smoother visual surface.")]
    public float rockNormalSmoothing = 1f;
    public Material rockMaterial;
    [Min(0)] public int rockSeedOffset = 800;

    [Header("World-space exclusion zones")]
    [Tooltip("Keep this area clear for the hangar.")]
    public bool avoidHangar = true;
    public Vector3 hangarCenter = new Vector3(0f, 0f, 40f);
    public Vector2 hangarHalfSize = new Vector2(18f, 14f);

    [Tooltip("Keep this corridor clear along the rails. Set endpoints in world coordinates.")]
    public bool avoidRails = true;
    public Vector3 railStart = new Vector3(0f, 0f, 20f);
    public Vector3 railEnd = new Vector3(0f, 0f, -20f);
    [Min(0f)] public float railHalfWidth = 4f;
    [Min(0f)] public float railEndPadding = 3f;

    [Tooltip("Keep this area clear for the launch pad.")]
    public bool avoidLaunchPad = true;
    public Vector3 launchPadCenter = new Vector3(0f, 0f, -35f);
    public Vector2 launchPadHalfSize = new Vector2(15f, 15f);

    [Tooltip("Keep rocks away from the rocket's initial flight path.")]
    public bool avoidRocketFlightPath = true;
    public Vector3 flightPathCenter = new Vector3(0f, 0f, -35f);
    [Min(0f)] public float flightPathRadius = 8f;

    [Header("Collider exclusion zones")]
    [Tooltip("Assign trigger colliders here. Rocks, craters and terrain detail will be excluded from their XZ area.")]
    public List<Collider> exclusionTriggers = new List<Collider>();
    [Min(0f), Tooltip("Distance over which terrain slopes down toward the flat zone.")]
    public float exclusionFadeDistance = 25f;
    [Tooltip("Optional material for the flat planes inside exclusion trigger zones. If empty, the terrain material is used.")]
    public Material exclusionPlaneMaterial;
    [Tooltip("Height of the flat zone plane. Use 0 for exactly Y=0; 0.02 can help cover the underlying terrain.")]
    public float exclusionPlaneYOffset = 0.02f;

    [Header("Generation")]
    [Min(1)] public int placementAttemptsPerRock = 12;
    public bool generateOnStart = false;
    public bool createColliders = false;

    private const string GeneratedRootName = "__PerlinSpaceport_Generated";
    private Transform generatedRoot;
    private Mesh generatedTerrainMesh;
    private readonly List<Mesh> generatedRockMeshes = new List<Mesh>();
    private Material fallbackTerrainMaterial;
    private Material fallbackRockMaterial;
    private readonly List<Vector2> craterCenters = new List<Vector2>();
    private readonly List<float> craterRadii = new List<float>();

    private void Start()
    {
        if (generateOnStart)
            GenerateEnvironment();
    }

    [ContextMenu("Generate Environment")]
    public void GenerateEnvironment()
    {
        ClearGeneratedEnvironment();
        Random.InitState(seed);
        craterCenters.Clear();
        craterRadii.Clear();
        CreateGeneratedRoot();
        CreateCraters();
        CreateTerrain();
        ScatterRocks();
        CreateExclusionPlanes();
    }

    [ContextMenu("Clear Generated Environment")]
    public void ClearGeneratedEnvironment()
    {
        Transform oldRoot = transform.Find(GeneratedRootName);
        if (oldRoot != null)
        {
            if (Application.isPlaying) Destroy(oldRoot.gameObject);
            else DestroyImmediate(oldRoot.gameObject);
        }

        if (generatedTerrainMesh != null)
        {
            if (Application.isPlaying) Destroy(generatedTerrainMesh);
            else DestroyImmediate(generatedTerrainMesh);
            generatedTerrainMesh = null;
        }

        foreach (Mesh mesh in generatedRockMeshes)
        {
            if (mesh == null) continue;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
        generatedRockMeshes.Clear();
        generatedRoot = null;
    }

    private void CreateGeneratedRoot()
    {
        GameObject root = new GameObject(GeneratedRootName);
        root.transform.SetParent(transform, false);
        generatedRoot = root.transform;
    }

    private void CreateExclusionPlanes()
    {
        if (exclusionTriggers == null || exclusionTriggers.Count == 0)
            return;

        Material planeMaterial = exclusionPlaneMaterial != null
            ? exclusionPlaneMaterial
            : terrainMaterial;

        if (planeMaterial == null)
        {
            if (fallbackTerrainMaterial == null)
                fallbackTerrainMaterial = CreateFallbackMaterial(new Color(0.34f, 0.31f, 0.27f));
            planeMaterial = fallbackTerrainMaterial;
        }

        for (int i = 0; i < exclusionTriggers.Count; i++)
        {
            Collider zone = exclusionTriggers[i];
            if (zone == null || !zone.enabled || !zone.isTrigger)
                continue;

            Bounds bounds = zone.bounds;
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Exclusion Zone Plane";
            plane.transform.SetParent(generatedRoot, true);
            plane.transform.position = new Vector3(
                bounds.center.x,
                exclusionPlaneYOffset,
                bounds.center.z);

            // Unity's built-in Plane is 10x10 units at scale (1,1,1).
            plane.transform.localScale = new Vector3(
                Mathf.Max(0.01f, bounds.size.x / 10f),
                1f,
                Mathf.Max(0.01f, bounds.size.z / 10f));

            Renderer planeRenderer = plane.GetComponent<Renderer>();
            planeRenderer.sharedMaterial = planeMaterial;

            // Keep the generated plane's collider, as requested.
            // Remove this component if the plane should be visual-only.
        }
    }

    private void CreateCraters()
    {
        int safeAttempts = Mathf.Max(craterCount * 15, 100);
        int attempts = 0;

        while (craterCenters.Count < craterCount && attempts < safeAttempts)
        {
            attempts++;
            float radius = Random.Range(craterMinRadius, craterMaxRadius);
            float x = Random.Range(-terrainWidth * 0.5f + radius, terrainWidth * 0.5f - radius);
            float z = Random.Range(-terrainDepth * 0.5f + radius, terrainDepth * 0.5f - radius);
            Vector3 world = new Vector3(worldCenter.x + x, groundY, worldCenter.z + z);

            if (IsInExclusionZone(world, radius * 0.75f))
                continue;

            bool overlaps = false;
            for (int i = 0; i < craterCenters.Count; i++)
            {
                float minDist = radius + craterRadii[i] * 0.8f;
                if (Vector2.Distance(new Vector2(x, z), craterCenters[i]) < minDist)
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
            {
                craterCenters.Add(new Vector2(x, z));
                craterRadii.Add(radius);
            }
        }
    }

    private void CreateTerrain()
    {
        int xSegments = Mathf.Max(2, Mathf.CeilToInt(terrainWidth / vertexSpacing));
        int zSegments = Mathf.Max(2, Mathf.CeilToInt(terrainDepth / vertexSpacing));
        int vertexCount = (xSegments + 1) * (zSegments + 1);

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        List<int> triangles = new List<int>(xSegments * zSegments * 6);

        float startX = worldCenter.x - terrainWidth * 0.5f;
        float startZ = worldCenter.z - terrainDepth * 0.5f;
        int v = 0;

        for (int z = 0; z <= zSegments; z++)
        {
            for (int x = 0; x <= xSegments; x++)
            {
                float localX = x * terrainWidth / xSegments;
                float localZ = z * terrainDepth / zSegments;
                float worldX = startX + localX;
                float worldZ = startZ + localZ;
                float height = GetTerrainHeight(worldX, worldZ);

                vertices[v] = new Vector3(worldX, groundY + height, worldZ);
                uvs[v] = new Vector2(localX / 10f, localZ / 10f);
                v++;
            }
        }

        for (int z = 0; z < zSegments; z++)
        {
            for (int x = 0; x < xSegments; x++)
            {
                float cellWorldX = startX + (x + 0.5f) * terrainWidth / xSegments;
                float cellWorldZ = startZ + (z + 0.5f) * terrainDepth / zSegments;
                Vector3 cellCenter = new Vector3(cellWorldX, groundY, cellWorldZ);

                // Keep terrain triangles continuous under the flat zone plane to avoid holes.
                // The flat plane is placed slightly above this surface and visually covers it.
                int i = z * (xSegments + 1) + x;
                triangles.Add(i);
                triangles.Add(i + xSegments + 1);
                triangles.Add(i + 1);

                triangles.Add(i + 1);
                triangles.Add(i + xSegments + 1);
                triangles.Add(i + xSegments + 2);
            }
        }

        generatedTerrainMesh = new Mesh();
        generatedTerrainMesh.name = "Generated_Perlin_Terrain";
        if (vertices.Length > 65535)
            generatedTerrainMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        generatedTerrainMesh.vertices = vertices;
        generatedTerrainMesh.uv = uvs;
        generatedTerrainMesh.triangles = triangles.ToArray();
        generatedTerrainMesh.RecalculateNormals();
        generatedTerrainMesh.RecalculateBounds();

        GameObject terrain = new GameObject("Procedural Terrain");
        terrain.transform.SetParent(generatedRoot, true);
        MeshFilter filter = terrain.AddComponent<MeshFilter>();
        filter.sharedMesh = generatedTerrainMesh;
        MeshRenderer renderer = terrain.AddComponent<MeshRenderer>();
        if (terrainMaterial != null)
            renderer.sharedMaterial = terrainMaterial;
        else
        {
            if (fallbackTerrainMaterial == null)
                fallbackTerrainMaterial = CreateFallbackMaterial(new Color(0.34f, 0.31f, 0.27f));
            renderer.sharedMaterial = fallbackTerrainMaterial;
        }

        if (createColliders)
        {
            MeshCollider collider = terrain.AddComponent<MeshCollider>();
            collider.sharedMesh = generatedTerrainMesh;
        }
    }

    private float GetTerrainHeight(float worldX, float worldZ)
    {
        float noise = FractalNoise(worldX, worldZ);
        float height = baseRelief + noise * noiseHeight;

        float localX = worldX - worldCenter.x;
        float localZ = worldZ - worldCenter.z;

        for (int i = 0; i < craterCenters.Count; i++)
        {
            float distance = Vector2.Distance(new Vector2(localX, localZ), craterCenters[i]);
            float radius = craterRadii[i];
            if (distance > radius * 1.4f) continue;

            float normalized = distance / radius;

            // Smooth bowl: deepest at center, fading before the outer edge.
            float bowl = Mathf.Exp(-Mathf.Pow(normalized / 0.62f, 2f));
            height -= craterDepth * bowl;

            // Raised rim, fading smoothly on both sides.
            float rimDistance = Mathf.Abs(normalized - 1f) / Mathf.Max(0.01f, craterRimWidth);
            float rim = Mathf.Exp(-rimDistance * rimDistance * 2f);
            height += craterRimHeight * rim;
        }

        // Ease the generated terrain down at the outer boundary to reduce a sharp edge.
        float edgeX = Mathf.Min(localX + terrainWidth * 0.5f, terrainWidth * 0.5f - localX);
        float edgeZ = Mathf.Min(localZ + terrainDepth * 0.5f, terrainDepth * 0.5f - localZ);
        float edgeDistance = Mathf.Min(edgeX, edgeZ);
        float edgeFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edgeDistance / 18f));
        height *= edgeFade;

        // Fade terrain height toward the edge of an exclusion trigger zone.
        height *= GetExclusionZoneFade(worldX, worldZ);
        return height;
    }

    private float FractalNoise(float worldX, float worldZ)
    {
        float amplitude = 1f;
        float frequency = noiseScale;
        float total = 0f;
        float maxAmplitude = 0f;

        for (int octave = 0; octave < noiseOctaves; octave++)
        {
            float offsetX = seed * 0.173f + octave * 37.1f;
            float offsetZ = seed * 0.319f + octave * 19.7f;
            float sampleX = (worldX + offsetX) * frequency;
            float sampleZ = (worldZ + offsetZ) * frequency;

            total += Mathf.PerlinNoise(sampleX, sampleZ) * amplitude;
            maxAmplitude += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        if (maxAmplitude <= 0f) return 0f;
        return (total / maxAmplitude) * 2f - 1f;
    }

    private void ScatterRocks()
    {
        if (rockCount <= 0) return;
        Random.State previousState = Random.state;
        Random.InitState(seed + rockSeedOffset);

        for (int i = 0; i < rockCount; i++)
        {
            bool placed = false;

            for (int attempt = 0; attempt < placementAttemptsPerRock; attempt++)
            {
                float x = Random.Range(-terrainWidth * 0.5f, terrainWidth * 0.5f);
                float z = Random.Range(-terrainDepth * 0.5f, terrainDepth * 0.5f);
                Vector3 position = new Vector3(worldCenter.x + x, groundY, worldCenter.z + z);
                if (IsInExclusionZone(position, 1f)) continue;

                float y = GetTerrainHeight(position.x, position.z);
                float scale = Random.Range(rockMinScale, rockMaxScale);
                Vector3 rockPosition = new Vector3(position.x, groundY + y, position.z);

                GameObject rock = new GameObject("Procedural Rock");
                rock.transform.SetParent(generatedRoot, true);
                rock.transform.position = rockPosition;
                rock.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                rock.transform.localScale = new Vector3(
                    scale * Random.Range(0.7f, 1.3f),
                    scale * Random.Range(0.55f, 1.25f),
                    scale * Random.Range(0.7f, 1.3f));

                Mesh rockMesh = CreateRockMesh(
                    rockAngularity,
                    seed + rockSeedOffset + i * 17,
                    rockSubdivision,
                    rockNormalSmoothing);
                generatedRockMeshes.Add(rockMesh);
                MeshFilter mf = rock.AddComponent<MeshFilter>();
                mf.sharedMesh = rockMesh;
                MeshRenderer mr = rock.AddComponent<MeshRenderer>();
                if (rockMaterial != null)
                    mr.sharedMaterial = rockMaterial;
                else
                {
                    if (fallbackRockMaterial == null)
                        fallbackRockMaterial = CreateFallbackMaterial(new Color(0.27f, 0.26f, 0.25f));
                    mr.sharedMaterial = fallbackRockMaterial;
                }

                if (createColliders)
                {
                    MeshCollider mc = rock.AddComponent<MeshCollider>();
                    mc.sharedMesh = rockMesh;
                    mc.convex = true;
                }

                placed = true;
                break;
            }

            if (!placed)
                Debug.LogWarning("PerlinSpaceportEnvironment: Could not place all rocks. Check exclusion zones and terrain size.", this);
        }

        Random.state = previousState;
    }

    // Generates an irregular, angular rock mesh from a subdivided icosahedron.
    private Mesh CreateRockMesh(float angularity, int rockSeed, int subdivisions, float normalSmoothing)
    {
        Random.State previousState = Random.state;
        Random.InitState(rockSeed);

        // Start from an octahedron for a lightweight, angular silhouette.
        Vector3[] baseVertices =
        {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
        };
        int[] baseTriangles =
        {
            0,2,4, 0,4,3, 0,3,5, 0,5,2,
            1,4,2, 1,3,4, 1,5,3, 1,2,5
        };

        List<Vector3> vertices = new List<Vector3>(baseVertices);
        List<int> triangles = new List<int>(baseTriangles);

        // Subdivide the octahedron repeatedly to make rocks rounder.
        List<int> refined = new List<int>(triangles);
        for (int level = 0; level < Mathf.Clamp(subdivisions, 0, 3); level++)
        {
            Dictionary<long, int> midpointCache = new Dictionary<long, int>();
            List<int> nextTriangles = new List<int>(refined.Count * 4);

            for (int i = 0; i < refined.Count; i += 3)
            {
                int a = refined[i];
                int b = refined[i + 1];
                int c = refined[i + 2];
                int ab = GetMidpoint(a, b, vertices, midpointCache);
                int bc = GetMidpoint(b, c, vertices, midpointCache);
                int ca = GetMidpoint(c, a, vertices, midpointCache);

                nextTriangles.AddRange(new[] {
                    a, ab, ca,
                    b, bc, ab,
                    c, ca, bc,
                    ab, bc, ca
                });
            }

            refined = nextTriangles;
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 v = vertices[i].normalized;
            float distortion = Random.Range(1f - angularity * 0.45f, 1f + angularity * 0.45f);
            v = new Vector3(v.x * distortion, v.y * distortion, v.z * distortion);
            v.y *= Random.Range(0.65f, 1.05f);
            vertices[i] = v;
        }

        Mesh mesh = new Mesh();
        mesh.name = "Generated_LowPoly_Rock";
        mesh.SetVertices(vertices);
        mesh.SetTriangles(refined, 0);
        mesh.RecalculateNormals();

        // Blend flat triangle normals toward averaged vertex normals for a smoother appearance.
        if (normalSmoothing > 0f)
        {
            Vector3[] smoothNormals = mesh.normals;
            Vector3[] flatNormals = new Vector3[mesh.vertexCount];
            int[] meshTriangles = mesh.triangles;

            for (int i = 0; i < meshTriangles.Length; i += 3)
            {
                int ia = meshTriangles[i];
                int ib = meshTriangles[i + 1];
                int ic = meshTriangles[i + 2];
                Vector3 faceNormal = Vector3.Cross(
                    mesh.vertices[ib] - mesh.vertices[ia],
                    mesh.vertices[ic] - mesh.vertices[ia]).normalized;
                flatNormals[ia] += faceNormal;
                flatNormals[ib] += faceNormal;
                flatNormals[ic] += faceNormal;
            }

            for (int i = 0; i < flatNormals.Length; i++)
            {
                flatNormals[i] = flatNormals[i].sqrMagnitude > 0.0001f
                    ? flatNormals[i].normalized
                    : smoothNormals[i];
                smoothNormals[i] = Vector3.Slerp(flatNormals[i], smoothNormals[i], normalSmoothing).normalized;
            }

            mesh.normals = smoothNormals;
        }

        mesh.RecalculateBounds();
        Random.state = previousState;
        return mesh;
    }

    private int GetMidpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
    {
        int min = Mathf.Min(a, b);
        int max = Mathf.Max(a, b);
        long key = ((long)min << 32) + max;
        if (cache.TryGetValue(key, out int index))
            return index;

        index = vertices.Count;
        vertices.Add((vertices[a] + vertices[b]).normalized);
        cache[key] = index;
        return index;
    }

    private bool IsInExclusionZone(Vector3 p, float padding)
    {
        if (IsInsideTriggerExclusion(p))
            return true;

        if (avoidHangar && InsideRectangle(p, hangarCenter, hangarHalfSize + Vector2.one * padding))
            return true;

        if (avoidLaunchPad && InsideRectangle(p, launchPadCenter, launchPadHalfSize + Vector2.one * padding))
            return true;

        if (avoidRails && DistanceToSegmentXZ(p, railStart, railEnd) < railHalfWidth + padding)
        {
            Vector2 point = new Vector2(p.x, p.z);
            Vector2 a = new Vector2(railStart.x, railStart.z);
            Vector2 b = new Vector2(railEnd.x, railEnd.z);
            float length = Vector2.Distance(a, b);
            float along = Vector2.Dot(point - a, (b - a).normalized);
            if (along >= -railEndPadding - padding && along <= length + railEndPadding + padding)
                return true;
        }

        if (avoidRocketFlightPath)
        {
            Vector2 p2 = new Vector2(p.x, p.z);
            Vector2 c = new Vector2(flightPathCenter.x, flightPathCenter.z);
            if (Vector2.Distance(p2, c) < flightPathRadius + padding)
                return true;
        }

        return false;
    }

    private float GetExclusionZoneFade(float worldX, float worldZ)
    {
        if (exclusionTriggers == null || exclusionFadeDistance <= 0f)
            return 1f;

        float fade = 1f;
        for (int i = 0; i < exclusionTriggers.Count; i++)
        {
            Collider zone = exclusionTriggers[i];
            if (zone == null || !zone.enabled || !zone.isTrigger)
                continue;

            Bounds b = zone.bounds;
            // Distance to the horizontal rectangle of the collider bounds.
            float dx = Mathf.Max(Mathf.Abs(worldX - b.center.x) - b.extents.x, 0f);
            float dz = Mathf.Max(Mathf.Abs(worldZ - b.center.z) - b.extents.z, 0f);
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            float zoneFade = Mathf.SmoothStep(
                0f, 1f, Mathf.Clamp01(distance / exclusionFadeDistance));
            fade = Mathf.Min(fade, zoneFade);
        }
        return fade;
    }

    private bool IsInsideTriggerExclusion(Vector3 worldPosition)
    {
        if (exclusionTriggers == null)
            return false;

        for (int i = 0; i < exclusionTriggers.Count; i++)
        {
            Collider zone = exclusionTriggers[i];
            if (zone == null || !zone.enabled || !zone.isTrigger)
                continue;

            // Test the horizontal footprint at the collider's vertical center,
            // so the trigger does not need to intersect the terrain's exact Y value.
            Vector3 testPoint = new Vector3(
                worldPosition.x,
                zone.bounds.center.y,
                worldPosition.z);

            Vector3 closest = zone.ClosestPoint(testPoint);
            if ((closest - testPoint).sqrMagnitude < 0.0001f)
                return true;
        }

        return false;
    }

    private static bool InsideRectangle(Vector3 p, Vector3 center, Vector2 halfSize)
    {
        return Mathf.Abs(p.x - center.x) < halfSize.x &&
               Mathf.Abs(p.z - center.z) < halfSize.y;
    }

    private static float DistanceToSegmentXZ(Vector3 p, Vector3 a3, Vector3 b3)
    {
        Vector2 p2 = new Vector2(p.x, p.z);
        Vector2 a = new Vector2(a3.x, a3.z);
        Vector2 b = new Vector2(b3.x, b3.z);
        Vector2 ab = b - a;
        float denom = ab.sqrMagnitude;
        if (denom < 0.0001f) return Vector2.Distance(p2, a);
        float t = Mathf.Clamp01(Vector2.Dot(p2 - a, ab) / denom);
        return Vector2.Distance(p2, a + ab * t);
    }

    private Material CreateFallbackMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Diffuse");
        Material mat = new Material(shader);
        mat.color = color;
        return mat;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.7f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(
            new Vector3(worldCenter.x, groundY, worldCenter.z),
            new Vector3(terrainWidth, 0.1f, terrainDepth));

        if (avoidHangar)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(hangarCenter, new Vector3(hangarHalfSize.x * 2f, 0.2f, hangarHalfSize.y * 2f));
        }

        if (avoidLaunchPad)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(launchPadCenter, new Vector3(launchPadHalfSize.x * 2f, 0.2f, launchPadHalfSize.y * 2f));
        }

        if (avoidRails)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(railStart, railEnd);
        }

        if (avoidRocketFlightPath)
        {
            Gizmos.color = Color.magenta;
            Vector3 c = new Vector3(flightPathCenter.x, groundY, flightPathCenter.z);
            Gizmos.DrawWireSphere(c, flightPathRadius);
        }
    }
}
