using System.Linq;
using UnityEditor;
using UnityEngine;
using WarTable.Game;
using WarTable;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 指挥台和沉浸舞台用到的模型 / 材质 / 贴图（GameSetup 装配时调用）。
    /// 模型：Art/Models/Deck/*.fbx（Tools/make_deck_models.py）；字：ArtSource/Cover/v6/alpha/deck_*.png（Tools/make_deck_labels.py）。
    /// 缺哪样就留空，ConsoleDeck 会用简单几何体顶替，不影响功能。
    /// </summary>
    public static class DeckArt
    {
        const string ModelDir = "Assets/_WarTable/Art/Models/Deck", TexDir = "Assets/_WarTable/Art/Textures/Deck";
        static readonly string[] EraKeys = { "medieval", "gothic", "modern", "future", "space" };

        static GameObject Model(string name)
        {
            string path = $"{ModelDir}/{name}.fbx";
            if (AssetImporter.GetAtPath(path) is ModelImporter mi && (mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importNormals != ModelImporterNormals.Import))
            { mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importNormals = ModelImporterNormals.Import; mi.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>Art/Textures/Deck 下的一张贴图，顺手把导入设置改对：kind = "albedo"（sRGB）/ "normal"（法线图）/ "mask"（线性：R 金属度、A 光滑度）。</summary>
        static Texture2D DeckTex(string file, string kind, int max = 1024)
        {
            string path = $"{TexDir}/{file}_{kind}.png";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return null;
            bool normal = kind == "normal", srgb = kind == "albedo";
            if (ti.maxTextureSize != max || ti.anisoLevel != 4 || (ti.textureType == TextureImporterType.NormalMap) != normal || ti.sRGBTexture != srgb || !ti.mipmapEnabled)
            {
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; ti.sRGBTexture = srgb;
                ti.maxTextureSize = max; ti.anisoLevel = 4; ti.mipmapEnabled = true; ti.alphaIsTransparency = false;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// 指挥台实物的材质：Tools/make_deck_models.py 烘的全套 PBR——颜色（缝隙压暗、棱边磨亮、掉漆、积锈）、法线（拉丝 / 颗粒 / 划痕 / 锤痕）、
        /// 金属度 + 光滑度（R / A）。自发光开着（默认黑），游戏里用属性块点亮。
        /// </summary>
        static Material Metal(string name, string tex, float bump = 1f, Color? tintOpt = null)
            => BoardBuilder.Mat(name, "Universal Render Pipeline/Lit", m =>
            {
                var tint = tintOpt ?? Color.white;
                var a = DeckTex(tex, "albedo"); var n = DeckTex(tex, "normal"); var k = DeckTex(tex, "mask");
                m.SetTexture("_BaseMap", a); m.SetColor("_BaseColor", a ? tint : new Color(0.2f, 0.21f, 0.24f));
                m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", bump); if (n) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
                m.SetTexture("_MetallicGlossMap", k); if (k) m.EnableKeyword("_METALLICSPECGLOSSMAP"); else m.DisableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_SmoothnessTextureChannel", 0f); m.SetFloat("_Metallic", 0.8f); m.SetFloat("_Smoothness", k ? 1f : 0.4f);   // 有贴图时 _Smoothness 是倍率
                m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });

        /// <summary>一块字的贴图（Tools/make_deck_labels.py）：plate_* 有颜色 / 法线 / 金属度三张，cap_* 有颜色（带透明度）/ 法线两张。</summary>
        static ConsoleDeck.Lab Lab(string name)
        {
            var a = DeckTex("Labels/" + name, "albedo", 512);
            return a ? new ConsoleDeck.Lab { a = a, n = DeckTex("Labels/" + name, "normal", 512), m = DeckTex("Labels/" + name, "mask", 512) } : null;
        }

        /// <summary>
        /// 令标（11-16 用户：令牌被当成按钮、看不出能拿；模型要做好）：钢锥 + 六棱装甲筒 + 顶上一个大拉环，立在指挥台右边的插孔里。
        /// 三支三色（冲锋朱红 / 坚守冰蓝 / 撤退琥珀），发光的令徽和色带用各自的材质，金属身共用一个。模型缺了就留着旧令牌。
        /// </summary>
        public static void FillRack(OrderRack rack)
        {
            var cols = new[] { new Color(1f, 0.22f, 0.12f), new Color(0.25f, 0.62f, 1f), new Color(1f, 0.68f, 0.14f) };
            var models = OrderRack.Keys.Select(k => Model("beacon_" + k)).ToArray();
            if (models.Any(m => !m)) return;
            rack.beacon = true;
            for (int i = 0; i < 3; i++)
            {
                rack.modelZh[i] = rack.modelEn[i] = models[i];   // 令标上只有图形令徽，中英共用；字在架子的名牌上
                var c = cols[i];
                rack.lacquer[i] = BoardBuilder.Mat($"M_Beacon_Glow_{OrderRack.Keys[i]}", "Universal Render Pipeline/Lit", m =>
                {
                    m.SetTexture("_BaseMap", null); m.SetTexture("_BumpMap", null); m.SetTexture("_MetallicGlossMap", null); m.DisableKeyword("_NORMALMAP"); m.DisableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.8f);
                    m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 1.5f); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                });
            }
            rack.gold = Metal("M_Beacon_Body", "beacon_charge", 0.5f);
        }

        static Texture2D Label(string name) => System.IO.File.Exists($"ArtSource/Cover/v6/alpha/{name}.png") ? CoverBuilder.Tex(name) : null;

        public static void Fill(ConsoleDeck deck)
        {
            deck.podCenter = Model("deck_pod_center"); deck.podLeft = Model("deck_pod_left"); deck.podRight = Model("deck_pod_right");
            deck.capPrefab = Model("deck_cap"); deck.pedestalPrefab = Model("deck_pedestal"); deck.leverPrefab = Model("deck_switch");
            deck.metal = BoardBuilder.Mat("M_Deck_Metal", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.2f, 0.21f, 0.24f)); m.SetFloat("_Metallic", 0.85f); m.SetFloat("_Smoothness", 0.55f);
                m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });
            deck.glow = BoardBuilder.Mat("M_Deck_Cap", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", Color.white); m.SetFloat("_Metallic", 0.1f); m.SetFloat("_Smoothness", 0.7f);
                m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });
            // 颜色整体压暗一点（和发黑的铁甲边框一个深浅），法线强度 0.55（再强表面发“砂”）
            var dark = new Color(0.6f, 0.62f, 0.68f);
            deck.podCenterMat = Metal("M_Deck_PodCenter", "deck_pod_center", 0.55f, dark); deck.podLeftMat = Metal("M_Deck_PodLeft", "deck_pod_left", 0.55f, dark); deck.podRightMat = Metal("M_Deck_PodRight", "deck_pod_right", 0.55f, dark);
            deck.pedestalMat = Metal("M_Deck_Pedestal", "deck_pedestal", 0.55f);
            // 按钮帽：搪瓷质感（不是金属），颜色由游戏里乘上去；贴图是中性浅灰，只带缝隙的暗和棱边的亮
            deck.capMat = Metal("M_Deck_CapTex", "deck_cap", 0.6f); deck.switchMat = Metal("M_Deck_SwitchTex", "deck_switch", 0.6f);
            deck.ringMat = BoardBuilder.Mat("M_Deck_Ring", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.08f, 0.07f, 0.06f)); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.7f);
                m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });
            // 字。11-16 晚（用户：标签要做好）：名牌 = 铭牌实体 + 全套 PBR（发蓝钢板、黄铜凸边、铆钉、刻进去填珐琅的字）；
            // 帽顶的字 = 透明的受光材质 + 法线（字是凸起的，会跟着光变）。每块牌子的贴图在运行时用属性块换
            deck.readyZh = Lab("cap_ready_zh"); deck.readyEn = Lab("cap_ready_en"); deck.fightZh = Lab("cap_fight_zh"); deck.fightEn = Lab("cap_fight_en");
            deck.immerseZh = Lab("cap_immerse_zh"); deck.immerseEn = Lab("cap_immerse_en"); deck.tableZh = Lab("cap_table_zh"); deck.tableEn = Lab("cap_table_en");
            deck.eraZh = new ConsoleDeck.Lab[Settings.Eras]; deck.eraEn = new ConsoleDeck.Lab[Settings.Eras];
            for (int i = 0; i < Settings.Eras; i++) { deck.eraZh[i] = Lab($"plate_era{i}_zh"); deck.eraEn[i] = Lab($"plate_era{i}_en"); }
            for (int i = 0; i < 3; i++) { deck.orderZh[i] = Lab($"plate_order_{OrderRack.Keys[i]}_zh"); deck.orderEn[i] = Lab($"plate_order_{OrderRack.Keys[i]}_en"); }
            deck.platePrefab = Model("deck_plate");
            // 11-17：令标换成按钮 → 右托架是三个按钮井的军令托架；帽顶的令徽中英共用
            var orders = Model("deck_pod_orders"); if (orders) { deck.podRight = orders; deck.podRightMat = Metal("M_Deck_PodOrders", "deck_pod_orders", 0.55f, dark); }
            deck.orderCap = new ConsoleDeck.Lab[3];
            for (int i = 0; i < 3; i++) deck.orderCap[i] = Lab($"cap_order_{OrderRack.Keys[i]}");
            // 小战场图（放大模式）：各风格一张俯视图（WarTable/烘战场图 菜单烘出来的 Art/Textures/Deck/minimap_<风格>.png），小点和箭头用纯色无光照材质
            deck.maps = new Texture2D[Settings.Eras];
            for (int i = 0; i < Settings.Eras; i++)
            {
                string mp = $"{TexDir}/minimap_{EraKeys[i]}.png";
                if (AssetImporter.GetAtPath(mp) is TextureImporter mi && (mi.maxTextureSize != 1024 || mi.wrapMode != TextureWrapMode.Clamp || mi.npotScale != TextureImporterNPOTScale.None))
                { mi.maxTextureSize = 1024; mi.wrapMode = TextureWrapMode.Clamp; mi.npotScale = TextureImporterNPOTScale.None; mi.sRGBTexture = true; mi.SaveAndReimport(); }
                deck.maps[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(mp);
            }
            deck.mapMat = CoverBuilder.Unlit("M_Deck_Map", deck.maps[0], Color.white, 3040);
            deck.pinMat = CoverBuilder.Unlit("M_Deck_MapPin", null, Color.white, 3045);
            var p0 = deck.eraZh[0];
            deck.plateMat = BoardBuilder.Mat("M_Deck_Plate", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", p0 ? p0.a : null); m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BumpMap", p0 ? p0.n : null); m.SetFloat("_BumpScale", 1.2f); m.EnableKeyword("_NORMALMAP");
                m.SetTexture("_MetallicGlossMap", p0 ? p0.m : null); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_SmoothnessTextureChannel", 0f); m.SetFloat("_Smoothness", 1f);
            });
            var c0 = deck.fightZh;
            deck.labelMat = BoardBuilder.Mat("M_Deck_CapLabel", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", c0 ? c0.a : null); m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BumpMap", c0 ? c0.n : null); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.78f);
                // 透明混合（贴图自带透明度），不写深度，画在帽子后面
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3050;
                m.SetShaderPassEnabled("ShadowCaster", false); m.SetShaderPassEnabled("DepthOnly", false);
            });
        }

        public static void FillStage(ImmersiveStage st, Transform board)
        {
            // 11-17：放大模式改成真人比例 + 真实房间，不要天空盒了 → 舞台只需要切换时的黑片（天空贴图、天球、延伸大地都不再进游戏）
            st.fadeMat = CoverBuilder.Unlit("M_ImmersiveFade", null, Color.black, 4900);
            st.fadeMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            var old = board.Find("Surround"); if (old) Object.DestroyImmediate(old.gameObject);
        }
    }
}
