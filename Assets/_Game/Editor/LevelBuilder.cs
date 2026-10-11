using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Generates the whole "Al-Rashid Power Plant" mission: materials, weapon data, prefabs, greybox level,
// guards, hostages, extraction, cameras, NavMesh. Menu: Hostage > Build Level.
//
// Coordinates: building footprint x[-20,20] z[-15,15]. Floors at FloorY[i] (F1, F2, F3, Roof).
// North = +z. Player spawns south (z≈-80). Front gate on the south fence.
public static class LevelBuilder
{
    const string Root = "Assets/_Game";
    const string ScenePath = Root + "/Scenes/Mission_PowerPlant.unity";
    const string NavMeshPath = Root + "/Scenes/Mission_PowerPlant_NavMesh.asset";
    static readonly string[] GameLayers = { "Player", "Enemy", "Hostage", "Environment", "Interactable", "Viewmodel" };

    static readonly float[] FloorY = { 0.1f, 4.1f, 8.1f, 12.1f };
    const float Slab = 0.3f;
    const float OuterT = 0.3f;
    const float InnerT = 0.2f;

    static Material mSand, mConcrete, mConcreteDark, mMetal, mRust, mTerrorist, mTerroristDark, mSuit, mShirt, mSkin,
        mPlayerArms, mGunMetal, mRope, mWood, mSandbag, mHelipad, mZone, mTracer, mImpact, mFlash, mHeli, mRock, mFence, mConsole, mWhite;

    static Transform env;
    static Font font;

    // =====================================================================================
    [MenuItem("Hostage/Build Level")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureLayers();
        EnsureFolders();
        CreateMaterials();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var usp = CreateWeaponData("USP-S", w =>
        {
            w.weaponName = "USP-S (suppressed)"; w.automatic = false; w.damage = 34; w.fireRate = 400; w.magSize = 12; w.reserveAmmo = 48;
            w.reloadTime = 1.9f; w.spread = 0.6f; w.recoilPitch = 1.4f; w.recoilYaw = 0.4f; w.impulseForce = 0.25f; w.noiseDb = 60; w.reloadNoiseDb = 30;
        });
        var m4 = CreateWeaponData("M4A1-S", w =>
        {
            w.weaponName = "M4A1-S (suppressed)"; w.automatic = true; w.damage = 28; w.fireRate = 700; w.magSize = 25; w.reserveAmmo = 125;
            w.reloadTime = 2.6f; w.spread = 1.1f; w.recoilPitch = 0.7f; w.recoilYaw = 0.35f; w.impulseForce = 0.2f; w.noiseDb = 72; w.reloadNoiseDb = 30;
        });

        // Prefabs (built in a temp scene object, saved, then instantiated in the mission scene).
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var playerPrefab = SavePrefab(BuildPlayer(usp, m4), "Player");
        var guardPrefab = SavePrefab(BuildGuard(), "Guard");
        var hostagePrefab = SavePrefab(BuildHostage(), "Hostage");
        var heliPrefab = SavePrefab(BuildHeli(), "Heli");

        SetupLighting();
        env = new GameObject("Environment").transform;
        BuildGround();
        BuildFence();
        BuildOutposts();
        BuildDecor();
        BuildPlant();
        BuildRoof(out var helipad, out var zone);

        // ---- Player + cameras
        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
        player.transform.SetPositionAndRotation(new Vector3(0, 0.05f, -80f), Quaternion.identity);
        var pc = player.GetComponent<PlayerController>();
        BuildCameras(pc.head);

        // ---- Guards
        var guards = new GameObject("Guards").transform;
        var routes = new GameObject("PatrolRoutes").transform;
        BuildGuards(guardPrefab, guards, routes);

        // ---- Hostages
        var hostages = new GameObject("Hostages").transform;
        void H(Vector3 p, float yaw) => Place(hostagePrefab, hostages, p, yaw);
        H(new Vector3(-17.5f, FloorY[0], 12.5f), 30);    // F1 turbine hall corner
        H(new Vector3(-15f, FloorY[1], 9f), 90);         // F2 control room
        H(new Vector3(-11f, FloorY[1], 12f), 200);       // F2 control room
        H(new Vector3(12f, FloorY[2], 9f), 0);           // F3 interrogation
        H(new Vector3(16f, FloorY[2], 12.5f), 120);      // F3 interrogation

        // ---- Extraction
        var heli = (GameObject)PrefabUtility.InstantiatePrefab(heliPrefab);
        heli.transform.position = helipad.position + Vector3.up * 1.4f;
        heli.GetComponent<Helicopter>().landingPoint = helipad;

        // ---- Systems
        var systems = new GameObject("GameSystems");
        var gm = systems.AddComponent<GameManager>();
        gm.player = pc;
        gm.extractionZone = zone;
        gm.helicopter = heli.GetComponent<Helicopter>();
        gm.tracerMaterial = mTracer;
        gm.impactMaterial = mImpact;
        gm.flashMaterial = mFlash;

        var alarm = systems.AddComponent<AlarmSystem>();
        alarm.siren = systems.AddComponent<AudioSource>();
        alarm.siren.playOnAwake = false;
        alarm.siren.loop = true;
        var rally = new GameObject("RallyPoints").transform;
        rally.SetParent(systems.transform);
        alarm.rallyPoints = new[]
        {
            new Vector3(14f, FloorY[1], -1f),   // F2 by stairwell
            new Vector3(10f, FloorY[2], -13.5f),// F3 stair landing
            new Vector3(0f, FloorY[3], -8f),    // roof
            new Vector3(-10f, FloorY[3], 10f),  // roof
            new Vector3(12f, FloorY[3], 8f),    // roof
            new Vector3(-18f, FloorY[1], 12.5f) // F2 fire-escape door
        }.Select((p, i) => { var t = new GameObject("Rally_" + i).transform; t.SetParent(rally); t.position = p; return t; }).ToArray();

        var ui = new GameObject("MissionUI");
        ui.AddComponent<MissionUI>();
        var fx = new GameObject("PostFX");
        fx.AddComponent<PostFX>().playerHealth = player.GetComponent<Health>();

        // ---- NavMesh
        var navGo = new GameObject("NavMesh");
        var surface = navGo.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Volume;
        surface.center = new Vector3(0, 9, 0);
        surface.size = new Vector3(140, 26, 130);
        surface.layerMask = LayerMask.GetMask("Environment");
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
        if (surface.navMeshData != null)
        {
            AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
        }
        navGo.AddComponent<NavMeshBootstrap>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[LevelBuilder] Mission_PowerPlant built and saved. Press Play.");
    }

    // =====================================================================================
    // Project setup

    static void EnsureLayers()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        foreach (var name in GameLayers)
        {
            bool exists = false;
            for (int i = 0; i < layers.arraySize; i++) if (layers.GetArrayElementAtIndex(i).stringValue == name) exists = true;
            if (exists) continue;
            for (int i = 6; i < layers.arraySize; i++)
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue)) { el.stringValue = name; break; }
            }
        }
        tagManager.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }

    static void EnsureFolders()
    {
        foreach (var sub in new[] { "Scenes", "Prefabs", "Materials", "Data" })
            if (!AssetDatabase.IsValidFolder($"{Root}/{sub}")) AssetDatabase.CreateFolder(Root, sub);
    }

    static Material Mat(string name, Color c, float smooth = 0.15f, float metal = 0f, string shaderName = "Universal Render Pipeline/Lit")
    {
        string path = $"{Root}/Materials/{name}.mat";
        var shader = Shader.Find(shaderName) ?? Shader.Find("Universal Render Pipeline/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void CreateMaterials()
    {
        mSand = Mat("Sand", new Color(0.82f, 0.68f, 0.47f), 0.05f);
        mConcrete = Mat("Concrete", new Color(0.66f, 0.63f, 0.58f), 0.1f);
        mConcreteDark = Mat("ConcreteDark", new Color(0.45f, 0.43f, 0.40f), 0.1f);
        mMetal = Mat("Metal", new Color(0.42f, 0.45f, 0.47f), 0.45f, 0.6f);
        mRust = Mat("Rust", new Color(0.48f, 0.3f, 0.2f), 0.2f, 0.3f);
        mFence = Mat("Fence", new Color(0.55f, 0.56f, 0.55f), 0.4f, 0.5f);
        mTerrorist = Mat("Terrorist", new Color(0.72f, 0.6f, 0.42f), 0.1f);
        mTerroristDark = Mat("TerroristGear", new Color(0.42f, 0.36f, 0.26f), 0.15f);
        mSuit = Mat("Suit", new Color(0.06f, 0.06f, 0.07f), 0.3f);
        mShirt = Mat("Shirt", new Color(0.92f, 0.92f, 0.9f), 0.1f);
        mSkin = Mat("Skin", new Color(0.78f, 0.6f, 0.47f), 0.2f);
        mPlayerArms = Mat("PlayerArms", new Color(0.33f, 0.36f, 0.22f), 0.15f);
        mGunMetal = Mat("GunMetal", new Color(0.12f, 0.12f, 0.13f), 0.5f, 0.7f);
        mRope = Mat("Rope", new Color(0.55f, 0.42f, 0.25f), 0.05f);
        mWood = Mat("Wood", new Color(0.55f, 0.4f, 0.24f), 0.1f);
        mSandbag = Mat("Sandbag", new Color(0.7f, 0.62f, 0.45f), 0.05f);
        mRock = Mat("Rock", new Color(0.58f, 0.47f, 0.36f), 0.1f);
        mHelipad = Mat("Helipad", new Color(0.25f, 0.26f, 0.27f), 0.2f);
        mConsole = Mat("Console", new Color(0.3f, 0.36f, 0.35f), 0.4f, 0.3f);
        mHeli = Mat("Heli", new Color(0.27f, 0.3f, 0.22f), 0.3f, 0.2f);
        mWhite = Mat("Marking", new Color(0.95f, 0.95f, 0.9f), 0.1f);
        mZone = Mat("ExtractionMarker", new Color(0.2f, 1f, 0.35f), 0, 0, "Universal Render Pipeline/Unlit");
        mFlash = Mat("MuzzleFlash", new Color(4f, 2.8f, 1.2f), 0, 0, "Universal Render Pipeline/Unlit");
        mImpact = Mat("Impact", new Color(0.08f, 0.07f, 0.06f), 0, 0, "Universal Render Pipeline/Unlit");
        mTracer = Mat("Tracer", new Color(3f, 2.4f, 1.4f), 0, 0, "Universal Render Pipeline/Particles/Unlit");
    }

    static WeaponData CreateWeaponData(string name, System.Action<WeaponData> setup)
    {
        string path = $"{Root}/Data/{name}.asset";
        var w = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        if (w == null)
        {
            w = ScriptableObject.CreateInstance<WeaponData>();
            setup(w);
            AssetDatabase.CreateAsset(w, path);
        }
        return w; // keep designer tweaks on rebuild
    }

    static GameObject SavePrefab(GameObject go, string name)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/{name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject Place(GameObject prefab, Transform parent, Vector3 pos, float yaw)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        return go;
    }

    static void AddSceneToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // =====================================================================================
    // Primitive helpers

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material m,
        Vector3 euler = default, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // World-space environment box (layer Environment, static).
    static GameObject Box(string name, Vector3 center, Vector3 size, Material m, Vector3 euler = default, Transform parent = null)
    {
        var go = Prim(PrimitiveType.Cube, name, parent != null ? parent : env, center, size, m, euler);
        go.layer = LayerMask.NameToLayer("Environment");
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static GameObject BoxMinMax(string name, Vector3 min, Vector3 max, Material m, Transform parent = null)
        => Box(name, (min + max) * 0.5f, max - min, m, default, parent);

    // Cylinder/sphere with an accurate mesh collider instead of the primitive's capsule/sphere.
    static GameObject MeshProp(PrimitiveType type, string name, Vector3 center, Vector3 scale, Material m, Vector3 euler = default, Transform parent = null)
    {
        var go = Prim(type, name, parent != null ? parent : env, center, scale, m, euler, collider: false);
        go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        go.layer = LayerMask.NameToLayer("Environment");
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    // Wall running along X (alongX) or Z, from s0 to s1, base y0, height h.
    // Openings: x = from, y = to (along the wall), z = bottom, w = top (heights relative to y0).
    static void Wall(string name, bool alongX, float fixedCoord, float s0, float s1, float y0, float h, float thick, Material m, Transform parent, params Vector4[] openings)
    {
        void Seg(float a, float b, float lo, float hi)
        {
            if (b - a < 0.01f || hi - lo < 0.01f) return;
            float mid = (a + b) * 0.5f, len = b - a, cy = y0 + (lo + hi) * 0.5f, sh = hi - lo;
            if (alongX) Box(name, new Vector3(mid, cy, fixedCoord), new Vector3(len, sh, thick), m, default, parent);
            else Box(name, new Vector3(fixedCoord, cy, mid), new Vector3(thick, sh, len), m, default, parent);
        }
        float cursor = s0;
        foreach (var o in openings.OrderBy(o => o.x))
        {
            Seg(cursor, o.x, 0, h);
            Seg(o.x, o.y, 0, o.z);
            Seg(o.x, o.y, o.w, h);
            cursor = o.y;
        }
        Seg(cursor, s1, 0, h);
    }

    static Vector4 Door(float a, float b, float top = 2.6f) => new Vector4(a, b, 0f, top);
    static Vector4 Window(float a, float b) => new Vector4(a, b, 1.0f, 2.2f);

    // Floor slab (top at yTop) with one optional rectangular hole (xMin, zMin, xMax, zMax).
    static void SlabWithHole(string name, float x0, float x1, float z0, float z1, float yTop, Material m, Transform parent, Vector4? hole = null)
    {
        float y0 = yTop - Slab;
        if (hole == null) { BoxMinMax(name, new Vector3(x0, y0, z0), new Vector3(x1, yTop, z1), m, parent); return; }
        var h = hole.Value;
        void R(float ax, float bx, float az, float bz) { if (bx - ax > 0.01f && bz - az > 0.01f) BoxMinMax(name, new Vector3(ax, y0, az), new Vector3(bx, yTop, bz), m, parent); }
        R(x0, x1, z0, h.y);   // south strip
        R(x0, x1, h.w, z1);   // north strip
        R(x0, h.x, h.y, h.w); // west
        R(h.z, x1, h.y, h.w); // east
    }

    // Walkable ramp from bottom to top (centre-line points on the walking surface).
    static void Ramp(string name, Vector3 bottom, Vector3 top, float width, Material m, Transform parent)
    {
        const float thick = 0.25f;
        Vector3 dir = (top - bottom).normalized;
        Vector3 b = bottom - dir * 0.6f; // sink the low end into the floor below
        var rot = Quaternion.LookRotation(dir, Vector3.up);
        Vector3 center = (b + top) * 0.5f - rot * Vector3.up * (thick * 0.5f);
        var go = Box(name, center, new Vector3(width, thick, Vector3.Distance(b, top)), m, default, parent);
        go.transform.rotation = rot;
    }

    static void PointLight(Vector3 pos, float range, float intensity, Color c, Transform parent)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = c;
        l.shadows = LightShadows.None;
    }

    // =====================================================================================
    // Lighting / atmosphere

    static void SetupLighting()
    {
        var sunGo = new GameObject("Sun");
        sunGo.transform.rotation = Quaternion.Euler(24f, -35f, 0f);
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.85f, 0.66f);
        sun.intensity = 1.7f;
        sun.shadows = LightShadows.Soft;

        RenderSettings.sun = sun;
        RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.74f);
        RenderSettings.ambientEquatorColor = new Color(0.62f, 0.54f, 0.43f);
        RenderSettings.ambientGroundColor = new Color(0.36f, 0.3f, 0.22f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.007f;
        RenderSettings.fogColor = new Color(0.84f, 0.75f, 0.62f);
    }

    static void BuildCameras(Transform head)
    {
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1200f;
        cam.fieldOfView = 70f;
        cam.cullingMask = ~(1 << LayerMask.NameToLayer("Viewmodel"));
        camGo.AddComponent<AudioListener>();
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        camGo.AddComponent<CinemachineBrain>();
        camGo.transform.SetPositionAndRotation(head.position, head.rotation);

        var vcamGo = new GameObject("FPS CinemachineCamera");
        var vcam = vcamGo.AddComponent<CinemachineCamera>();
        vcam.Target.TrackingTarget = head;
        var lens = LensSettings.Default;
        lens.FieldOfView = 70f;
        lens.NearClipPlane = 0.05f;
        lens.FarClipPlane = 1200f;
        vcam.Lens = lens;
        vcamGo.AddComponent<CinemachineHardLockToTarget>();
        vcamGo.AddComponent<CinemachineRotateWithFollowTarget>();
        vcamGo.AddComponent<CinemachineImpulseListener>();
        vcamGo.transform.SetPositionAndRotation(head.position, head.rotation);
    }

    // =====================================================================================
    // Characters

    static GameObject BuildPlayer(WeaponData usp, WeaponData m4)
    {
        int playerLayer = LayerMask.NameToLayer("Player");
        int vmLayer = LayerMask.NameToLayer("Viewmodel");

        var root = new GameObject("Player");
        root.tag = "Player";
        root.layer = playerLayer;
        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0);
        cc.stepOffset = 0.4f; cc.slopeLimit = 50f; cc.skinWidth = 0.05f;
        var hp = root.AddComponent<Health>();
        hp.maxHealth = 100; hp.regenDelay = 5f; hp.regenRate = 25f;

        var head = new GameObject("Head").transform;
        head.SetParent(root.transform, false);
        head.localPosition = new Vector3(0, 1.65f, 0);

        var vmCamGo = new GameObject("ViewmodelCamera");
        vmCamGo.transform.SetParent(head, false);
        var vmCam = vmCamGo.AddComponent<Camera>();
        vmCam.fieldOfView = 58f;
        vmCam.nearClipPlane = 0.01f;
        vmCam.farClipPlane = 5f;
        vmCam.cullingMask = 1 << vmLayer;
        var vmData = vmCam.GetUniversalAdditionalCameraData();
        vmData.renderType = CameraRenderType.Overlay;
        vmData.renderShadows = false;

        var weaponsRoot = new GameObject("Weapons").transform;
        weaponsRoot.SetParent(head, false);

        var pistol = BuildViewmodel("USP-S", weaponsRoot, usp, rifle: false);
        var rifle = BuildViewmodel("M4A1-S", weaponsRoot, m4, rifle: true);
        foreach (Transform t in weaponsRoot) Layers.SetLayerRecursive(t.gameObject, vmLayer);

        var pc = root.AddComponent<PlayerController>();
        pc.head = head;
        pc.viewmodelCamera = vmCam;
        root.AddComponent<PlayerNoise>();
        root.AddComponent<PlayerInteract>();
        var sw = root.AddComponent<WeaponSwitcher>();
        sw.weapons = new[] { pistol, rifle };
        sw.startIndex = 0;
        rifle.gameObject.SetActive(false);
        return root;
    }

    static Weapon BuildViewmodel(string name, Transform parent, WeaponData data, bool rifle)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var model = new GameObject("Model").transform;
        model.SetParent(go.transform, false);
        model.localPosition = rifle ? new Vector3(0.16f, -0.2f, 0.32f) : new Vector3(0.17f, -0.18f, 0.38f);

        Transform muzzle;
        if (!rifle)
        {
            Prim(PrimitiveType.Cube, "Slide", model, new Vector3(0, 0.03f, 0.04f), new Vector3(0.035f, 0.045f, 0.2f), mGunMetal, collider: false);
            Prim(PrimitiveType.Cube, "Grip", model, new Vector3(0, -0.05f, -0.04f), new Vector3(0.032f, 0.12f, 0.05f), mGunMetal, new Vector3(15, 0, 0), false);
            Prim(PrimitiveType.Cylinder, "Suppressor", model, new Vector3(0, 0.03f, 0.23f), new Vector3(0.035f, 0.09f, 0.035f), mGunMetal, new Vector3(90, 0, 0), false);
            Prim(PrimitiveType.Cube, "Hand", model, new Vector3(0, -0.08f, -0.05f), new Vector3(0.09f, 0.08f, 0.09f), mPlayerArms, collider: false);
            Prim(PrimitiveType.Cube, "Forearm", model, new Vector3(0.02f, -0.13f, -0.25f), new Vector3(0.08f, 0.08f, 0.35f), mPlayerArms, new Vector3(-12, 0, 0), false);
            Prim(PrimitiveType.Cube, "SupportHand", model, new Vector3(-0.05f, -0.07f, -0.02f), new Vector3(0.07f, 0.08f, 0.09f), mPlayerArms, collider: false);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(model, false);
            muzzle.localPosition = new Vector3(0, 0.03f, 0.33f);
        }
        else
        {
            Prim(PrimitiveType.Cube, "Receiver", model, new Vector3(0, 0, 0.05f), new Vector3(0.05f, 0.08f, 0.32f), mGunMetal, collider: false);
            Prim(PrimitiveType.Cube, "Handguard", model, new Vector3(0, 0.005f, 0.32f), new Vector3(0.055f, 0.06f, 0.24f), mGunMetal, collider: false);
            Prim(PrimitiveType.Cylinder, "Suppressor", model, new Vector3(0, 0.01f, 0.56f), new Vector3(0.04f, 0.12f, 0.04f), mGunMetal, new Vector3(90, 0, 0), false);
            Prim(PrimitiveType.Cube, "Stock", model, new Vector3(0, -0.02f, -0.22f), new Vector3(0.045f, 0.09f, 0.22f), mGunMetal, collider: false);
            Prim(PrimitiveType.Cube, "Mag", model, new Vector3(0, -0.09f, 0.08f), new Vector3(0.035f, 0.12f, 0.06f), mGunMetal, new Vector3(10, 0, 0), false);
            Prim(PrimitiveType.Cube, "Grip", model, new Vector3(0, -0.08f, -0.06f), new Vector3(0.035f, 0.1f, 0.045f), mGunMetal, new Vector3(20, 0, 0), false);
            Prim(PrimitiveType.Cube, "Sight", model, new Vector3(0, 0.06f, 0.05f), new Vector3(0.03f, 0.035f, 0.08f), mGunMetal, collider: false);
            Prim(PrimitiveType.Cube, "Hand", model, new Vector3(0, -0.1f, -0.07f), new Vector3(0.08f, 0.08f, 0.09f), mPlayerArms, collider: false);
            Prim(PrimitiveType.Cube, "Forearm", model, new Vector3(0.03f, -0.15f, -0.28f), new Vector3(0.08f, 0.08f, 0.35f), mPlayerArms, new Vector3(-12, 0, 0), false);
            Prim(PrimitiveType.Cube, "SupportHand", model, new Vector3(-0.02f, -0.04f, 0.33f), new Vector3(0.08f, 0.07f, 0.1f), mPlayerArms, collider: false);
            Prim(PrimitiveType.Cube, "SupportArm", model, new Vector3(-0.12f, -0.1f, 0.14f), new Vector3(0.08f, 0.08f, 0.38f), mPlayerArms, new Vector3(-8, 35, 0), false);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(model, false);
            muzzle.localPosition = new Vector3(0, 0.01f, 0.69f);
        }

        var lightGo = new GameObject("MuzzleLight");
        lightGo.transform.SetParent(muzzle, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 5f;
        light.intensity = 3f;
        light.color = new Color(1f, 0.75f, 0.4f);
        light.shadows = LightShadows.None;
        light.enabled = false;

        var flash = Prim(PrimitiveType.Sphere, "Flash", muzzle, new Vector3(0, 0, 0.03f), new Vector3(0.05f, 0.05f, 0.09f), mFlash, collider: false);
        flash.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        flash.SetActive(false);

        var impulse = go.AddComponent<CinemachineImpulseSource>();
        impulse.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Recoil;
        impulse.ImpulseDefinition.ImpulseDuration = 0.12f;
        impulse.DefaultVelocity = new Vector3(0, -0.15f, -0.3f);

        var w = go.AddComponent<Weapon>();
        w.data = data;
        w.model = model;
        w.muzzle = muzzle;
        w.muzzleLight = light;
        w.muzzleFlash = flash;
        w.impulse = impulse;

        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        return w;
    }

    static GameObject BuildGuard()
    {
        int layer = LayerMask.NameToLayer("Enemy");
        var root = new GameObject("Guard");

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.35f; agent.height = 1.8f; agent.speed = 1.8f; agent.angularSpeed = 360f; agent.acceleration = 14f;
        agent.stoppingDistance = 0.3f; agent.autoBraking = true;
        agent.enabled = false; // Guard.Start enables once the NavMesh exists

        var hp = root.AddComponent<Health>();
        hp.maxHealth = 100;
        root.AddComponent<GuardVision>();
        root.AddComponent<GuardHearing>();
        var guard = root.AddComponent<Guard>();

        var body = Prim(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0, 0.75f, 0), new Vector3(0.6f, 0.75f, 0.6f), mTerrorist);
        body.AddComponent<Hitbox>().multiplier = 1f;
        Prim(PrimitiveType.Cube, "Vest", root.transform, new Vector3(0, 1.12f, 0.02f), new Vector3(0.55f, 0.5f, 0.42f), mTerroristDark, collider: false);
        Prim(PrimitiveType.Cube, "Pouches", root.transform, new Vector3(0, 0.95f, 0.22f), new Vector3(0.4f, 0.15f, 0.1f), mTerroristDark, collider: false);
        var head = Prim(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0, 1.66f, 0), Vector3.one * 0.32f, mTerrorist);
        var hb = head.AddComponent<Hitbox>(); hb.multiplier = 4f; hb.isHead = true;
        Prim(PrimitiveType.Sphere, "Helmet", root.transform, new Vector3(0, 1.74f, -0.01f), new Vector3(0.36f, 0.22f, 0.38f), mTerroristDark, collider: false);
        Prim(PrimitiveType.Cube, "Goggles", root.transform, new Vector3(0, 1.68f, 0.14f), new Vector3(0.22f, 0.06f, 0.06f), mGunMetal, collider: false);

        var gun = Prim(PrimitiveType.Cube, "AK", root.transform, new Vector3(0.18f, 1.18f, 0.38f), new Vector3(0.07f, 0.1f, 0.75f), mGunMetal, collider: false);
        Prim(PrimitiveType.Cube, "Mag", gun.transform, new Vector3(0, -1.0f, 0.1f), new Vector3(0.8f, 1.2f, 0.08f), mWood, new Vector3(15, 0, 0), false);
        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(root.transform, false);
        muzzle.localPosition = new Vector3(0.18f, 1.2f, 0.8f);
        guard.muzzle = muzzle;

        var ind = new GameObject("Indicator");
        ind.transform.SetParent(root.transform, false);
        ind.transform.localPosition = new Vector3(0, 2.35f, 0);
        var tm = ind.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 64;
        tm.characterSize = 0.08f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontStyle = FontStyle.Bold;
        tm.text = "";
        ind.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        guard.indicator = tm;

        Layers.SetLayerRecursive(root, layer);
        return root;
    }

    static GameObject BuildHostage()
    {
        int layer = LayerMask.NameToLayer("Hostage");
        var root = new GameObject("Hostage");
        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.3f; agent.height = 1.7f; agent.speed = 3.5f; agent.angularSpeed = 540f; agent.acceleration = 14f;
        agent.stoppingDistance = 0.4f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        agent.enabled = false;
        var hp = root.AddComponent<Health>();
        hp.maxHealth = 50;
        var hostage = root.AddComponent<Hostage>();

        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        var body = Prim(PrimitiveType.Capsule, "Body", visual, new Vector3(0, 0.75f, 0), new Vector3(0.55f, 0.75f, 0.5f), mSuit);
        body.AddComponent<Hitbox>().multiplier = 1f;
        Prim(PrimitiveType.Cube, "Shirt", visual, new Vector3(0, 1.22f, 0.2f), new Vector3(0.2f, 0.32f, 0.08f), mShirt, collider: false);
        Prim(PrimitiveType.Cube, "Tie", visual, new Vector3(0, 1.2f, 0.245f), new Vector3(0.05f, 0.26f, 0.02f), mRust, collider: false);
        var head = Prim(PrimitiveType.Sphere, "Head", visual, new Vector3(0, 1.62f, 0), Vector3.one * 0.3f, mSkin);
        var hhb = head.AddComponent<Hitbox>(); hhb.multiplier = 2f; hhb.isHead = true;
        Prim(PrimitiveType.Cube, "Blindfold", visual, new Vector3(0, 1.65f, 0.02f), new Vector3(0.31f, 0.07f, 0.3f), mShirt, collider: false);

        var ropes = new GameObject("Ropes").transform;
        ropes.SetParent(visual, false);
        Prim(PrimitiveType.Cube, "WristRope", ropes, new Vector3(0, 0.95f, -0.28f), new Vector3(0.3f, 0.08f, 0.1f), mRope, collider: false);
        Prim(PrimitiveType.Cube, "AnkleRope", ropes, new Vector3(0, 0.15f, 0), new Vector3(0.6f, 0.08f, 0.55f), mRope, collider: false);

        hostage.visual = visual;
        hostage.ropes = ropes.gameObject;
        Layers.SetLayerRecursive(root, layer);
        return root;
    }

    static GameObject BuildHeli()
    {
        var root = new GameObject("Helicopter");
        var heli = root.AddComponent<Helicopter>();
        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        Prim(PrimitiveType.Cube, "Fuselage", body, new Vector3(0, 0.9f, 0), new Vector3(2.4f, 2f, 5.2f), mHeli, collider: false);
        Prim(PrimitiveType.Cube, "Cockpit", body, new Vector3(0, 0.8f, 2.9f), new Vector3(2.0f, 1.5f, 1.2f), mGunMetal, collider: false);
        Prim(PrimitiveType.Cube, "TailBoom", body, new Vector3(0, 1.3f, -5f), new Vector3(0.5f, 0.6f, 5.5f), mHeli, collider: false);
        Prim(PrimitiveType.Cube, "Fin", body, new Vector3(0, 2.0f, -7.5f), new Vector3(0.15f, 1.6f, 0.9f), mHeli, collider: false);
        Prim(PrimitiveType.Cube, "SkidL", body, new Vector3(-1.1f, -0.3f, 0.3f), new Vector3(0.12f, 0.12f, 5f), mGunMetal, collider: false);
        Prim(PrimitiveType.Cube, "SkidR", body, new Vector3(1.1f, -0.3f, 0.3f), new Vector3(0.12f, 0.12f, 5f), mGunMetal, collider: false);
        Prim(PrimitiveType.Cylinder, "Mast", body, new Vector3(0, 2.1f, 0), new Vector3(0.25f, 0.25f, 0.25f), mGunMetal, collider: false);

        var rotor = new GameObject("MainRotor").transform;
        rotor.SetParent(root.transform, false);
        rotor.localPosition = new Vector3(0, 2.4f, 0);
        Prim(PrimitiveType.Cube, "Blade1", rotor, Vector3.zero, new Vector3(11f, 0.05f, 0.35f), mGunMetal, collider: false);
        Prim(PrimitiveType.Cube, "Blade2", rotor, Vector3.zero, new Vector3(0.35f, 0.05f, 11f), mGunMetal, collider: false);

        var tail = new GameObject("TailRotor").transform;
        tail.SetParent(root.transform, false);
        tail.localPosition = new Vector3(0.2f, 2.0f, -7.6f);
        Prim(PrimitiveType.Cube, "TailBlade", tail, Vector3.zero, new Vector3(0.05f, 1.8f, 0.2f), mGunMetal, collider: false);

        heli.mainRotor = rotor;
        heli.tailRotor = tail;
        return root;
    }

    // =====================================================================================
    // Outside

    static void BuildGround()
    {
        var ground = Prim(PrimitiveType.Plane, "Ground", env, Vector3.zero, new Vector3(150, 1, 150), mSand);
        ground.layer = LayerMask.NameToLayer("Environment");
        GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.BatchingStatic);

        var dunes = new GameObject("Dunes").transform;
        dunes.SetParent(env);
        void Dune(Vector3 p, Vector3 s) => MeshProp(PrimitiveType.Sphere, "Dune", p, s, mSand, default, dunes);
        Dune(new Vector3(0, -2.2f, -68), new Vector3(36, 7, 12));    // cover in front of spawn
        Dune(new Vector3(-32, -2.5f, -82), new Vector3(30, 8, 16));
        Dune(new Vector3(36, -3f, -78), new Vector3(40, 9, 18));
        Dune(new Vector3(-95, -4f, 15), new Vector3(45, 14, 80));
        Dune(new Vector3(95, -4f, -5), new Vector3(45, 14, 80));
        Dune(new Vector3(0, -5f, 100), new Vector3(140, 16, 35));
        Dune(new Vector3(-70, -2f, -60), new Vector3(20, 6, 14));
        Dune(new Vector3(75, -2f, 50), new Vector3(24, 7, 16));

        var rocks = new GameObject("Rocks").transform;
        rocks.SetParent(env);
        var rnd = new System.Random(7);
        Vector3[] rockSpots =
        {
            new(-12, 0, -62), new(14, 0, -60), new(-40, 0, -62), new(48, 0, -60), new(-66, 0, -20), new(-68, 0, 8),
            new(66, 0, -15), new(68, 0, 12), new(-20, 0, -45), new(22, 0, -40), new(-48, 0, -15), new(50, 0, 0),
        };
        foreach (var p in rockSpots)
        {
            float s = 1.2f + (float)rnd.NextDouble() * 2f;
            Box("Rock", p + Vector3.up * s * 0.3f, new Vector3(s * 1.4f, s, s * 1.1f), mRock, new Vector3(rnd.Next(-15, 15), rnd.Next(0, 360), rnd.Next(-15, 15)), rocks);
        }

        // Humvee near the spawn.
        var hv = new GameObject("Humvee").transform;
        hv.SetParent(env);
        Box("Body", new Vector3(6, 1.1f, -76), new Vector3(2.3f, 1.1f, 4.8f), mPlayerArms, default, hv);
        Box("Cabin", new Vector3(6, 2.0f, -76.4f), new Vector3(2.1f, 0.8f, 2.4f), mPlayerArms, default, hv);
        Box("Turret", new Vector3(6, 2.6f, -76.4f), new Vector3(0.8f, 0.4f, 0.8f), mGunMetal, default, hv);
        foreach (var o in new[] { new Vector3(-1.2f, 0, 1.6f), new Vector3(1.2f, 0, 1.6f), new Vector3(-1.2f, 0, -1.6f), new Vector3(1.2f, 0, -1.6f) })
            MeshProp(PrimitiveType.Cylinder, "Wheel", new Vector3(6, 0.5f, -76) + o, new Vector3(1f, 0.2f, 1f), mGunMetal, new Vector3(0, 0, 90), hv);
    }

    static void BuildFence()
    {
        var fence = new GameObject("Fence").transform;
        fence.SetParent(env);
        const float h = 2.8f, t = 0.12f, x0 = -60, x1 = 60, z0 = -55, z1 = 55;
        Vector4 Gap(float a, float b) => new Vector4(a, b, 0, h);
        Wall("Fence", true, z0, x0, x1, 0, h, t, mFence, fence, Gap(-5, 5));        // front gate
        Wall("Fence", true, z1, x0, x1, 0, h, t, mFence, fence);
        Wall("Fence", false, x1, z0, z1, 0, h, t, mFence, fence, Gap(0, 4));        // cut fence east (back door route)
        Wall("Fence", false, x0, z0, z1, 0, h, t, mFence, fence, Gap(-10, -6));     // cut fence west (vent route)
        // Open gate leaves
        Box("GateLeaf", new Vector3(-6.8f, h * 0.5f, -57.2f), new Vector3(0.1f, h, 4.6f), mMetal, default, fence);
        Box("GateLeaf", new Vector3(6.8f, h * 0.5f, -57.2f), new Vector3(0.1f, h, 4.6f), mMetal, default, fence);
        // Posts
        for (float x = x0; x <= x1; x += 6) { Box("Post", new Vector3(x, h * 0.5f + 0.2f, z0), new Vector3(0.2f, h + 0.4f, 0.2f), mMetal, default, fence); Box("Post", new Vector3(x, h * 0.5f + 0.2f, z1), new Vector3(0.2f, h + 0.4f, 0.2f), mMetal, default, fence); }
        for (float z = z0; z <= z1; z += 6) { Box("Post", new Vector3(x0, h * 0.5f + 0.2f, z), new Vector3(0.2f, h + 0.4f, 0.2f), mMetal, default, fence); Box("Post", new Vector3(x1, h * 0.5f + 0.2f, z), new Vector3(0.2f, h + 0.4f, 0.2f), mMetal, default, fence); }
    }

    static void BuildTower(Vector3 basePos, Transform parent)
    {
        var tw = new GameObject("Watchtower").transform;
        tw.SetParent(parent);
        const float top = 6f;
        foreach (var o in new[] { new Vector3(-1.4f, 0, -1.4f), new Vector3(1.4f, 0, -1.4f), new Vector3(-1.4f, 0, 1.4f), new Vector3(1.4f, 0, 1.4f) })
        {
            Box("Leg", basePos + o + Vector3.up * top * 0.5f, new Vector3(0.25f, top, 0.25f), mWood, default, tw);
            Box("RoofPost", basePos + o + Vector3.up * (top + 1.3f), new Vector3(0.12f, 2.6f, 0.12f), mWood, default, tw);
        }
        Box("Platform", basePos + Vector3.up * (top - 0.1f), new Vector3(3.4f, 0.2f, 3.4f), mWood, default, tw);
        Box("Rail", basePos + new Vector3(0, top + 0.5f, 1.65f), new Vector3(3.4f, 1f, 0.1f), mWood, default, tw);
        Box("Rail", basePos + new Vector3(0, top + 0.5f, -1.65f), new Vector3(3.4f, 1f, 0.1f), mWood, default, tw);
        Box("Rail", basePos + new Vector3(1.65f, top + 0.5f, 0), new Vector3(0.1f, 1f, 3.4f), mWood, default, tw);
        Box("Rail", basePos + new Vector3(-1.65f, top + 0.5f, 0), new Vector3(0.1f, 1f, 3.4f), mWood, default, tw);
        Box("Roof", basePos + Vector3.up * (top + 2.65f), new Vector3(3.8f, 0.15f, 3.8f), mRust, default, tw);
        PointLight(basePos + new Vector3(0, top + 2.3f, 0), 10f, 2f, new Color(1f, 0.85f, 0.6f), tw);
    }

    static void BuildOutposts()
    {
        var posts = new GameObject("Outposts").transform;
        posts.SetParent(env);

        BuildTower(new Vector3(-40, 0, 25), posts);   // Tower A: watches the west/vent approach
        BuildTower(new Vector3(30, 0, -45), posts);   // Tower B: overlooks the front gate

        // Sandbag MG post (south-west yard)
        var sb = new GameObject("SandbagPost").transform;
        sb.SetParent(posts);
        Vector3 c = new Vector3(-28, 0, -35);
        for (int i = -3; i <= 3; i++)
        {
            float a = (180f + i * 22f) * Mathf.Deg2Rad;
            var p = c + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 3f;
            Box("Sandbags", p + Vector3.up * 0.5f, new Vector3(1.4f, 1f, 0.7f), mSandbag, new Vector3(0, i * 22f, 0), sb);
        }
        Box("MG", c + new Vector3(0, 1.25f, -2.6f), new Vector3(0.12f, 0.15f, 1.3f), mGunMetal, default, sb);
        Box("Ammo", c + new Vector3(1.5f, 0.3f, 0.5f), new Vector3(0.6f, 0.6f, 0.9f), mPlayerArms, default, sb);

        // Gate checkpoint
        var gp = new GameObject("GatePost").transform;
        gp.SetParent(posts);
        Box("Booth", new Vector3(9, 1.4f, -50), new Vector3(3, 2.8f, 3), mConcrete, default, gp);
        Box("BoothRoof", new Vector3(9, 2.9f, -50), new Vector3(3.6f, 0.2f, 3.6f), mRust, default, gp);
        Box("Barrier", new Vector3(5.5f, 1.1f, -53), new Vector3(0.3f, 1f, 0.3f), mConcrete, default, gp);
        Box("BarrierArm", new Vector3(3f, 1.5f, -51f), new Vector3(0.15f, 0.15f, 5f), mWhite, new Vector3(-70, 90, 0), gp);
        for (int i = 0; i < 3; i++) Box("JerseyBarrier", new Vector3(-10 + i * 3.5f, 0.45f, -48), new Vector3(3f, 0.9f, 0.7f), mConcrete, default, gp);
    }

    static void BuildDecor()
    {
        var deco = new GameObject("Decor").transform;
        deco.SetParent(env);

        MeshProp(PrimitiveType.Cylinder, "CoolingTower", new Vector3(-45, 11, 0), new Vector3(14, 11, 14), mConcrete, default, deco);
        MeshProp(PrimitiveType.Cylinder, "CoolingTower", new Vector3(44, 11, 26), new Vector3(14, 11, 14), mConcrete, default, deco);
        MeshProp(PrimitiveType.Cylinder, "Smokestack", new Vector3(34, 16, -14), new Vector3(3, 16, 3), mConcreteDark, default, deco);
        Box("StackBand", new Vector3(34, 28, -14), new Vector3(3.2f, 1f, 3.2f), mRust, default, deco);

        // Pipes along the building
        MeshProp(PrimitiveType.Cylinder, "Pipe", new Vector3(0, 1.0f, 17.5f), new Vector3(0.8f, 18f, 0.8f), mRust, new Vector3(0, 0, 90), deco);
        MeshProp(PrimitiveType.Cylinder, "Pipe", new Vector3(-32, 0.8f, 0), new Vector3(0.6f, 10f, 0.6f), mRust, new Vector3(0, 0, 90), deco);

        // Yard crates / containers for cover
        var rnd = new System.Random(3);
        Vector3[] crates =
        {
            new(-14, 0, -24), new(-12.6f, 0, -24.4f), new(12, 0, -22), new(-35, 0, -10), new(26, 0, 8), new(27, 0, -4),
            new(-26, 0, 18), new(18, 0, 24), new(-6, 0, -32), new(40, 0, -25), new(-50, 0, 30), new(50, 0, 40),
        };
        foreach (var p in crates)
        {
            float s = 1.2f;
            Box("Crate", p + Vector3.up * s * 0.5f, Vector3.one * s, mWood, new Vector3(0, rnd.Next(0, 90), 0), deco);
        }
        Box("Container", new Vector3(-30, 1.3f, 40), new Vector3(2.6f, 2.6f, 12f), mRust, new Vector3(0, 90, 0), deco);
        Box("Container", new Vector3(28, 1.3f, -28), new Vector3(2.6f, 2.6f, 12f), mPlayerArms, new Vector3(0, 20, 0), deco);
        Box("Container", new Vector3(-38, 1.3f, -42), new Vector3(2.6f, 2.6f, 12f), mMetal, default, deco);
        Box("Generator", new Vector3(24, 1f, 12), new Vector3(2f, 2f, 3.5f), mConsole, default, deco);
    }

    // =====================================================================================
    // Main plant

    static void BuildPlant()
    {
        var plant = new GameObject("MainPlant").transform;
        plant.SetParent(env);
        const float X0 = -20.15f, X1 = 20.15f, Z0 = -15.15f, Z1 = 15.15f;

        // Stairwell (east): lane A x[16.7,19.85], lane B x[13.5,16.5], z[-12,-4]. Divider at x 16.6.
        Vector4 laneAHole = new Vector4(16.7f, -12f, X1 - OuterT, -4f);
        Vector4 laneBHole = new Vector4(13.5f, -12f, 16.5f, -4f);

        // Slabs
        SlabWithHole("Floor_F1", X0, X1, Z0, Z1, FloorY[0], mConcreteDark, plant);
        SlabWithHole("Floor_F2", X0, X1, Z0, Z1, FloorY[1], mConcrete, plant, laneAHole);
        SlabWithHole("Floor_F3", X0, X1, Z0, Z1, FloorY[2], mConcrete, plant, laneBHole);
        SlabWithHole("Roof", X0, X1, Z0, Z1, FloorY[3], mConcrete, plant, laneAHole);

        // Interior stairs
        const float laneAx = 18.27f, laneBx = 15f, rampW = 2.9f;
        Ramp("Stairs_F1_F2", new Vector3(laneAx, FloorY[0], -12), new Vector3(laneAx, FloorY[1], -4), rampW, mMetal, plant);
        Ramp("Stairs_F2_F3", new Vector3(laneBx, FloorY[1], -4), new Vector3(laneBx, FloorY[2], -12), rampW, mMetal, plant);
        Ramp("Stairs_F3_Roof", new Vector3(laneAx, FloorY[2], -12), new Vector3(laneAx, FloorY[3], -4), rampW, mMetal, plant);
        Box("StairDivider", new Vector3(16.6f, FloorY[3] * 0.5f, -8f), new Vector3(0.2f, FloorY[3], 8f), mConcreteDark, default, plant);
        // Railings around stair holes
        Box("Rail", new Vector3(18.27f, FloorY[1] + 0.5f, -12.05f), new Vector3(3.15f, 1f, 0.1f), mMetal, default, plant);
        Box("Rail", new Vector3(13.4f, FloorY[2] + 0.5f, -8f), new Vector3(0.1f, 1f, 8f), mMetal, default, plant);
        Box("Rail", new Vector3(15f, FloorY[2] + 0.5f, -3.9f), new Vector3(3.1f, 1f, 0.1f), mMetal, default, plant);
        Box("Rail", new Vector3(18.27f, FloorY[3] + 0.5f, -12.05f), new Vector3(3.15f, 1f, 0.1f), mMetal, default, plant);
        Box("StairHut", new Vector3(18.27f, FloorY[3] + 1.5f, -12.2f), new Vector3(3.3f, 3f, 0.2f), mConcreteDark, default, plant);

        // Outer walls per floor
        for (int f = 0; f < 3; f++)
        {
            float yb = f == 0 ? 0f : FloorY[f];
            float top = FloorY[f + 1] - Slab;
            float h = top - yb;
            float off = FloorY[f] - yb; // openings are given relative to the floor surface
            Vector4 D(float a, float b, float t = 2.6f) => new Vector4(a, b, off, off + t);
            Vector4 W(float a, float b) => new Vector4(a, b, off + 1.0f, off + 2.2f);
            Vector4 Vent(float a, float b) => new Vector4(a, b, 0f, off + 1.25f);

            var south = f == 0 ? new[] { D(-2, 2, 2.9f), W(-14, -11), W(9, 12) } : new[] { W(-15, -12), W(-6, -3), W(5, 8) };
            var north = f == 0 ? new[] { W(-10, -7), W(6, 9) } : new[] { W(-15, -12), W(3, 6), W(10, 13) };
            var east = f == 0 ? new[] { D(3, 5), W(8, 11) } : new[] { W(6, 9) };
            var west = f == 0 ? new[] { Vent(-6, -5), W(8, 10) }
                     : f == 1 ? new[] { D(11.5f, 13.5f), W(-8, -5) }
                     : new[] { D(0.5f, 2.5f), W(-8, -5) };

            Wall($"Wall_S_F{f + 1}", true, -15f, X0, X1, yb, h, OuterT, mConcrete, plant, south);
            Wall($"Wall_N_F{f + 1}", true, 15f, X0, X1, yb, h, OuterT, mConcrete, plant, north);
            Wall($"Wall_E_F{f + 1}", false, 20f, -14.85f, 14.85f, yb, h, OuterT, mConcrete, plant, east);
            Wall($"Wall_W_F{f + 1}", false, -20f, -14.85f, 14.85f, yb, h, OuterT, mConcrete, plant, west);
            // Band where the slab meets the wall
            if (f > 0) Box("Band", new Vector3(0, FloorY[f] - Slab * 0.5f, -15.2f), new Vector3(40.6f, Slab + 0.1f, 0.15f), mConcreteDark, default, plant);
        }

        // Vent duct outside the west wall (crouch only)
        Box("VentSide", new Vector3(-21.4f, 0.67f, -6.05f), new Vector3(2.5f, 1.35f, 0.1f), mMetal, default, plant);
        Box("VentSide", new Vector3(-21.4f, 0.67f, -4.95f), new Vector3(2.5f, 1.35f, 0.1f), mMetal, default, plant);
        Box("VentTop", new Vector3(-21.4f, 1.4f, -5.5f), new Vector3(2.5f, 0.1f, 1.2f), mMetal, default, plant);

        // Back service door frame + front entrance canopy
        Box("Canopy", new Vector3(0, 3.2f, -17f), new Vector3(7f, 0.2f, 3.6f), mRust, default, plant);
        Box("DoorFrame", new Vector3(20.25f, 2.75f, 4f), new Vector3(0.2f, 0.25f, 2.6f), mRust, default, plant);

        // ---------------- F1 turbine hall
        float y = FloorY[0];
        var f1 = new GameObject("F1_TurbineHall").transform; f1.SetParent(plant);
        MeshProp(PrimitiveType.Cylinder, "Turbine", new Vector3(-9, y + 1.6f, 4), new Vector3(3.2f, 5f, 3.2f), mMetal, new Vector3(0, 0, 90), f1);
        MeshProp(PrimitiveType.Cylinder, "Turbine", new Vector3(6, y + 1.6f, 4), new Vector3(3.2f, 5f, 3.2f), mMetal, new Vector3(0, 0, 90), f1);
        Box("TurbineBase", new Vector3(-9, y + 0.3f, 4), new Vector3(10.5f, 0.6f, 2.6f), mConcreteDark, default, f1);
        Box("TurbineBase", new Vector3(6, y + 0.3f, 4), new Vector3(10.5f, 0.6f, 2.6f), mConcreteDark, default, f1);
        Box("Generator", new Vector3(-1.5f, y + 1.2f, 4), new Vector3(2.4f, 2.4f, 3f), mConsole, default, f1);
        foreach (var p in new[] { new Vector3(-15, 0, -10), new Vector3(-4, 0, -9), new Vector3(3, 0, -6), new Vector3(-12, 0, 9), new Vector3(10, 0, 11), new Vector3(-17, 0, -2) })
            Box("Crate", p + Vector3.up * (y + 0.6f), new Vector3(1.2f, 1.2f, 1.2f), mWood, default, f1);
        Box("ControlDesk", new Vector3(8, y + 0.5f, -12.5f), new Vector3(4f, 1f, 1f), mConsole, default, f1);

        // ---------------- F2 control rooms
        y = FloorY[1];
        float h2 = FloorY[2] - Slab - y;
        var f2 = new GameObject("F2_ControlRooms").transform; f2.SetParent(plant);
        Wall("Wall_F2", true, 0f, -19.85f, 13.5f, y, h2, InnerT, mConcreteDark, f2, Door(-14, -12), Door(-2, 0));
        Wall("Wall_F2", false, 0f, 0.1f, 14.85f, y, h2, InnerT, mConcreteDark, f2, Door(7, 9));
        foreach (var x in new[] { -16f, -11f, -6f }) Box("Console", new Vector3(x, y + 0.55f, 14.2f), new Vector3(3.5f, 1.1f, 0.9f), mConsole, default, f2);
        Box("Console", new Vector3(-6, y + 0.55f, 6.5f), new Vector3(0.9f, 1.1f, 4f), mConsole, default, f2);
        foreach (var x in new[] { 3f, 6f, 9f }) Box("ServerRack", new Vector3(x, y + 1.1f, 12f), new Vector3(1.2f, 2.2f, 0.8f), mGunMetal, default, f2);
        Box("Desk", new Vector3(-10, y + 0.4f, -8), new Vector3(2.5f, 0.8f, 1.2f), mWood, default, f2);
        Box("Desk", new Vector3(4, y + 0.4f, -9), new Vector3(2.5f, 0.8f, 1.2f), mWood, default, f2);
        Box("Lockers", new Vector3(-19.4f, y + 1f, -10), new Vector3(0.6f, 2f, 4f), mMetal, default, f2);

        // ---------------- F3 offices
        y = FloorY[2];
        float h3 = FloorY[3] - Slab - y;
        var f3 = new GameObject("F3_Offices").transform; f3.SetParent(plant);
        Wall("Wall_F3", true, 3f, -19.85f, 13.5f, y, h3, InnerT, mConcreteDark, f3, Door(-12, -10), Door(4, 6));
        Wall("Wall_F3", false, -6f, 3.1f, 14.85f, y, h3, InnerT, mConcreteDark, f3, Door(8, 10));
        Wall("Wall_F3", false, 6f, 3.1f, 14.85f, y, h3, InnerT, mConcreteDark, f3, Door(8, 10));
        Wall("Wall_F3", true, 3f, 13.5f, 19.85f, y, h3, InnerT, mConcreteDark, f3, Door(16, 18));
        Box("Desk", new Vector3(-14, y + 0.4f, 10), new Vector3(2.5f, 0.8f, 1.2f), mWood, default, f3);
        Box("Desk", new Vector3(0, y + 0.4f, 12), new Vector3(2.5f, 0.8f, 1.2f), mWood, default, f3);
        Box("Table", new Vector3(13, y + 0.4f, 7), new Vector3(1.6f, 0.8f, 1f), mMetal, default, f3);
        Box("Chair", new Vector3(14.5f, y + 0.45f, 7), new Vector3(0.5f, 0.9f, 0.5f), mMetal, default, f3);
        Box("FilingCabinets", new Vector3(-19.4f, y + 0.8f, -9), new Vector3(0.6f, 1.6f, 3f), mMetal, default, f3);
        Box("Desk", new Vector3(-6, y + 0.4f, -8), new Vector3(2.5f, 0.8f, 1.2f), mWood, default, f3);

        // Interior lights
        var lights = new GameObject("InteriorLights").transform; lights.SetParent(plant);
        for (int f = 0; f < 3; f++)
            foreach (var p in new[] { new Vector3(-10, 0, -7), new Vector3(8, 0, -7), new Vector3(-10, 0, 8), new Vector3(10, 0, 8) })
                PointLight(p + Vector3.up * (FloorY[f] + 3.1f), 14f, 2.5f, new Color(1f, 0.88f, 0.7f), lights);
        PointLight(new Vector3(17, FloorY[1] + 2.5f, -13.5f), 9f, 2f, new Color(1f, 0.88f, 0.7f), lights);
        PointLight(new Vector3(17, FloorY[2] + 2.5f, -13.5f), 9f, 2f, new Color(1f, 0.88f, 0.7f), lights);

        // ---------------- Exterior fire escape (west side)
        var fe = new GameObject("FireEscape").transform; fe.SetParent(plant);
        const float laneC = -21.375f, laneD = -23.8f, feW = 2.3f;
        Ramp("FE_Ground_F2", new Vector3(laneD, 0f, 3), new Vector3(laneD, FloorY[1], 11), feW, mRust, fe);
        BoxMinMax("FE_Landing_F2", new Vector3(-25f, FloorY[1] - 0.25f, 11), new Vector3(-20.15f, FloorY[1], 14), mRust, fe);
        Ramp("FE_F2_F3", new Vector3(laneC, FloorY[1], 11), new Vector3(laneC, FloorY[2], 3), feW, mRust, fe);
        BoxMinMax("FE_Landing_F3", new Vector3(-25f, FloorY[2] - 0.25f, 0), new Vector3(-20.15f, FloorY[2], 3), mRust, fe);
        Ramp("FE_F3_Roof", new Vector3(laneD, FloorY[2], 3), new Vector3(laneD, FloorY[3], 11), feW, mRust, fe);
        BoxMinMax("FE_Landing_Roof", new Vector3(-25f, FloorY[3] - 0.25f, 11), new Vector3(-20.15f, FloorY[3], 14), mRust, fe);
        foreach (var (z, top) in new[] { (11.1f, FloorY[3]), (13.9f, FloorY[3]), (0.1f, FloorY[2]), (2.9f, FloorY[2]) })
            Box("FE_Post", new Vector3(-24.9f, top * 0.5f, z), new Vector3(0.15f, top, 0.15f), mRust, default, fe);
    }

    static void BuildRoof(out Transform helipad, out ExtractionZone zone)
    {
        var roof = new GameObject("RoofProps").transform;
        roof.SetParent(env);
        float y = FloorY[3];

        // Parapet with a gap where the fire escape arrives.
        const float ph = 1.0f, pt = 0.3f;
        Wall("Parapet", true, -15f, -20.15f, 20.15f, y, ph, pt, mConcrete, roof);
        Wall("Parapet", true, 15f, -20.15f, 20.15f, y, ph, pt, mConcrete, roof);
        Wall("Parapet", false, 20f, -14.85f, 14.85f, y, ph, pt, mConcrete, roof);
        Wall("Parapet", false, -20f, -14.85f, 14.85f, y, ph, pt, mConcrete, roof, new Vector4(11, 14, 0, ph));

        // Helipad
        Vector3 padCenter = new Vector3(0, y, 3);
        MeshProp(PrimitiveType.Cylinder, "Helipad", padCenter + Vector3.up * 0.03f, new Vector3(10, 0.03f, 10), mHelipad, default, roof);
        Box("H", padCenter + new Vector3(-1.2f, 0.07f, 0), new Vector3(0.5f, 0.02f, 4f), mWhite, default, roof);
        Box("H", padCenter + new Vector3(1.2f, 0.07f, 0), new Vector3(0.5f, 0.02f, 4f), mWhite, default, roof);
        Box("H", padCenter + new Vector3(0, 0.07f, 0), new Vector3(2.4f, 0.02f, 0.5f), mWhite, default, roof);
        helipad = new GameObject("LandingPoint").transform;
        helipad.SetParent(roof);
        helipad.position = padCenter;

        // Extraction zone (14 x 14) + green border markers
        var zoneGo = new GameObject("ExtractionZone");
        zoneGo.transform.SetParent(roof);
        zoneGo.transform.position = padCenter + Vector3.up * 1.5f;
        var box = zoneGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(14, 3.2f, 14);
        zone = zoneGo.AddComponent<ExtractionZone>();
        foreach (var (p, s) in new[]
        {
            (new Vector3(0, 0, 7), new Vector3(14, 0.02f, 0.2f)), (new Vector3(0, 0, -7), new Vector3(14, 0.02f, 0.2f)),
            (new Vector3(7, 0, 0), new Vector3(0.2f, 0.02f, 14)), (new Vector3(-7, 0, 0), new Vector3(0.2f, 0.02f, 14)),
        })
        {
            var m = Prim(PrimitiveType.Cube, "ZoneMarker", roof, padCenter + p + Vector3.up * 0.06f, s, mZone, collider: false);
            m.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // Radio on a crate at the zone edge
        var radio = new GameObject("Radio");
        radio.transform.SetParent(roof);
        radio.transform.position = padCenter + new Vector3(5.5f, 0, 5.5f);
        radio.layer = LayerMask.NameToLayer("Interactable");
        var rc = radio.AddComponent<BoxCollider>();
        rc.center = new Vector3(0, 0.75f, 0);
        rc.size = new Vector3(1.1f, 1.5f, 1.1f);
        radio.AddComponent<Radio>();
        var crate = Prim(PrimitiveType.Cube, "Crate", radio.transform, new Vector3(0, 0.45f, 0), new Vector3(1f, 0.9f, 1f), mWood, collider: false);
        var set = Prim(PrimitiveType.Cube, "RadioSet", radio.transform, new Vector3(0, 1.08f, 0), new Vector3(0.6f, 0.36f, 0.35f), mPlayerArms, collider: false);
        Prim(PrimitiveType.Cylinder, "Antenna", radio.transform, new Vector3(0.22f, 1.7f, 0), new Vector3(0.02f, 0.5f, 0.02f), mGunMetal, collider: false);
        Prim(PrimitiveType.Cube, "Light", radio.transform, new Vector3(-0.15f, 1.27f, -0.18f), new Vector3(0.06f, 0.04f, 0.02f), mZone, collider: false);
        crate.layer = set.layer = radio.layer;

        // Roof cover: AC units, crates
        Box("ACUnit", new Vector3(-12, y + 0.8f, -8), new Vector3(3f, 1.6f, 2f), mMetal, default, roof);
        Box("ACUnit", new Vector3(12, y + 0.8f, -9), new Vector3(3f, 1.6f, 2f), mMetal, default, roof);
        Box("ACUnit", new Vector3(-14, y + 0.8f, 11), new Vector3(2f, 1.6f, 3f), mMetal, default, roof);
        Box("Crate", new Vector3(10, y + 0.6f, 12), Vector3.one * 1.2f, mWood, default, roof);
        Box("Crate", new Vector3(-8, y + 0.6f, -12.5f), Vector3.one * 1.2f, mWood, default, roof);
        Box("Antenna", new Vector3(-17, y + 4f, -12), new Vector3(0.2f, 8f, 0.2f), mMetal, default, roof);
    }

    // =====================================================================================
    // Guards

    static void BuildGuards(GameObject prefab, Transform parent, Transform routes)
    {
        PatrolRoute Route(string name, params Vector3[] pts)
        {
            var r = new GameObject("Route_" + name).AddComponent<PatrolRoute>();
            r.transform.SetParent(routes);
            for (int i = 0; i < pts.Length; i++)
            {
                var wp = new GameObject("WP" + i).transform;
                wp.SetParent(r.transform);
                wp.position = pts[i];
            }
            return r;
        }
        Guard G(string name, Vector3 pos, float yaw, PatrolRoute route = null, bool interior = false, bool stationary = false, float scan = 0f, float range = 25f)
        {
            var go = Place(prefab, parent, pos, yaw);
            go.name = "Guard_" + name;
            var g = go.GetComponent<Guard>();
            g.route = route;
            g.interior = interior;
            g.stationary = stationary;
            g.scanAngle = scan;
            go.GetComponent<GuardVision>().range = range;
            return g;
        }

        // ---- Exterior (7)
        G("TowerA", new Vector3(-40, 6f, 25), 146, stationary: true, scan: 55, range: 40);
        G("TowerB", new Vector3(30, 6f, -45), 252, stationary: true, scan: 50, range: 40);
        G("Sandbag1", new Vector3(-28, 0, -35), 200);
        G("Sandbag2", new Vector3(-25.5f, 0, -36), 150);
        G("Gate", new Vector3(5.5f, 0, -51.5f), 180);
        G("BackDoor", new Vector3(23.5f, 0, 4), 60);
        G("YardPatrol", new Vector3(-32, 0, -22), 90, Route("Yard",
            new Vector3(-32, 0, -22), new Vector3(32, 0, -22), new Vector3(32, 0, 20), new Vector3(-32, 0, 20)));

        // ---- Interior (10)
        float f1 = FloorY[0], f2 = FloorY[1], f3 = FloorY[2], rf = FloorY[3];
        G("F1_Door", new Vector3(2.5f, f1, -12), 180, interior: true);
        G("F1_Vent", new Vector3(-12, f1, -3), 20, interior: true);
        G("F1_Patrol", new Vector3(-15, f1, -9), 90, Route("F1",
            new Vector3(-15, f1, -9), new Vector3(10, f1, -9), new Vector3(12, f1, 9), new Vector3(-15, f1, 9)), interior: true);
        G("F2_Hostages", new Vector3(-9, f2, 5), 270, interior: true);
        G("F2_Console", new Vector3(-4, f2, 12), 200, interior: true);
        G("F2_Patrol", new Vector3(-15, f2, -8), 90, Route("F2",
            new Vector3(-15, f2, -8), new Vector3(10, f2, -8), new Vector3(8, f2, 8)), interior: true);
        G("F3_Interrogation", new Vector3(10, f3, 6), 300, interior: true);
        G("F3_Patrol", new Vector3(-15, f3, -6), 90, Route("F3",
            new Vector3(-15, f3, -6), new Vector3(10, f3, -6), new Vector3(0, f3, 9)), interior: true);
        G("Roof_South", new Vector3(-8, rf, -10), 180, interior: true);
        G("Roof_North", new Vector3(10, rf, 10), 30, interior: true);
    }
}
