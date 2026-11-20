using UnityEditor;
using UnityEngine;

/// <summary>
/// Tarla Adası (Farm_Island)
///   - Central_Island'ın doğusunda; batıdaki köprüyle (z = 32.5) ana adaya bağlı.
///   - Kuzeyden ray + yürüme yolu Ticaret İskelesi'ne gider (köprüleri TradeIsland kurar).
///   - 3 çitli tarla alanı (Central_Island'daki şablon), her birinde iki 4x4 parsel.
/// </summary>
public static partial class IslandBuilder
{
    const string FarmRootName = "Farm_Island";
    static readonly Vector3 FarmOrigin = new Vector3(37f, 0f, 29f);

    const int FarmPathRow = 3;          // dünya z = 32.5 -> Central_Island'ın en doğu kıyısı
    const int FarmRailCol = 1;          // dünya x = 38.5 -> Ticaret İskelesi'ndeki ray ile aynı hat
    const int FarmWalkCol = 2;          // dünya x = 39.5 -> Ticaret İskelesi'ne yürüme köprüsü
    const float CentralEdgeXAtFarmRow = 16.5f;

    static readonly RectInt FarmhouseRect = new RectInt(-4, 5, 5, 5);
    static readonly RectInt GranaryRect = new RectInt(-8, -1, 4, 4);
    static readonly RectInt FarmStationRect = new RectInt(-2, -1, 6, 4);
    static readonly RectInt FarmGroveRect = new RectInt(-18, 4, 12, 10); // kuzeybatı koru

    // ---------- Tarla alanı şablonu (Central_Island'daki çitli tarladan alındı) ----------
    const int AreaW = 11, AreaH = 6, PlotSize = 4;
    static readonly int[] PlotOffsetsX = { 1, 6 };
    static readonly int[] GateCellsX = { 2, 3, 7, 8 };
    const float FenceInset = 0.33f;

    class AreaDef
    {
        public string id;
        public RectInt rect;
        public bool gateNorth;
        public int firstOrder;
    }

    static readonly AreaDef[] Areas =
    {
        new AreaDef { id = "FarmArea_01", rect = new RectInt(-14, -8, AreaW, AreaH), gateNorth = true,  firstOrder = 1 },
        new AreaDef { id = "FarmArea_02", rect = new RectInt(-2, -8, AreaW, AreaH),  gateNorth = true,  firstOrder = 3 },
        new AreaDef { id = "FarmArea_03", rect = new RectInt(3, 4, AreaW, AreaH),    gateNorth = false, firstOrder = 5 },
    };

    static RectInt PlotRect(AreaDef a, int k) =>
        new RectInt(a.rect.xMin + PlotOffsetsX[k], a.rect.yMin + 1, PlotSize, PlotSize);

    // =====================================================================
    [MenuItem("Cloud Harvest/Islands/Build Farm Island")]
    static void MenuBuildFarm()
    {
        if (!CanBuild() || !ConfirmReplace(FarmRootName, "Trade_Island (Planned)")) return;
        ClearCaches();
        BuildFarm(DefaultSeed);
        ReportMissing();
    }

    [MenuItem("Cloud Harvest/Islands/Build Farm + Trade Islands")]
    static void MenuBuildAll()
    {
        if (!CanBuild() || !ConfirmReplace(FarmRootName, TradeRootName, "Trade_Island (Planned)")) return;
        ClearCaches();
        BuildFarm(DefaultSeed);
        BuildTrade(DefaultSeed + 7);
        ReportMissing();
    }

    static void ClearCaches()
    {
        prefabCache.Clear();
        sizeCache.Clear();
        localBoundsCache.Clear();
        missing.Clear();
    }

    static GameObject BuildFarm(int seed)
    {
        BeginIsland(seed, -22, 22, -18, 18, 17f, 13.5f, false);

        // 1) Ayrılmış alanlar
        foreach (var a in Areas)
        {
            MarkRect(a.rect, Use.FieldArea);
            for (int k = 0; k < PlotOffsetsX.Length; k++) MarkRect(PlotRect(a, k), Use.Field);
        }
        MarkRect(FarmhouseRect, Use.Building);
        MarkRect(GranaryRect, Use.Building);
        MarkRect(FarmStationRect, Use.Station);
        for (int j = -2; j <= 2; j++) SetUse(-3, j, Use.Path);   // iki güney tarla alanı arası yol
        SetUse(-2, 4, Use.Path); SetUse(-1, 4, Use.Path);         // çiftlik evi kapısı
        for (int i = -4; i <= 14; i++) SetUse(i, FarmPathRow, Use.Path);

        // 2) Şekil + köprü ayakları
        GenerateShape(new Vector2Int(0, 0));
        int westEdge = Landing(-4, FarmPathRow, -1, 0, Use.Path);            // -> Central_Island
        int railEdge = Landing(FarmRailCol, -1, 0, 1, Use.Rail);             // -> Ticaret İskelesi (ray)
        int walkEdge = Landing(FarmWalkCol, FarmPathRow + 1, 0, 1, Use.Path); // -> Ticaret İskelesi (yaya)
        FinishLayout();

        var root = new GameObject(FarmRootName);
        root.transform.position = FarmOrigin;
        Transform t = root.transform;
        float y0 = Surface(0);

        BuildTerrain(t);
        BuildFarmFields(t);
        BuildFarmBuildings(t, westEdge);
        BuildFarmStation(t, railEdge);

        // Central_Island'a köprü
        Transform br = Group(t, "Bridge_ToCentralIsland");
        BridgeLine(br, true, FarmPathRow + 0.5f,
            CentralEdgeXAtFarmRow + 1f - FarmOrigin.x,
            westEdge - 0.5f);

        // Diğer adaların köprü bağlayacağı noktalar
        Transform anchors = Group(t, "Anchors");
        Anchor(anchors, "North_Rail", new Vector3(FarmRailCol + 0.5f, y0, railEdge + 0.5f));
        Anchor(anchors, "North_Walk", new Vector3(FarmWalkCol + 0.5f, y0, walkEdge + 0.5f));
        Anchor(anchors, "West_Walk", new Vector3(westEdge + 0.5f, y0, FarmPathRow + 0.5f));

        BuildVegetationAndCollectibles(t, FarmGroveRect, 1f, 1f);
        BuildShoreRocks(t);
        BuildFarmRoute(t, railEdge);

        EndIsland(root, "Farm_Island");
        Selection.activeGameObject = root;
        return root;
    }

    // =====================================================================
    // Fields
    // =====================================================================
    static void BuildFarmFields(Transform root)
    {
        Transform fieldsRoot = Group(root, "Fields");
        float y0 = Surface(0);

        foreach (var a in Areas)
        {
            Transform area = Group(fieldsRoot, a.id);
            BuildAreaFence(a, Group(area, "Fence"), y0);

            for (int k = 0; k < PlotOffsetsX.Length; k++)
            {
                RectInt pr = PlotRect(a, k);
                Transform fr = Group(area, $"{a.id}_Plot_{(char)('A' + k)}");
                var field = fr.gameObject.AddComponent<FarmField>();
                field.fieldId = fr.name;
                field.unlockOrder = a.firstOrder + k;
                field.unlocked = false; // hepsi kilitli başlar: önce toplanabilirler temizlenmeli

                Transform locked = Group(fr, "Locked");
                Transform lockedGround = Group(locked, "Ground");
                Transform lockedCollect = Group(locked, "Collectibles");
                Transform weeds = Group(locked, "Weeds");

                Transform unlocked = Group(fr, "Unlocked");
                Transform soil = Group(unlocked, "Ground");
                Transform crops = Group(unlocked, "Crops");

                for (int i = pr.xMin; i < pr.xMax; i++)
                    for (int j = pr.yMin; j < pr.yMax; j++)
                    {
                        SpawnCube("Cube_SoilWGrass_1", lockedGround, i, j, 0);
                        SpawnCube("Cube_Soil", soil, i, j, 0);
                        Occupy(i, j);

                        Vector3 c = CellPos(i, j, y0);
                        int roll = rng.Next(100);
                        if (roll < 32) SpawnWood(lockedCollect, c);
                        else if (roll < 58) SpawnStone(lockedCollect, c, 0.12f);
                        else if (roll < 74) SpawnMushroom(lockedCollect, c);
                        else if (roll < 90)
                            Spawn(PlantDir + Pick(GroundCover) + ".prefab", weeds, c + Jitter(0.25f), rng.Next(360), Range(0.9f, 1.3f));
                    }

                var drop = new GameObject("DropOffPoint").transform;
                drop.SetParent(fr, false);
                float gx = a.rect.xMin + GateCellsX[k * 2] + 1f;
                float gz = a.gateNorth ? a.rect.yMax + 0.5f : a.rect.yMin - 0.5f;
                drop.localPosition = new Vector3(gx, y0, gz);

                field.lockedState = locked.gameObject;
                field.unlockedState = unlocked.gameObject;
                field.cropsRoot = crops;
                field.dropOffPoint = drop;
                locked.gameObject.SetActive(true);
                unlocked.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Central_Island'daki çitli tarla ile aynı kurulum: kenar hücresi başına bir FanceWooden,
    /// hücre merkezinden 0.33 dışarıda, dışa dönük (K=0, D=90, G=180, B=-90). Girişte boşluk.
    /// </summary>
    static void BuildAreaFence(AreaDef a, Transform parent, float y)
    {
        string fence = ItemDir + "FanceWooden.prefab";
        RectInt r = a.rect;
        int gateRow = a.gateNorth ? r.yMax - 1 : r.yMin;

        for (int i = r.xMin; i < r.xMax; i++)
        {
            bool gate = System.Array.IndexOf(GateCellsX, i - r.xMin) >= 0;
            int jn = r.yMax - 1, js = r.yMin;
            if (!(gate && gateRow == jn))
                Spawn(fence, parent, new Vector3(i + 0.5f, y, jn + 0.5f + FenceInset), 0f);
            if (!(gate && gateRow == js))
                Spawn(fence, parent, new Vector3(i + 0.5f, y, js + 0.5f - FenceInset), 180f);
        }
        for (int j = r.yMin; j < r.yMax; j++)
        {
            Spawn(fence, parent, new Vector3(r.xMax - 0.5f + FenceInset, y, j + 0.5f), 90f);
            Spawn(fence, parent, new Vector3(r.xMin + 0.5f - FenceInset, y, j + 0.5f), -90f);
        }
    }

    // =====================================================================
    // Buildings, station, route
    // =====================================================================
    static void BuildFarmBuildings(Transform root, int westEdge)
    {
        Transform b = Group(root, "Buildings");
        float y0 = Surface(0);

        BuildStaged(b, "Farmhouse", BldDir + "Farm.FBX",
            new[] { BldDir + "construction/Farm_0.FBX", BldDir + "construction/Farm_1.FBX" },
            FarmhouseRect, 180f, 5f, y0);
        BuildStaged(b, "Granary", BldDir + "Granary.FBX",
            new[] { BldDir + "construction/Granary_0.FBX", BldDir + "construction/Granary_1.FBX" },
            GranaryRect, 90f, 3.8f, y0);

        Transform props = Group(b, "Props");
        Prop(MedDir + "Well.prefab", props, -5.8f, 6.8f, -90f, 1.6f);
        Prop(RpgDir + "Props/Containers/rpgpp_lt_trough_01.prefab", props, -5.8f, 8.8f, 0f, 1.4f);
        Prop(MedDir + "Haystack_01.prefab", props, -12.5f, -0.6f, 20f, 1.6f);
        Prop(MedDir + "Haystack_02.prefab", props, -10.8f, 0.9f, -35f, 1.4f);
        Prop(MedDir + "Haystack_03.prefab", props, -13.4f, 1.3f, 70f, 1.4f);
        Prop(RpgDir + "Props/Misc/rpgpp_lt_wagon_01.prefab", props, -4.2f, -1.5f, 100f, 1.8f);
        Prop(RpgDir + "Props/Items/rpgpp_lt_rake_01.prefab", props, -14.6f, -2.2f, 10f, 1.4f);
        Prop(MedDir + "Streetlight.prefab", props, westEdge + 0.6f, FarmPathRow - 0.25f, 0f, 0f);
        Prop(MedDir + "Streetlight.prefab", props, westEdge + 0.6f, FarmPathRow + 1.25f, 0f, 0f);
        Prop(MedDir + "Streetlight.prefab", props, -8.5f, FarmPathRow + 1.3f, 0f, 0f);
        Prop(MedDir + "Streetlight.prefab", props, 3.6f, 2.6f, 0f, 0f);
    }

    const float WagonLength = 1.4f;

    static void BuildFarmStation(Transform root, int railEdge)
    {
        Transform st = Group(root, "Station");
        float y0 = Surface(0);
        float x = FarmRailCol + 0.5f;

        // İstasyondan kuzey kıyısına ray (Z boyunca)
        RailLine(Group(st, "Rails"), false, x, FarmStationRect.yMin, railEdge + 1, y0);

        string cartPath = ItemDir + "Carriage.prefab";
        if (Load(cartPath) != null)
        {
            Vector3 cs = MeasureSize(cartPath);
            float yaw = cs.x >= cs.z ? 90f : 0f; // uzun ekseni hat (Z) boyunca
            var cart = PlaceCentered(cartPath, st, x, 0.8f, y0 + railHeight * 0.5f, yaw, FitScale(cartPath, WagonLength));
            if (cart) cart.name = "Wagon_01";
        }

        Transform cargo = Group(st, "Cargo");
        Prop(RpgDir + "Props/Containers/rpgpp_lt_crate_01.prefab", cargo, -1.3f, -0.4f, 10f, 1f);
        Prop(RpgDir + "Props/Containers/rpgpp_lt_sack_02_set.prefab", cargo, -0.4f, 1.5f, 0f, 1f);
        Prop(ItemDir + "BarrelsGroup1.prefab", cargo, -1.5f, 1.4f, 45f, 1f);
        Prop(RpgDir + "Props/Containers/rpgpp_lt_basket_01.prefab", cargo, 2.6f, -0.5f, 0f, 1f);
        Prop(RpgDir + "Props/Containers/rpgpp_lt_crate_02.prefab", cargo, 3.4f, 0.6f, -15f, 1f);
        Prop(ItemDir + "Bag.prefab", cargo, 2.5f, 1.6f, 30f, 1f);
        Prop(ItemDir + "Box.prefab", cargo, x, FarmStationRect.yMin - 0.4f, 0f, 1f); // ray sonu tamponu

        Anchor(st, "LoadPoint", new Vector3(x - 1.1f, y0, 0.8f));
    }

    static void BuildFarmRoute(Transform root, int railEdge)
    {
        var routeGo = new GameObject("WagonRoute_ToTradeIsland");
        routeGo.transform.SetParent(root, false);
        var route = routeGo.AddComponent<WagonRoute>();

        float y = Surface(0) + railHeight * 0.5f;
        float x = FarmRailCol + 0.5f;
        Vector3 tradeTerminal = TradeOrigin + new Vector3(TradeRailCol + 0.5f, y, TradeTerminalJ + 0.5f) - FarmOrigin;
        Anchor(routeGo.transform, "WP_00_FarmStation", new Vector3(x, y, 0.8f));
        Anchor(routeGo.transform, "WP_01_FarmNorthEdge", new Vector3(x, y, railEdge + 1f));
        Anchor(routeGo.transform, "WP_02_TradeTerminal", tradeTerminal);

        // Ticaret İskelesi zaten varsa bağla
        GameObject trade = FindRoot(TradeRootName);
        Transform rb = trade ? trade.transform.Find("RailBridge_ToFarmIsland") : null;
        route.requiredConnection = rb ? rb.gameObject : null;
        route.routeOpen = rb != null && rb.gameObject.activeSelf;
    }
}
