using UnityEngine;

/// <summary>
/// Procedural environment for an existing rocket spaceport.
/// Adds a ground slab, rock formations, crater rims, distant hills, and optional sky/light.
/// Does not search for or modify existing hangar, rails, or launch-pad objects.
/// Attach to an empty GameObject; set protected zones in world coordinates before generating.
/// </summary>
[DisallowMultipleComponent]
public class SpaceportEnvironmentGenerator : MonoBehaviour
{
    [Header("Generation")]
    public int seed = 20261020;
    public bool generateOnStart = false;
    public bool clearPreviousGenerationFirst = true;

    [Header("World")]
    [Min(100f)] public float worldSize = 600f;
    public Vector3 worldCenter = Vector3.zero;
    [Min(2f)] public float groundThickness = 4f;
    public float groundTopY = 0f;

    [Header("Optional materials")]
    public Material groundMaterial;
    public Material rockMaterial;
    public Material cliffMaterial;
    public Material craterMaterial;

    [Header("Low rocky mounds")]
    [Min(0)] public int moundCount = 35;
    public Vector2 moundRadius = new Vector2(4f, 12f);
    public Vector2 moundHeight = new Vector2(0.4f, 2.2f);

    [Header("Rock formations")]
    [Min(0)] public int rockFormationCount = 90;
    public Vector2 formationRadius = new Vector2(3f, 10f);
    public Vector2 rockSize = new Vector2(1.2f, 5f);
    [Range(2, 7)] public int rocksPerFormationMin = 2;
    [Range(2, 9)] public int rocksPerFormationMax = 5;

    [Header("Crater fields")]
    [Min(0)] public int craterCount = 14;
    public Vector2 craterRadius = new Vector2(7f, 18f);
    [Range(0.08f, 0.3f)] public float craterRimHeightFactor = 0.16f;
    [Range(0.15f, 0.5f)] public float craterRimWidthFactor = 0.28f;

    [Header("Distant hills")]
    public bool createDistantHills = true;
    [Range(12, 80)] public int hillCount = 42;
    public Vector2 hillRadius = new Vector2(14f, 38f);
    public Vector2 hillHeight = new Vector2(12f, 40f);
    [Min(0f)] public float hillRingRadius = 250f;
    [Range(0f, 0.35f)] public float hillRingJitter = 0.15f;

    [Header("Protected hangar zone — world coordinates")]
    public Vector3 hangarCenter = Vector3.zero;
    public Vector2 hangarHalfExtents = new Vector2(18f, 18f);

    [Header("Protected rails / rollout corridor — world coordinates")]
    public Vector3 railsStart = new Vector3(0f, 0f, 20f);
    public Vector3 railsEnd = new Vector3(0f, 0f, 100f);
    [Min(1f)] public float railsClearHalfWidth = 8f;
    [Min(0f)] public float railsEndPadding = 8f;

    [Header("Protected launch pad zone — world coordinates")]
    public Vector3 launchPadCenter = new Vector3(0f, 0f, 120f);
    public Vector2 launchPadHalfExtents = new Vector2(25f, 25f);

    [Header("Rocket flight clearance")]
    public Vector3 flightPathCenter = new Vector3(0f, 0f, 120f);
    [Min(5f)] public float flightPathRadius = 22f;
    [Min(20f)] public float flightPathHeight = 300f;

    [Header("Clearance and placement")]
    [Min(0f)] public float extraClearance = 4f;
    [Range(5, 100)] public int placementAttempts = 35;

    [Header("Optional sky and lighting")]
    public bool setSkyAndLighting = false;
    public Material skyboxMaterial;
    public Color ambientColor = new Color(0.36f, 0.34f, 0.32f);
    public bool createSunIfMissing = true;
    public Color sunColor = new Color(1f, 0.78f, 0.58f);
    [Range(0f, 2f)] public float sunIntensity = 1f;
    public Vector3 sunRotation = new Vector3(20f, -30f, 0f);

    private const string RootName = "__GeneratedSpaceportEnvironment";

    private void Start()
    {
        if (generateOnStart) GenerateSpaceportEnvironment();
    }

    [ContextMenu("Generate Spaceport Environment")]
    public void GenerateSpaceportEnvironment()
    {
        if (clearPreviousGenerationFirst) ClearGeneratedEnvironment();

        GameObject rootObject = new GameObject(RootName);
        Transform root = rootObject.transform;
        root.SetParent(transform, true);

        Random.State oldState = Random.state;
        Random.InitState(seed);

        CreateGround(root);
        CreateMounds(root);
        CreateCraters(root);
        CreateRockFormations(root);
        if (createDistantHills) CreateHills(root);

        Random.state = oldState;
        if (setSkyAndLighting) ConfigureSkyAndLighting(root);

        Debug.Log("Spaceport environment generated. Existing scene objects were not modified.", this);
    }

    [ContextMenu("Clear Generated Environment")]
    public void ClearGeneratedEnvironment()
    {
        Transform old = transform.Find(RootName);
        if (old == null) return;
        if (Application.isPlaying) Destroy(old.gameObject);
        else DestroyImmediate(old.gameObject);
    }

    private void CreateGround(Transform root)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Environment Ground";
        ground.transform.SetParent(root, true);
        ground.transform.position = new Vector3(worldCenter.x, groundTopY - groundThickness * 0.5f, worldCenter.z);
        ground.transform.localScale = new Vector3(worldSize, groundThickness, worldSize);
        ApplyMaterial(ground, groundMaterial, new Color(0.30f, 0.27f, 0.24f));
    }

    private void CreateMounds(Transform root)
    {
        for (int i = 0; i < moundCount; i++)
        {
            if (!TryRandomPosition(out Vector3 p, 0f)) continue;
            float r = Random.Range(moundRadius.x, moundRadius.y);
            float h = Random.Range(moundHeight.x, moundHeight.y);
            if (IsProtected(p, r)) continue;

            GameObject mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mound.name = "Low Rocky Mound";
            mound.transform.SetParent(root, true);
            mound.transform.position = new Vector3(p.x, groundTopY - h * 0.35f, p.z);
            mound.transform.localScale = new Vector3(r * 2f, h, r * 1.7f);
            ApplyMaterial(mound, groundMaterial, new Color(0.32f, 0.29f, 0.26f));
        }
    }

    private void CreateCraters(Transform root)
    {
        for (int i = 0; i < craterCount; i++)
        {
            if (!TryRandomPosition(out Vector3 p, 0f)) continue;
            float r = Random.Range(craterRadius.x, craterRadius.y);
            if (IsProtected(p, r * 1.2f)) continue;

            GameObject bowl = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bowl.name = "Crater Bowl";
            bowl.transform.SetParent(root, true);
            bowl.transform.position = new Vector3(p.x, groundTopY + 0.02f, p.z);
            bowl.transform.localScale = new Vector3(r * 2f, r * 0.12f, r * 2f);
            ApplyMaterial(bowl, craterMaterial, new Color(0.20f, 0.18f, 0.17f));

            int segments = Random.Range(7, 12);
            float rimH = r * craterRimHeightFactor;
            float rimW = r * craterRimWidthFactor;
            for (int j = 0; j < segments; j++)
            {
                float a = Mathf.PI * 2f * j / segments + Random.Range(-0.18f, 0.18f);
                float rr = r * Random.Range(0.82f, 1.12f);
                Vector3 q = p + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rim.name = "Crater Rim Rock";
                rim.transform.SetParent(root, true);
                rim.transform.position = new Vector3(q.x, groundTopY + rimH * Random.Range(0.2f, 0.55f), q.z);
                rim.transform.rotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
                rim.transform.localScale = new Vector3(rimW * Random.Range(0.7f, 1.3f), rimH * Random.Range(0.8f, 1.5f), rimW * Random.Range(0.7f, 1.3f));
                ApplyMaterial(rim, rockMaterial, new Color(0.28f, 0.26f, 0.24f));
            }
        }
    }

    private void CreateRockFormations(Transform root)
    {
        for (int i = 0; i < rockFormationCount; i++)
        {
            if (!TryRandomPosition(out Vector3 center, 0f)) continue;
            float spread = Random.Range(formationRadius.x, formationRadius.y);
            if (IsProtected(center, spread + rockSize.y)) continue;

            int count = Random.Range(rocksPerFormationMin, rocksPerFormationMax + 1);
            for (int j = 0; j < count; j++)
            {
                Vector2 offset = Random.insideUnitCircle * spread;
                Vector3 p = center + new Vector3(offset.x, 0f, offset.y);
                float s = Random.Range(rockSize.x, rockSize.y);
                if (IsProtected(p, s * 0.8f)) continue;
                CreateRock(root, p, s);
            }
        }
    }

    private void CreateRock(Transform root, Vector3 p, float s)
    {
        GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        rock.name = "Angular Rock";
        rock.transform.SetParent(root, true);
        rock.transform.position = new Vector3(p.x, groundTopY + s * 0.25f, p.z);
        rock.transform.rotation = Quaternion.Euler(Random.Range(-18f, 18f), Random.Range(0f, 360f), Random.Range(-18f, 18f));
        rock.transform.localScale = new Vector3(s * Random.Range(0.75f, 1.45f), s * Random.Range(0.45f, 0.95f), s * Random.Range(0.7f, 1.35f));
        ApplyMaterial(rock, rockMaterial, new Color(0.34f, 0.31f, 0.28f));
    }

    private void CreateHills(Transform root)
    {
        for (int i = 0; i < hillCount; i++)
        {
            float angle = Mathf.PI * 2f * i / hillCount + Random.Range(-0.10f, 0.10f);
            float d = hillRingRadius * Random.Range(1f - hillRingJitter, 1f + hillRingJitter);
            Vector3 p = worldCenter + new Vector3(Mathf.Cos(angle) * d, 0f, Mathf.Sin(angle) * d);
            float r = Random.Range(hillRadius.x, hillRadius.y);
            float h = Random.Range(hillHeight.x, hillHeight.y);
            if (IsProtected(p, r * 1.25f)) continue;

            GameObject hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hill.name = "Distant Rocky Hill";
            hill.transform.SetParent(root, true);
            hill.transform.position = new Vector3(p.x, groundTopY + h * 0.28f, p.z);
            hill.transform.localScale = new Vector3(r * 2.5f, h, r * 1.9f);
            ApplyMaterial(hill, cliffMaterial, new Color(0.26f, 0.25f, 0.24f));
        }
    }

    private bool TryRandomPosition(out Vector3 p, float radius)
    {
        float half = worldSize * 0.5f;
        for (int i = 0; i < placementAttempts; i++)
        {
            p = new Vector3(Random.Range(worldCenter.x - half, worldCenter.x + half), groundTopY, Random.Range(worldCenter.z - half, worldCenter.z + half));
            if (!IsProtected(p, radius)) return true;
        }
        p = Vector3.zero;
        return false;
    }

    private bool IsProtected(Vector3 p, float objectRadius)
    {
        float pad = extraClearance + objectRadius;
        if (InsideBoxXZ(p, hangarCenter, hangarHalfExtents + Vector2.one * pad)) return true;
        if (InsideBoxXZ(p, launchPadCenter, launchPadHalfExtents + Vector2.one * pad)) return true;

        Vector2 point = new Vector2(p.x, p.z);
        Vector2 a = new Vector2(railsStart.x, railsStart.z);
        Vector2 b = new Vector2(railsEnd.x, railsEnd.z);
        Vector2 ab = b - a;
        float denom = ab.sqrMagnitude;
        float t = denom > 0.0001f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / denom) : 0f;
        Vector2 nearest = a + ab * t;
        if (Vector2.Distance(point, nearest) <= railsClearHalfWidth + pad) return true;
        if (Vector2.Distance(point, a) <= railsEndPadding + pad || Vector2.Distance(point, b) <= railsEndPadding + pad) return true;

        Vector2 flight = new Vector2(flightPathCenter.x, flightPathCenter.z);
        return Vector2.Distance(point, flight) <= flightPathRadius + pad;
    }

    private static bool InsideBoxXZ(Vector3 p, Vector3 center, Vector2 half)
    {
        return Mathf.Abs(p.x - center.x) <= half.x && Mathf.Abs(p.z - center.z) <= half.y;
    }

    private void ConfigureSkyAndLighting(Transform root)
    {
        if (skyboxMaterial != null) RenderSettings.skybox = skyboxMaterial;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor;

        if (createSunIfMissing && RenderSettings.sun == null)
        {
            GameObject sunObject = new GameObject("Environment Directional Light");
            sunObject.transform.SetParent(root, true);
            sunObject.transform.rotation = Quaternion.Euler(sunRotation);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
        }
    }

    private static void ApplyMaterial(GameObject obj, Material material, Color color)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer == null) return;
        if (material != null)
        {
            renderer.sharedMaterial = material;
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogWarning("No compatible shader found. Assign a material in the Inspector.", obj);
            return;
        }

        Material instance = new Material(shader);
        if (instance.HasProperty("_BaseColor")) instance.SetColor("_BaseColor", color);
        else if (instance.HasProperty("_Color")) instance.color = color;
        renderer.sharedMaterial = instance;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(hangarCenter, new Vector3(hangarHalfExtents.x * 2f, 2f, hangarHalfExtents.y * 2f));
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(launchPadCenter, new Vector3(launchPadHalfExtents.x * 2f, 2f, launchPadHalfExtents.y * 2f));
        Gizmos.color = Color.red;
        Gizmos.DrawLine(railsStart, railsEnd);
        Vector3 side = Vector3.Cross((railsEnd - railsStart).normalized, Vector3.up) * railsClearHalfWidth;
        Gizmos.DrawLine(railsStart + side, railsEnd + side);
        Gizmos.DrawLine(railsStart - side, railsEnd - side);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(flightPathCenter, flightPathRadius);
        Gizmos.DrawLine(flightPathCenter, flightPathCenter + Vector3.up * flightPathHeight);
    }
}
