using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticaret İskelesi (Trade_Island)
///   - Central_Island'ın kuzeydoğusu ile Farm_Island'ın kuzeyi arasında.
///   - Batıdan köprü: Central_Island (z = 51.5 hattı).
///   - Güneyden yürüme köprüsü + ray köprüsü: Farm_Island (x = 39.5 / 38.5 hatları).
///   - Taş meydan, pazar tezgahları, pazar binası, depo, ray terminali ve kuzeybatıda iskele.
/// Önce Farm_Island kurulmuş olmalı (köprüler onun Anchors noktalarına bağlanır).
/// </summary>
public static partial class IslandBuilder
{
    const string TradeRootName = "Trade_Island";
    static readonly Vector3 TradeOrigin = new Vector3(30f, 0f, 57f);

    const int TradeRailCol = 8;    // dünya x = 38.5 (Farm_Island ray hattı)
    const int TradeWalkCol = 9;    // dünya x = 39.5 (Farm_Island yürüme hattı)
    const int TradePathRow = -6;   // dünya z = 51.5 -> Central_Island
    const int TradeTerminalJ = 1;  // ray terminalinin son hücresi
    const int PierCol = -9;
    const float CentralEdgeXAtTradeRow = 10.5f; // Central_Island'ın z = 51.5 hattındaki en doğu küpü
    const float DefaultFarmNorthEdgeZ = 41.5f;

    static readonly RectInt TradePlazaRect = new RectInt(-6, -3, 12, 7);
    static readonly RectInt MarketRect = new RectInt(-3, 4, 6, 4);
    static readonly RectInt WarehouseRect = new RectInt(9, -2, 4, 4);
    static readonly RectInt TerminalRect = new RectInt(6, -2, 2, 4);

    const string Containers = RpgDir + "Props/Containers/";

    [MenuItem("Cloud Harvest/Islands/Build Trade Island")]
    static void MenuBuildTrade()
    {
        if (!CanBuild() || !ConfirmReplace(TradeRootName, "Trade_Island (Planned)")) return;
        ClearCaches();
        BuildTrade(DefaultSeed + 7);
        ReportMissing();
    }

    static GameObject BuildTrade(int seed)
    {
        GameObject farm = FindRoot(FarmRootName);
        if (farm == null)
            Debug.LogWarning("[Islands] Farm_Island bulunamadı; Farm köprüleri varsayılan kıyıya (z = 41.5) kurulacak.");

        BeginIsland(seed, -17, 17, -13, 13, 13f, 8.5f, true);

        // 1) Ayrılmış alanlar
        MarkRect(TradePlazaRect, Use.Plaza);
        MarkRect(MarketRect, Use.Building);
        MarkRect(WarehouseRect, Use.Building);
        MarkRect(TerminalRect, Use.Station);
        for (int i = 0; i <= TradeWalkCol; i++) SetUse(i, TradePathRow, Use.Path);         // güney yolu
        for (int j = TradePathRow + 1; j < TradePlazaRect.yMin; j++) SetUse(0, j, Use.Path); // meydana bağlantı
        SetUse(-8, 2, Use.Path); SetUse(-7, 2, Use.Path);                                    // iskeleye yol

        // 2) Şekil + köprü ayakları
        GenerateShape(new Vector2Int(0, 0));
        int westEdge = Landing(0, TradePathRow, -1, 0, Use.Path);              // -> Central_Island
        int railEdge = Landing(TradeRailCol, TradeTerminalJ, 0, -1, Use.Rail); // -> Farm_Island (ray)
        int walkEdge = Landing(TradeWalkCol, TradePathRow, 0, -1, Use.Path);   // -> Farm_Island (yaya)
        int pierEdge = Landing(PierCol, 2, 0, 1, Use.Path);                    // -> iskele
        FinishLayout();

        var root = new GameObject(TradeRootName);
        root.transform.position = TradeOrigin;
        Transform t = root.transform;
        float y0 = Surface(0);

        BuildTerrain(t);
        BuildTradeBuildings(t);
        BuildMarketPlaza(t);
        BuildTerminal(t, railEdge);
        BuildPier(t, pierEdge);

        // Köprüler
        Transform toCentral = Group(t, "Bridge_ToCentralIsland");
        BridgeLine(toCentral, true, TradePathRow + 0.5f, CentralEdgeXAtTradeRow + 1f - TradeOrigin.x, westEdge - 0.5f);

        Transform farmWalk = farm ? farm.transform.Find("Anchors/North_Walk") : null;
        Transform farmRail = farm ? farm.transform.Find("Anchors/North_Rail") : null;
        float farmWalkZ = farmWalk ? farmWalk.position.z : DefaultFarmNorthEdgeZ;
        float farmRailZ = farmRail ? farmRail.position.z : DefaultFarmNorthEdgeZ;

        Transform toFarm = Group(t, "Bridge_ToFarmIsland");
        BridgeLine(toFarm, false, TradeWalkCol + 0.5f, farmWalkZ + 1f - TradeOrigin.z, walkEdge - 0.5f);

        Transform railBridge = Group(t, "RailBridge_ToFarmIsland");
        RailBridge(railBridge, false, TradeRailCol + 0.5f, farmRailZ + 0.5f - TradeOrigin.z, railEdge, y0);

        BuildVegetationAndCollectibles(t, new RectInt(0, 0, 0, 0), 0.7f, 0.35f);
        BuildShoreRocks(t);

        // Tarla adasının vagon rotasını aç
        var route = farm ? farm.GetComponentInChildren<WagonRoute>(true) : null;
        if (route)
        {
            Undo.RecordObject(route, "Link Wagon Route");
            route.requiredConnection = railBridge.gameObject;
            route.routeOpen = true;
            EditorUtility.SetDirty(route);
        }

        EndIsland(root, "Trade_Island");
        Selection.activeGameObject = root;
        return root;
    }

    // =====================================================================
    static void BuildTradeBuildings(Transform root)
    {
        Transform b = Group(root, "Buildings");
        float y0 = Surface(0);

        BuildStaged(b, "Market", BldDir + "Market.FBX",
            new[] { BldDir + "construction/Market_0.FBX", BldDir + "construction/Market_1.FBX" },
            MarketRect, 180f, 5.5f, y0);
        BuildStaged(b, "Warehouse", "Assets/Models/free Storage House/Prefab/Storage_House.prefab", null,
            WarehouseRect, -90f, 3.8f, y0);
    }

    static void BuildMarketPlaza(Transform root)
    {
        Transform p = Group(root, "MarketPlaza");
        Transform stalls = Group(p, "Stalls");
        Transform deco = Group(p, "Decor");

        // Tezgahlar: kuzey sırası meydana (güneye), güney sırası kuzeye bakar
        Stall(stalls, "Stall_Produce", RpgDir + "Exterior/Wood_path/rpgpp_lt_awning_standing_01a.prefab", -3.5f, 2.4f, 180f,
            (PlantDir + "Pumpkin.prefab", -0.8f, -0.8f), (PlantDir + "Watermelon.prefab", 0f, -0.9f), (PlantDir + "Cabbage.prefab", 0.8f, -0.8f));
        Stall(stalls, "Stall_Grain", RpgDir + "Exterior/Wood_path/rpgpp_lt_awning_standing_01b.prefab", 2.5f, 2.4f, 180f,
            (Containers + "rpgpp_lt_sack_open_01.prefab", -0.8f, -0.8f), (Containers + "rpgpp_lt_sack_02_set.prefab", 0.8f, -0.8f));
        Stall(stalls, "Stall_Goods", RpgDir + "Exterior/Wood_path/rpgpp_lt_awning_standing_01a.prefab", -3.5f, -2.2f, 0f,
            (Containers + "rpgpp_lt_vase_01.prefab", -0.8f, 0.8f), (Containers + "rpgpp_lt_jug_01.prefab", 0f, 0.9f), (Containers + "rpgpp_lt_basket_02.prefab", 0.8f, 0.8f));
        Stall(stalls, "Stall_Coins", RpgDir + "Exterior/Wood_path/rpgpp_lt_awning_standing_01b.prefab", 2.5f, -2.2f, 0f,
            (ItemDir + "CoinsGroup1.prefab", -0.7f, 0.8f), (Containers + "rpgpp_lt_crate_03.prefab", 0.8f, 0.8f));

        Prop(RpgDir + "Props/Misc/rpgpp_lt_well_01.prefab", deco, -0.5f, 0.5f, 0f, 1.8f);
        Prop(RpgDir + "Props/Benches/rpgpp_lt_bench_wood_01.prefab", deco, -2.3f, 0.5f, 90f, 1.6f);
        Prop(RpgDir + "Props/Benches/rpgpp_lt_bench_wood_01.prefab", deco, 1.3f, 0.5f, -90f, 1.6f);
        Prop(TTPrefabDir + "banners/TT_Banner_Blue_A.prefab", deco, -5.5f, 3.5f, 180f, 1.2f);
        Prop(TTPrefabDir + "banners/TT_Banner_Blue_A.prefab", deco, 5.5f, 3.5f, 180f, 1.2f);
        Prop(MedDir + "Streetlight.prefab", deco, -5.6f, -2.6f, 0f, 0f);
        Prop(MedDir + "Streetlight.prefab", deco, 5.6f, -2.6f, 0f, 0f);
        Prop(MedDir + "Streetlight.prefab", deco, 0.6f, TradePathRow + 1.4f, 0f, 0f);
        Prop(ItemDir + "Chest.prefab", deco, 8.6f, 2.4f, 180f, 1f);
    }

    static void Stall(Transform parent, string name, string awningPath, float x, float z, float yaw,
        params (string path, float dx, float dz)[] goods)
    {
        Transform s = Group(parent, name);
        Prop(awningPath, s, x, z, yaw, 2.2f);
        foreach (var g in goods)
            Prop(g.path, s, x + g.dx, z + g.dz, Range(0f, 360f), 0.9f);
    }

    static void BuildTerminal(Transform root, int railEdge)
    {
        Transform st = Group(root, "RailTerminal");
        float y0 = Surface(0);
        float x = TradeRailCol + 0.5f;

        RailLine(Group(st, "Rails"), false, x, railEdge, TradeTerminalJ + 1, y0);
        Prop(ItemDir + "Box.prefab", st, x, TradeTerminalJ + 1.4f, 0f, 1f); // ray sonu tamponu

        Transform cargo = Group(st, "Cargo");
        Prop(ItemDir + "BarrelsGroup1.prefab", cargo, 6.5f, -1.5f, 30f, 1f);
        Prop(Containers + "rpgpp_lt_crate_02.prefab", cargo, 7.4f, -0.5f, -10f, 1f);
        Prop(Containers + "rpgpp_lt_sack_02_set.prefab", cargo, 6.5f, 0.5f, 0f, 1f);
        Prop(Containers + "rpgpp_lt_crate_01.prefab", cargo, 7.3f, 1.4f, 15f, 1f);

        Anchor(st, "UnloadPoint", new Vector3(x - 1.1f, y0, TradeTerminalJ + 0.5f));
    }

    static void BuildPier(Transform root, int pierEdge)
    {
        Transform pier = Group(root, "Pier");
        Transform deck = Group(pier, "Deck");
        float x = PierCol + 0.5f;
        float from = pierEdge + 1.5f, to = pierEdge + 7.5f;
        BridgeLine(deck, false, x, from, to);
        Posts(Group(pier, "Posts"), false, x, from + 0.5f, to + 0.5f, 2f, 0.55f);

        if (deck.childCount == 0) return;
        Bounds? db = RendererBounds(deck.GetChild(deck.childCount - 1).gameObject);
        if (!db.HasValue) return;
        float deckTop = db.Value.max.y - root.position.y;

        Transform cargo = Group(pier, "Cargo");
        void OnDeck(string path, float dx, float dz, float yaw)
        {
            if (Load(path) == null) return;
            PlaceCentered(path, cargo, x + dx, to + dz, deckTop, yaw, FitScale(path, 0.9f));
        }
        OnDeck(ItemDir + "Barrel.prefab", -0.3f, 0f, 0f);
        OnDeck(ItemDir + "Box.prefab", 0.3f, -0.6f, 20f);
        OnDeck(Containers + "rpgpp_lt_sack_01.prefab", -0.3f, -1.4f, 0f);
        OnDeck(ItemDir + "Flag.prefab", 0.35f, 0.3f, 0f);
    }
}
