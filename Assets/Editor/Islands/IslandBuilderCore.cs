using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ada üreticilerinin ortak çekirdeği: ızgara, organik kıyı, yamaçlar, kumsal,
/// köprü / ray döşeme, toplanabilirler ve yerleştirme yardımcıları.
/// Ada tarifleri: FarmIsland.cs, TradeIsland.cs
/// </summary>
public static partial class IslandBuilder
{
    const int DefaultSeed = 2611;

    // ---------- Asset paths ----------
    const string K = "Assets/Models/KUBIKOS - World/Prefabs/";
    const string CubeDir = K + "CubesAtlas/";
    const string PlantDir = K + "TreesAndPlants/";
    const string ItemDir = K + "Items/";
    const string RockDir = K + "Rocks/Blue/";
    const string MedDir = "Assets/Models/Low Poly Medieval Environment Pack (35+ Props)/Prefabs/";
    const string RpgDir = "Assets/Models/RPGPP_LT/Prefabs/";
    const string BldDir = "Assets/Models/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/models/buildings/";
    const string TTPrefabDir = "Assets/Models/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/prefabs/";

    const float FallbackWaterY = -3.2f;
    const float BridgeY = 0.85f;  // mevcut Bridge objeleriyle aynı pivot yüksekliği
    // Sahnedeki köprü Z boyunca yaw = +90 ile döşeli -> X boyunca giden köprü yaw = 0.
    const float BridgeYawAlongX = 0f, BridgeYawAlongZ = 90f;

    enum Use { Free, Path, Plaza, Field, FieldArea, Building, Station, Rail }

    // Stil: ilk adalarda (Camping_Area / Central_Island) kullanılan bitki ve ağaçlar
    static readonly string[] GroundCover = { "Grass3", "Grass4", "Grass5", "Grass6", "Grass7", "Plant3_4", "Plant1_2", "Plant2_2", "Plant2_3", "Plant4_2", "Plant4_3", "LeafGroup2" };
    static readonly string[] Flowers = { "Flower1", "Flower1_3", "Flower2", "Flower2_5", "Flower2Group1", "Flower3_6", "Flower4", "Flower4_2", "Flower4Group1" };
    static readonly string[] Trees = { "Tree_2", "Tree_2", "Tree_4", "Tree_4", "Tree_1", "Tree_3" };
    static readonly string[] MedievalTrees = { "Tree_03", "Tree_03.1" };

    // Toplanabilirler (sahnedeki Woods / Rocks / MushRooms gruplarıyla aynı prefab'ler)
    static readonly string[] WoodPrefabs = { MedDir + "Firewood_02", MedDir + "Firewood_02", MedDir + "Firewood_02", MedDir + "Firewood_04" };
    static readonly string[] StoneSmall = { "RockSmallGroupBlue3", "RockSmallGroupBlue4", "RockSmallGroupBlue8", "RockSmallGroupBlue8", "RockSmallGroupBlue9", "RockSmallGroupBlue10", "RockSmallGroupBlue11" };
    static readonly string[] StoneBig = { "RockLayered_1 (1)", "RockLayered_2 (1)", "RockLayered_3 (1)", "RockLayered_5 (1)", "Rock_1 (1)" };
    static readonly string[] Mushrooms = { "MushroomRed", "MushroomRedGroup1", "MushroomRedGroup2", "MushroomYellow", "MushroomYellowGroup1", "MushroomYellowGroup2" };

    static readonly string[] ShoreRocks = { "RockSmallGroupBlue1", "RockSmallGroupBlue3", "RockSmallGroupBlue4", "RockFlat_1 (1)", "RockFlat_2 (1)", "RockFlat_3 (1)" };
    static readonly string[] WaterRocks = { "RockLayered_1 (1)", "RockLayered_2 (1)", "RockLayered_3 (1)", "RockGround_1 (1)", "RockGround_2 (1)" };

    // ---------- Per-island state ----------
    static System.Random rng;
    static float ox, oz;
    static float cellH = 1f, topOff = 1f;
    static int bottomLayer = -4;
    static float waterY;
    static int MinX, MaxX, MinZ, MaxZ;
    static float RadiusX, RadiusZ;
    static bool stonePaths;
    static int GW => MaxX - MinX + 1;
    static int GH => MaxZ - MinZ + 1;

    static bool[,] land, shore, occupied, forced;
    static int[,] top, coastDist;
    static Use[,] use;
    static readonly HashSet<Vector2Int> keepClear = new HashSet<Vector2Int>();

    static readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();
    static readonly Dictionary<string, Vector3> sizeCache = new Dictionary<string, Vector3>();
    static readonly HashSet<string> missing = new HashSet<string>();
    static int spawned;
    static readonly int[] collectibleCount = new int[5];

    // =====================================================================
    // Build lifecycle
    // =====================================================================
    static bool CanBuild()
    {
        if (!EditorApplication.isPlaying) return true;
        Debug.LogError("[Islands] Play modundayken çalıştırılamaz.");
        return false;
    }

    /// <summary>Var olan kökleri (onayla) siler. false = kullanıcı iptal etti.</summary>
    static bool ConfirmReplace(params string[] names)
    {
        Scene scene = SceneManager.GetActiveScene();
        var found = new List<GameObject>();
        foreach (var n in names)
        {
            var go = FindRoot(scene, n);
            if (go) found.Add(go);
        }
        if (found.Count == 0) return true;
        string list = string.Join(", ", found.ConvertAll(g => g.name));
        if (!EditorUtility.DisplayDialog("Islands",
                $"Sahnede zaten var: {list}. Silinip yeniden kurulsun mu? (Undo ile geri alınabilir)",
                "Yeniden Kur", "İptal"))
            return false;
        foreach (var go in found) Undo.DestroyObjectImmediate(go);
        return true;
    }

    static void BeginIsland(int seed, int minX, int maxX, int minZ, int maxZ, float rx, float rz, bool stone)
    {
        rng = new System.Random(seed);
        ox = (float)rng.NextDouble() * 1000f;
        oz = (float)rng.NextDouble() * 1000f;
        spawned = 0;
        System.Array.Clear(collectibleCount, 0, collectibleCount.Length);
        MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ;
        RadiusX = rx; RadiusZ = rz;
        stonePaths = stone;

        land = new bool[GW, GH];
        shore = new bool[GW, GH];
        occupied = new bool[GW, GH];
        forced = new bool[GW, GH];
        top = new int[GW, GH];
        coastDist = new int[GW, GH];
        use = new Use[GW, GH];
        keepClear.Clear();

        MeasureCube();
        DetectWater();
        MeasureRail();
    }

    static void EndIsland(GameObject root, string label)
    {
        Undo.RegisterCreatedObjectUndo(root, "Build " + label);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[Islands] {label} kuruldu. obje={spawned}, toplanabilir: odun={collectibleCount[(int)CollectibleType.Wood]}, " +
                  $"taş={collectibleCount[(int)CollectibleType.Stone]}, mantar={collectibleCount[(int)CollectibleType.Food]}");
    }

    static void ReportMissing()
    {
        if (missing.Count > 0)
            Debug.LogWarning("[Islands] Bulunamayan prefab'ler (atlandı):\n" + string.Join("\n", missing));
        missing.Clear();
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }

    static GameObject FindRoot(string name) => FindRoot(SceneManager.GetActiveScene(), name);

    // =====================================================================
    // Measurement
    // =====================================================================
    static void MeasureCube()
    {
        Vector3 s = MeasureSize(CubeDir + "Cube_Soil.prefab", out Bounds b);
        cellH = s.y > 0.01f ? s.y : 1f;
        topOff = b.max.y; // pivot (y = 0) yüzeyinden üst yüzeye
    }

    static void DetectWater()
    {
        var ys = new List<float>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            string n = r.gameObject.name;
            if (n.StartsWith("Water (") || n == "Water")
                ys.Add(r.bounds.center.y);
        }
        waterY = FallbackWaterY;
        if (ys.Count > 0)
        {
            ys.Sort();
            float median = ys[ys.Count / 2];
            if (median < -0.5f && median > -20f) waterY = median;
        }
        bottomLayer = Mathf.CeilToInt((waterY - topOff) / cellH);
        if (bottomLayer > -1) bottomLayer = -1;
    }

    static float railHeight, railLen = 1f;
    static float railYawAlongX, railYawAlongZ;

    static void MeasureRail()
    {
        // Ray parçasının uzun ekseni traversler (enine); hat yönü kısa eksen.
        Vector3 rs = MeasureSize(ItemDir + "Railway.prefab");
        bool longX = rs.x >= rs.z;
        railYawAlongX = longX ? 90f : 0f;
        railYawAlongZ = longX ? 0f : 90f;
        railLen = Mathf.Min(rs.x, rs.z);
        if (railLen < 0.3f) railLen = 1f;
        if (Mathf.Abs(railLen - Mathf.Round(railLen)) < 0.15f) railLen = Mathf.Round(railLen);
        railHeight = rs.y;
    }

    // =====================================================================
    // Grid helpers
    // =====================================================================
    static bool In(int i, int j) => i >= MinX && i <= MaxX && j >= MinZ && j <= MaxZ;
    static bool IsLand(int i, int j) => In(i, j) && land[i - MinX, j - MinZ];
    static Use GetUse(int i, int j) => In(i, j) ? use[i - MinX, j - MinZ] : Use.Free;
    static void SetUse(int i, int j, Use u) { if (In(i, j)) use[i - MinX, j - MinZ] = u; }
    static int Top(int i, int j) => top[i - MinX, j - MinZ];
    static bool IsShore(int i, int j) => In(i, j) && shore[i - MinX, j - MinZ];
    static bool IsOccupied(int i, int j) => !In(i, j) || occupied[i - MinX, j - MinZ];
    static void Occupy(int i, int j) { if (In(i, j)) occupied[i - MinX, j - MinZ] = true; }
    static int Coast(int i, int j) => In(i, j) ? coastDist[i - MinX, j - MinZ] : 0;

    static float Surface(int layer) => layer * cellH + topOff;
    static Vector3 CellPos(int i, int j, float y) => new Vector3(i + 0.5f, y, j + 0.5f);

    static float Noise(float x, float z, float freq, float seedShift = 0f) =>
        Mathf.PerlinNoise(x * freq + ox + seedShift, z * freq + oz + seedShift * 0.37f);

    static int Hash(int i, int j, int k = 0)
    {
        unchecked
        {
            int h = i * 73856093 ^ j * 19349663 ^ k * 83492791 ^ (int)(ox * 1000f);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h & 0x7fffffff;
        }
    }

    static void MarkRect(RectInt r, Use u)
    {
        for (int i = r.xMin; i < r.xMax; i++)
            for (int j = r.yMin; j < r.yMax; j++)
                SetUse(i, j, u);
    }

    static bool NearUse(int i, int j, Use u)
    {
        for (int di = -1; di <= 1; di++)
            for (int dj = -1; dj <= 1; dj++)
                if (GetUse(i + di, j + dj) == u) return true;
        return false;
    }

    static readonly Vector2Int[] Dir4 = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    // =====================================================================
    // Shape
    // =====================================================================
    /// <summary>Ayrılmış alanlar işaretlendikten sonra çağrılır: organik kıyı + zorunlu kara.</summary>
    static void GenerateShape(Vector2Int seedCell)
    {
        for (int i = MinX; i <= MaxX; i++)
            for (int j = MinZ; j <= MaxZ; j++)
                if (GetUse(i, j) != Use.Free)
                    for (int di = -1; di <= 1; di++)
                        for (int dj = -1; dj <= 1; dj++)
                            if (In(i + di, j + dj)) forced[i + di - MinX, j + dj - MinZ] = true;

        for (int i = MinX; i <= MaxX; i++)
            for (int j = MinZ; j <= MaxZ; j++)
            {
                float x = i + 0.5f, z = j + 0.5f;
                float e = 1f - (x * x) / (RadiusX * RadiusX) - (z * z) / (RadiusZ * RadiusZ);
                float n = (Noise(x, z, 0.13f) - 0.5f) * 0.7f + (Noise(x, z, 0.37f, 11f) - 0.5f) * 0.25f;
                bool border = i <= MinX + 1 || i >= MaxX - 1 || j <= MinZ + 1 || j >= MaxZ - 1;
                land[i - MinX, j - MinZ] = !border && e + n > 0f;
            }
        Smooth(); Smooth();
        for (int i = MinX; i <= MaxX; i++)
            for (int j = MinZ; j <= MaxZ; j++)
                if (forced[i - MinX, j - MinZ]) land[i - MinX, j - MinZ] = true;
        KeepConnected(seedCell.x, seedCell.y);
        FillHoles();
    }

    /// <summary>
    /// Başlangıç hücresinden verilen yönde adanın o hattaki en uç kara hücresine kadar yolu
    /// karaya çevirir (köprü ayağı). Uç hücrenin koordinatını (x ya da z) döndürür.
    /// Hattın devamı (su) kıyı kayalarından temiz tutulur.
    /// </summary>
    static int Landing(int si, int sj, int di, int dj, Use u)
    {
        int fi = si, fj = sj, ci = si, cj = sj;
        while (In(ci + di, cj + dj))
        {
            ci += di; cj += dj;
            if (IsLand(ci, cj)) { fi = ci; fj = cj; }
        }

        ci = si; cj = sj;
        while (true)
        {
            land[ci - MinX, cj - MinZ] = true;
            Use cur = GetUse(ci, cj);
            bool overwrite = u == Use.Rail
                ? cur != Use.Building && cur != Use.Field && cur != Use.FieldArea
                : cur == Use.Free;
            if (overwrite) SetUse(ci, cj, u);
            if (ci == fi && cj == fj) break;
            ci += di; cj += dj;
        }

        // hattın su üstündeki devamı + yanları temiz
        ci = fi + di; cj = fj + dj;
        while (In(ci, cj))
        {
            for (int k = -1; k <= 1; k++)
                keepClear.Add(new Vector2Int(ci + (di == 0 ? k : 0), cj + (dj == 0 ? k : 0)));
            ci += di; cj += dj;
        }
        return di != 0 ? fi : fj;
    }

    /// <summary>Kıyı mesafesi, kıyı basamakları ve kumsal.</summary>
    static void FinishLayout()
    {
        ComputeCoastDistance();

        for (int i = MinX; i <= MaxX; i++)
            for (int j = MinZ; j <= MaxZ; j++)
            {
                if (!IsLand(i, j) || GetUse(i, j) != Use.Free || coastDist[i - MinX, j - MinZ] != 1) continue;
                if (NearUse(i, j, Use.Path) || NearUse(i, j, Use.Rail) || NearUse(i, j, Use.Plaza)) continue;
                if (Noise(i, j, 0.33f, 5f) > 0.6f) top[i - MinX, j - MinZ] = -1;
            }

        for (int i = MinX; i <= MaxX; i++)
            for (int j = MinZ; j <= MaxZ; j++)
            {
                if (IsLand(i, j) || keepClear.Contains(new Vector2Int(i, j))) continue;
                bool adj = false;
                for (int di = -1; di <= 1 && !adj; di++)
                    for (int dj = -1; dj <= 1 && !adj; dj++)
                        adj = IsLand(i + di, j + dj);
                if (adj && Noise(i, j, 0.21f, 50f) > 0.40f) shore[i - MinX, j - MinZ] = true;
            }
    }

    static void Smooth()
    {
        var next = (bool[,])land.Clone();
        for (int i = MinX + 1; i < MaxX; i++)
            for (int j = MinZ + 1; j < MaxZ; j++)
            {
                int c = 0;
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                        if ((di != 0 || dj != 0) && IsLand(i + di, j + dj)) c++;
                if (c >= 5) next[i - MinX, j - MinZ] = true;
                else if (c <= 2) next[i - MinX, j - MinZ] = false;
            }
        land = next;
    }

    static void KeepConnected(int si, int sj)
    {
        var seen = new bool[GW, GH];
        var q = new Queue<Vector2Int>();
        land[si - MinX, sj - MinZ] = true;
        q.Enqueue(new Vector2Int(si, sj));
        seen[si - MinX, sj - MinZ] = true;
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var d in Dir4)
            {
                int ni = c.x + d.x, nj = c.y + d.y;
                if (!IsLand(ni, nj) || seen[ni - MinX, nj - MinZ]) continue;
                seen[ni - MinX, nj - MinZ] = true;
                q.Enqueue(new Vector2Int(ni, nj));
            }
        }
        for (int i = 0; i < GW; i++)
            for (int j = 0; j < GH; j++)
                if (land[i, j] && !seen[i, j]) land[i, j] = false;
    }

    static void FillHoles()
    {
        var seen = new bool[GW, GH];
        var q = new Queue<Vector2Int>();
        for (int i = MinX; i <= MaxX; i++)
            for (int j = MinZ; j <= MaxZ; j++)
                if ((i == MinX || i == MaxX || j == MinZ || j == MaxZ) && !IsLand(i, j))
                {
                    seen[i - MinX, j - MinZ] = true;
                    q.Enqueue(new Vector2Int(i, j));
                }
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var d in Dir4)
            {
                int ni = c.x + d.x, nj = c.y + d.y;
                if (!In(ni, nj) || IsLand(ni, nj) || seen[ni - MinX, nj - MinZ]) continue;
                seen[ni - MinX, nj - MinZ] = true;
                q.Enqueue(new Vector2Int(ni, nj));
            }
        }
        for (int i = 0; i < GW; i++)
            for (int j = 0; j < GH; j++)
                if (!land[i, j] && !seen[i, j]) land[i, j] = true;
    }

    static void ComputeCoastDistance()
    {
        var q = new Queue<Vector2Int>();
        for (int i = MinX; i <= MaxX; i++)
            for (int j = MinZ; j <= MaxZ; j++)
            {
                bool l = IsLand(i, j);
                coastDist[i - MinX, j - MinZ] = l ? int.MaxValue : 0;
                if (!l) q.Enqueue(new Vector2Int(i, j));
            }
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            int cd = coastDist[c.x - MinX, c.y - MinZ];
            foreach (var d in Dir4)
            {
                int ni = c.x + d.x, nj = c.y + d.y;
                if (!In(ni, nj)) continue;
                if (coastDist[ni - MinX, nj - MinZ] <= cd + 1) continue;
                coastDist[ni - MinX, nj - MinZ] = cd + 1;
                q.Enqueue(new Vector2Int(ni, nj));
            }
        }
    }

    static int NeighborTop(int i, int j)
    {
        if (IsLand(i, j)) return Top(i, j);
        if (IsShore(i, j)) return bottomLayer;
        return bottomLayer - 1;
    }

    // =====================================================================
    // Terrain
    // =====================================================================
    static void BuildTerrain(Transform root)
    {
        Transform terrain = Group(root, "Terrain");
        Transform surface = Group(terrain, "Surface");
        Transform cliffs = Group(terrain, "Cliffs");
        Transform shoreT = Group(terrain, "Shore");

        for (int i = MinX; i <= MaxX; i++)
            for (int j = MinZ; j <= MaxZ; j++)
            {
                if (IsLand(i, j))
                {
                    int t = Top(i, j);
                    Use u = GetUse(i, j);
                    if (u != Use.Field)
                        SpawnCube(TopCube(i, j, u), surface, i, j, t);

                    for (int L = t - 1; L >= bottomLayer; L--)
                    {
                        bool exposed = false;
                        foreach (var d in Dir4)
                            if (NeighborTop(i + d.x, j + d.y) < L) { exposed = true; break; }
                        if (exposed) SpawnCube(SideCube(i, j, L, t), cliffs, i, j, L);
                    }
                }
                else if (IsShore(i, j))
                {
                    string c = Noise(i, j, 0.4f, 77f) > 0.62f ? "Cube_Rock" : "Cube_Sand";
                    SpawnCube(c, shoreT, i, j, bottomLayer);
                }
            }
    }

    static string TopCube(int i, int j, Use u)
    {
        switch (u)
        {
            case Use.Plaza:
                return "Cube_Rock_2"; // Central_Island'daki taş yollarla aynı
            case Use.Path:
            case Use.Station:
            case Use.Rail:
                if (stonePaths) return "Cube_Rock_2";
                return Noise(i, j, 0.5f, 9f) > 0.55f ? "Cube_Ground" : "Cube_GroundWGrass_6";
            case Use.FieldArea:
                return "Cube_SoilWGrass_1";
            default:
                return GrassCube(i, j);
        }
    }

    // İlk adadaki karışım: çoğunlukla _1, kümeler halinde _4 / _6 / _3
    static string GrassCube(int i, int j)
    {
        float n = Noise(i, j, 0.25f, 33f);
        if (n > 0.66f) return "Cube_SoilWGrass_4";
        if (n < 0.30f) return "Cube_SoilWGrass_6";
        if (Hash(i, j, 1) % 100 < 7) return "Cube_SoilWGrass_3";
        return "Cube_SoilWGrass_1";
    }

    static string SideCube(int i, int j, int L, int t)
    {
        int depth = t - L;
        if (depth <= 2 && L >= -2) return "Cube_Soil"; // ilk adadaki gibi üstte toprak yamaç
        float n = Noise(i * 0.6f + L * 1.7f, j * 0.6f - L * 1.3f, 0.35f, 61f);
        return n > 0.5f ? "Cube_Rock_2" : "Cube_Rock";
    }

    static void SpawnCube(string name, Transform parent, int i, int j, int layer)
    {
        Spawn(CubeDir + name + ".prefab", parent, CellPos(i, j, layer * cellH));
    }

    // =====================================================================
    // Bridges & rails (koordinatlar adanın yerel koordinatları)
    // =====================================================================
    /// <summary>Hücre merkezleri fromCenter..toCenter (dahil) arasına 1 m'lik köprü parçaları.</summary>
    static void BridgeLine(Transform parent, bool alongX, float fixedCenter, float fromCenter, float toCenter)
    {
        if (fromCenter > toCenter) { float t = fromCenter; fromCenter = toCenter; toCenter = t; }
        for (float c = fromCenter; c <= toCenter + 0.01f; c += 1f)
            PlaceCenteredXZ(ItemDir + "Bridge.prefab", parent,
                alongX ? c : fixedCenter, alongX ? fixedCenter : c, BridgeY,
                alongX ? BridgeYawAlongX : BridgeYawAlongZ);
    }

    /// <summary>fromEdge..toEdge kenarları arasına ray parçaları.</summary>
    static void RailLine(Transform parent, bool alongX, float fixedCenter, float fromEdge, float toEdge, float y)
    {
        if (fromEdge > toEdge) { float t = fromEdge; fromEdge = toEdge; toEdge = t; }
        string railPath = ItemDir + "Railway.prefab";
        int n = Mathf.FloorToInt((toEdge - fromEdge) / railLen + 0.001f);
        for (int k = 0; k < n; k++)
        {
            float c = fromEdge + railLen * (k + 0.5f);
            PlaceCentered(railPath, parent, alongX ? c : fixedCenter, alongX ? fixedCenter : c, y,
                alongX ? railYawAlongX : railYawAlongZ, 1f);
        }
    }

    /// <summary>Su üstünde ray: ahşap güverte + direkler + raylar.</summary>
    static void RailBridge(Transform parent, bool alongX, float fixedCenter, float fromEdge, float toEdge, float y)
    {
        if (fromEdge > toEdge) { float t = fromEdge; fromEdge = toEdge; toEdge = t; }
        RailLine(parent, alongX, fixedCenter, fromEdge, toEdge, y);
        string deck = CubeDir + "Cube_Wood_Dark.prefab";
        if (Load(deck) == null) return;
        Vector3 deckSize = MeasureSize(deck, out Bounds db);
        const float thk = 0.2f;
        for (float c = fromEdge + 0.5f; c < toEdge; c += 1f)
        {
            Vector3 p = alongX ? new Vector3(c, y - db.max.y * thk, fixedCenter) : new Vector3(fixedCenter, y - db.max.y * thk, c);
            var d = Spawn(deck, parent, p);
            if (d) d.transform.localScale = alongX ? new Vector3(1f, thk, 1.4f) : new Vector3(1.4f, thk, 1f);
        }
        Posts(parent, alongX, fixedCenter, fromEdge + 1.5f, toEdge, 3f, 0.55f);
    }

    /// <summary>Köprü / iskele altına sudan yükselen ahşap direkler.</summary>
    static void Posts(Transform parent, bool alongX, float fixedCenter, float from, float to, float step, float halfWidth)
    {
        string deck = CubeDir + "Cube_Wood_Dark.prefab";
        if (Load(deck) == null) return;
        for (float c = from; c < to; c += step)
            for (int side = -1; side <= 1; side += 2)
                for (int L = -1; L >= bottomLayer - 1; L--)
                {
                    Vector3 p = alongX
                        ? new Vector3(c, L * cellH, fixedCenter + side * halfWidth)
                        : new Vector3(fixedCenter + side * halfWidth, L * cellH, c);
                    var post = Spawn(deck, parent, p);
                    if (post) post.transform.localScale = new Vector3(0.25f, 1f, 0.25f);
                }
    }

    // =====================================================================
    // Vegetation + free-roaming collectibles
    // =====================================================================
    static void BuildVegetationAndCollectibles(Transform root, RectInt grove, float treeMul, float collectMul)
    {
        Transform veg = Group(root, "Vegetation");
        Transform trees = Group(veg, "Trees");
        Transform ground = Group(veg, "GroundCover");
        Transform col = Group(root, "Collectibles");
        Transform woods = Group(col, "Woods");
        Transform rocks = Group(col, "Rocks");
        Transform mush = Group(col, "MushRooms");
        var treeCells = new HashSet<Vector2Int>();

        bool NearBusy(int i, int j) =>
            NearUse(i, j, Use.Path) || NearUse(i, j, Use.Plaza) || NearUse(i, j, Use.Building) ||
            NearUse(i, j, Use.FieldArea) || NearUse(i, j, Use.Station) || NearUse(i, j, Use.Rail);

        for (int i = MinX; i <= MaxX; i++)
            for (int j = MinZ; j <= MaxZ; j++)
            {
                if (!IsLand(i, j) || IsOccupied(i, j) || GetUse(i, j) != Use.Free || Top(i, j) < 0) continue;
                if (NearBusy(i, j)) continue;
                int cd = Coast(i, j);
                bool inGrove = grove.Contains(new Vector2Int(i, j));
                float chance = (inGrove ? 0.30f : (cd >= 2 && cd <= 3 ? 0.14f : 0.04f)) * treeMul;
                if (rng.NextDouble() >= chance || HasNeighbor(treeCells, i, j)) continue;

                string tp = rng.NextDouble() < 0.8 ? PlantDir + Pick(Trees) + ".prefab" : MedDir + Pick(MedievalTrees) + ".prefab";
                Spawn(tp, trees, CellPos(i, j, Surface(0)) + Jitter(0.15f), rng.Next(360), Range(0.9f, 1.4f));
                treeCells.Add(new Vector2Int(i, j));
                Occupy(i, j);
            }

        for (int i = MinX; i <= MaxX; i++)
            for (int j = MinZ; j <= MaxZ; j++)
            {
                if (!IsLand(i, j) || IsOccupied(i, j) || GetUse(i, j) != Use.Free) continue;
                Vector3 c = CellPos(i, j, Surface(Top(i, j)));
                bool nearTree = HasNeighbor(treeCells, i, j);
                bool inGrove = grove.Contains(new Vector2Int(i, j));
                int cd = Coast(i, j);
                bool nearRoad = NearBusy(i, j);

                double r = rng.NextDouble();
                double wood = nearRoad ? 0.0 : ((nearTree ? 0.16 : 0.04) + (inGrove ? 0.08 : 0.0)) * collectMul;
                double stone = nearRoad ? 0.0 : ((cd <= 2 ? 0.12 : 0.05) + (inGrove ? 0.05 : 0.0)) * collectMul;
                double food = nearRoad ? 0.0 : (nearTree ? 0.10 : 0.02) * collectMul;

                if (r < wood) { SpawnWood(woods, c); Occupy(i, j); }
                else if (r < wood + stone) { SpawnStone(rocks, c, 0.2f); Occupy(i, j); }
                else if (r < wood + stone + food) { SpawnMushroom(mush, c); Occupy(i, j); }
                else if (r < wood + stone + food + 0.16)
                    Spawn(PlantDir + Pick(GroundCover) + ".prefab", ground, c + Jitter(0.3f), rng.Next(360), Range(0.9f, 1.3f));
                else if (r < wood + stone + food + 0.24)
                    Spawn(PlantDir + Pick(Flowers) + ".prefab", ground, c + Jitter(0.3f), rng.Next(360));
            }
    }

    static bool HasNeighbor(HashSet<Vector2Int> cells, int i, int j)
    {
        for (int di = -1; di <= 1; di++)
            for (int dj = -1; dj <= 1; dj++)
                if (cells.Contains(new Vector2Int(i + di, j + dj))) return true;
        return false;
    }

    static void BuildShoreRocks(Transform root)
    {
        Transform rocks = Group(root, "ShoreRocks");
        float beachY = Surface(bottomLayer);
        for (int i = MinX; i <= MaxX; i++)
            for (int j = MinZ; j <= MaxZ; j++)
            {
                if (IsLand(i, j) || keepClear.Contains(new Vector2Int(i, j))) continue;
                if (IsShore(i, j))
                {
                    if (rng.NextDouble() < 0.2)
                        Spawn(RockDir + Pick(ShoreRocks) + ".prefab", rocks, CellPos(i, j, beachY) + Jitter(0.3f), rng.Next(360), Range(0.8f, 1.4f));
                }
                else if (Coast(i, j) == 1 && rng.NextDouble() < 0.06)
                {
                    Spawn(RockDir + Pick(WaterRocks) + ".prefab", rocks, CellPos(i, j, waterY - 0.3f) + Jitter(0.3f), rng.Next(360), Range(1.0f, 1.8f));
                }
            }
    }

    // =====================================================================
    // Collectibles (sahnedeki Woods / Rocks / MushRooms ile aynı kurulum)
    // =====================================================================
    static GameObject SpawnWood(Transform parent, Vector3 cellCenter)
    {
        var go = Spawn(Pick(WoodPrefabs) + ".prefab", parent, cellCenter + Jitter(0.1f), rng.Next(2) * 90f + Range(-12f, 12f));
        return MakeCollectible(go, CollectibleType.Wood);
    }

    static GameObject SpawnStone(Transform parent, Vector3 cellCenter, float bigChance)
    {
        string p = rng.NextDouble() < bigChance ? Pick(StoneBig) : Pick(StoneSmall);
        var go = Spawn(RockDir + p + ".prefab", parent, cellCenter + Jitter(0.15f), rng.Next(360));
        return MakeCollectible(go, CollectibleType.Stone);
    }

    static GameObject SpawnMushroom(Transform parent, Vector3 cellCenter)
    {
        var go = Spawn(PlantDir + Pick(Mushrooms) + ".prefab", parent, cellCenter + Jitter(0.2f), rng.Next(360));
        return MakeCollectible(go, CollectibleType.Food);
    }

    static GameObject MakeCollectible(GameObject go, CollectibleType type)
    {
        if (go == null) return null;
        var c = go.GetComponent<Collectible>();
        if (c == null) c = go.AddComponent<Collectible>();
        c.collectibleType = type;
        collectibleCount[(int)type]++;
        return go;
    }

    // =====================================================================
    // Buildings & props
    // =====================================================================
    /// <summary>Ana model + gizli inşaat aşamaları (MainHouse ile aynı düzen).</summary>
    static void BuildStaged(Transform parent, string name, string mainPath, string[] stagePaths, RectInt rect, float yaw, float maxFoot, float y)
    {
        Transform g = Group(parent, name);
        float cx = rect.xMin + rect.width * 0.5f, cz = rect.yMin + rect.height * 0.5f;
        if (Load(mainPath) == null) return;
        float s = FitScale(mainPath, maxFoot);
        if (PlaceCentered(mainPath, g, cx, cz, y, yaw, s) == null) return;
        if (stagePaths == null) return;
        foreach (var sp in stagePaths)
        {
            GameObject stage = PlaceCentered(sp, g, cx, cz, y, yaw, s);
            if (stage) stage.SetActive(false);
        }
    }

    /// <summary>maxFoot > 0 ise büyük prop'ları bu taban boyutuna küçültür. Suya denk gelirse atlanır.</summary>
    static GameObject Prop(string path, Transform parent, float x, float z, float yaw, float maxFoot)
    {
        int i = Mathf.FloorToInt(x), j = Mathf.FloorToInt(z);
        if (!IsLand(i, j) || Load(path) == null) return null;
        float s = maxFoot > 0f ? FitScale(path, maxFoot) : 1f;
        var go = PlaceCentered(path, parent, x, z, Surface(Top(i, j)), yaw, s);
        Occupy(i, j);
        return go;
    }

    static float FitScale(string path, float maxFoot)
    {
        Vector3 size = MeasureSize(path);
        float foot = Mathf.Max(size.x, size.z);
        return foot > maxFoot ? maxFoot / foot : 1f;
    }

    static Transform Anchor(Transform parent, string name, Vector3 local)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = local;
        return t;
    }

    // =====================================================================
    // Low-level helpers
    // =====================================================================
    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject Load(string path)
    {
        if (prefabCache.TryGetValue(path, out var p)) return p;
        p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        prefabCache[path] = p;
        if (p == null) missing.Add(path);
        return p;
    }

    static GameObject Spawn(string path, Transform parent, Vector3 localPos, float yaw = 0f, float scale = 1f, string name = null)
    {
        GameObject prefab = Load(path);
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.localRotation;
        go.transform.localScale = prefab.transform.localScale * scale;
        if (name != null) go.name = name;
        spawned++;
        return go;
    }

    /// <summary>Taban merkezi (x,z)'ye, tabanı y'ye oturtur (gerçek renderer sınırlarıyla; pivot kaymasından bağımsız).</summary>
    static GameObject PlaceCentered(string path, Transform parent, float x, float z, float y, float yaw, float scale)
    {
        var go = Spawn(path, parent, new Vector3(x, y, z), yaw, scale);
        if (go == null) return null;
        Bounds? rb = RendererBounds(go);
        if (rb.HasValue)
        {
            Vector3 target = parent.TransformPoint(new Vector3(x, y, z));
            Bounds b = rb.Value;
            go.transform.position += new Vector3(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z);
        }
        return go;
    }

    /// <summary>Sadece yatay (x,z) hizalama; yükseklik pivot'tan (ör. köprü y = 0.85).</summary>
    static GameObject PlaceCenteredXZ(string path, Transform parent, float x, float z, float pivotY, float yaw)
    {
        var go = Spawn(path, parent, new Vector3(x, pivotY, z), yaw);
        if (go == null) return null;
        Bounds? rb = RendererBounds(go);
        if (rb.HasValue)
        {
            Vector3 target = parent.TransformPoint(new Vector3(x, pivotY, z));
            go.transform.position += new Vector3(target.x - rb.Value.center.x, 0f, target.z - rb.Value.center.z);
        }
        return go;
    }

    static Bounds? RendererBounds(GameObject go)
    {
        Bounds? result = null;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (result == null) result = r.bounds;
            else { var b = result.Value; b.Encapsulate(r.bounds); result = b; }
        }
        return result;
    }

    static readonly Dictionary<string, Bounds> localBoundsCache = new Dictionary<string, Bounds>();

    static Vector3 MeasureSize(string path) => MeasureSize(path, out _);

    /// <summary>Prefab sahneye konduğundaki gerçek boyut ve pivota göre sınırlar (yaw = 0, ölçek = 1).</summary>
    static Vector3 MeasureSize(string path, out Bounds localBounds)
    {
        if (sizeCache.TryGetValue(path, out var s))
        {
            localBounds = localBoundsCache[path];
            return s;
        }
        GameObject prefab = Load(path);
        s = Vector3.one;
        localBounds = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
        if (prefab != null)
        {
            var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Vector3 origin = new Vector3(0f, -5000f, 0f);
            tmp.transform.position = origin;
            Bounds? b = RendererBounds(tmp);
            if (b.HasValue)
            {
                s = b.Value.size;
                localBounds = new Bounds(b.Value.center - origin, b.Value.size);
            }
            Object.DestroyImmediate(tmp);
        }
        sizeCache[path] = s;
        localBoundsCache[path] = localBounds;
        return s;
    }

    static string Pick(params string[] options) => options[rng.Next(options.Length)];
    static float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static Vector3 Jitter(float r) => new Vector3(Range(-r, r), 0f, Range(-r, r));
}
