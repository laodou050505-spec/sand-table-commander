using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarTable.EditorTools
{
    /// <summary>
    /// 布景件（P2）：导入成预制体并摆到沙盘上。坐标是 Unity 码（玩家看过去 +X 在左手边）。
    /// 大件只放在左右布景带和两军身后的边上，不进两军布阵的区域。
    /// 菜单 WarTable/4 摆放布景。
    /// </summary>
    public static class PropBuilder
    {
        public const string Root = "Props";

        /// <summary>每种布景件的高度（米，沙盘本地；整盘再乘 BoardScale）。</summary>
        public static readonly Dictionary<string, float> Heights = new()
        {
            ["Tree_Oak_A"] = 0.078f, ["Tree_Oak_B"] = 0.068f, ["Tree_Pine_A"] = 0.088f, ["Tree_Pine_B"] = 0.092f, ["Tree_Dead"] = 0.058f, ["Bush_A"] = 0.018f,
            ["Rock_A"] = 0.032f, ["Rock_B"] = 0.016f, ["G_Chapel"] = 0.088f, ["G_Cottage_A"] = 0.064f, ["G_ClockTower"] = 0.105f, ["F_Spire"] = 0.092f, ["F_Habitat"] = 0.034f, ["F_Relay"] = 0.07f, ["F_Crates"] = 0.02f, ["F_Hive"] = 0.062f, ["Cottage_B"] = 0.044f, ["Cottage_C"] = 0.046f, ["Chapel"] = 0.068f, ["Windmill"] = 0.085f,
            ["Bridge_Stone"] = 0.024f, ["Watchtower_Ruin"] = 0.095f, ["Tent_Kingdom_B"] = 0.046f,
            ["Tent_Clan_A"] = 0.04f, ["Tent_Clan_B"] = 0.062f, ["Wagon_Supply"] = 0.024f, ["Haystack"] = 0.02f, ["Well"] = 0.024f,
            ["Campfire"] = 0.012f, ["Market_Stall"] = 0.028f, ["Barrels_Crates"] = 0.013f, ["Weapon_Rack"] = 0.026f, ["Archery_Target"] = 0.022f,
            ["Sheep_Flock"] = 0.013f, ["Jetty_Boat"] = 0.014f, ["Woodpile"] = 0.015f, ["Standing_Stones"] = 0.03f, ["Palisade_Clan"] = 0.04f,
            ["Wall_Stone"] = 0.011f,
            // 11-12 用户：“注意别把场景做得太空” → 未来时代被藏掉的中古布景都有未来版顶上：
            // 程序建模（Tools/make_future_crystals.py）：月面水晶簇（冰蓝 / 紫）、能量信标；P2：太阳能板阵、货运车、储罐、能量围栏、灯塔、充能架、蜂群骨刺
            ["F_Crystal_A"] = 0.05f, ["F_Crystal_B"] = 0.02f, ["F_Crystal_A_V"] = 0.05f, ["F_Crystal_B_V"] = 0.02f, ["F_Beacon"] = 0.017f, ["F_Beacon_V"] = 0.017f,
            // 现代时代（11-13）
            ["M_Command"] = 0.05f, ["M_Barracks"] = 0.034f, ["M_Watchtower"] = 0.078f, ["M_Crates"] = 0.014f, ["M_Truck"] = 0.027f, ["M_Sandbags"] = 0.011f,
            ["M_Container"] = 0.05f, ["M_Barricade"] = 0.016f, ["M_Target"] = 0.02f, ["M_Generator"] = 0.022f,
            ["M_Tent_Sand"] = 0.03f, ["M_Tent_Blue"] = 0.03f, ["M_Pickup"] = 0.021f,   // 军帐（替骨帐 / 条纹圆帐）、赤砂皮卡（替兽骨图腾）
            // 深空风格（11-13，样图 C）
            ["S_Command"] = 0.05f, ["S_Hab"] = 0.032f, ["S_Tower"] = 0.085f, ["S_Pod"] = 0.04f, ["S_Hauler"] = 0.026f, ["S_Crates"] = 0.016f, ["S_Barrier"] = 0.012f, ["S_Lamp"] = 0.036f,
            ["S_Wreck"] = 0.058f, ["S_Ribs"] = 0.03f, ["S_Derrick"] = 0.085f, ["S_Shack"] = 0.032f, ["S_ScrapWall"] = 0.03f, ["S_OreCart"] = 0.02f, ["S_Spire"] = 0.055f,   // 舰体、肋骨、岩柱的占地比原件大，按量出来的占地缩过（原件脚下的平台放不下会悬空）
            ["F_Rock_A"] = 0.032f, ["F_Rock_B"] = 0.016f, ["F_Standing_Stones"] = 0.03f,   // 去苔藓的玄武岩（Tools/make_future_rocks.py）
            ["F_Solar"] = 0.016f, ["F_Rover"] = 0.019f, ["F_Tank"] = 0.03f, ["F_Barrier"] = 0.012f, ["F_Pylon"] = 0.042f, ["F_Rack"] = 0.022f, ["F_Spikes"] = 0.034f,
        };

        public struct Place
        {
            public string id; public float x, z, yaw, scale;
            public Place(string id, float x, float z, float yaw, float scale = 1f) { this.id = id; this.x = x; this.z = z; this.yaw = yaw; this.scale = scale; }
        }

        /// <summary>
        /// 摆放表（码；yaw：度，0 = 模型正面朝 +Z 即朝玩家）。用户要求“组合摆放”：一组一组的小场景（房子旁边有柴堆和木桶、
        /// 树三两成丛脚下有灌木石头、营帐围着篝火），不要一件一件均匀撒开。布阵区（|x|≤40，|z|≤40）不放大件。
        /// 摆完跑 <see cref="ClipAudit"/> 查互相穿插。
        /// </summary>
        /// <summary>哥特时代换掉的布景（11-11 用户：不同时代的场地要有简单变化、有自己风格的建筑）：原件 → 哥特版（同一位置、同一朝向）。
        /// 橡树另按“隔一棵换一棵枯树”处理。哥特版的模型还没做好（预制体不存在）就不摆，原件照常显示。</summary>
        public static readonly Dictionary<string, string> Gothic = new()
        {
            ["Chapel"] = "G_Chapel", ["Cottage_B"] = "G_Cottage_A", ["Cottage_C"] = "G_Cottage_A", ["Windmill"] = "G_ClockTower",
        };

        /// <summary>
        /// 现代时代换掉的布景（11-13，样图 A：蓝盾特勤 对 赤砂佣兵）。现代和中古是同一片乡野——树、灌木、岩石、草垛、羊群、柴堆、石桥、小船、
        /// 篝火都留着，只把“中古的人造物”换成现代军事设施（每一类都有替换件，场景不变空，见 scene-not-empty 那条教训）：
        /// 教堂 → 指挥所，农舍 → 板房营房，风车 → 岗楼，木桶货箱 / 武器架 → 弹药箱油桶，马车 → 军用卡车，石墙 → 沙袋墙，
        /// 瞭望塔废墟 → 生锈集装箱，木栅栏 → 路障，箭靶 → 靶架，货摊 / 水井 → 发电机拖车。模型还没做好的（预制体不存在）自动不摆、原件照常显示。
        /// </summary>
        public static readonly Dictionary<string, string> Modern = new()
        {
            ["Chapel"] = "M_Command", ["Cottage_B"] = "M_Barracks", ["Cottage_C"] = "M_Barracks",
            ["Barrels_Crates"] = "M_Crates", ["Weapon_Rack"] = "M_Crates", ["Wagon_Supply"] = "M_Truck", ["Wall_Stone"] = "M_Sandbags",
            ["Palisade_Clan"] = "M_Barricade", ["Archery_Target"] = "M_Target", ["Market_Stall"] = "M_Generator", ["Well"] = "M_Generator",
            // 11-13 用户：“注意地面的样子和场景的布置”。第一版只是一对一原地替换，结果：长条板房照着农舍的朝向歪七扭八、压着柴堆；
            // 集装箱摞在岩丘顶上；沙袋墙里圈着羊；两军营地还是条纹圆帐、骨帐和兽骨图腾。改成按“军营”重新布置（改位见 ModernSpot）：
            // 岩丘顶 → 岗楼（制高点哨位），风车 → 集装箱（麦田边的物资点），羊圈（沙袋围着）里的羊 → 弹药箱（弹药堆放场），
            // 骨帐 / 条纹圆帐 / 帅帐 → 军帐，兽骨图腾 → 赤砂皮卡
            ["Watchtower_Ruin"] = "M_Watchtower", ["Windmill"] = "M_Container", ["Sheep_Flock"] = "M_Crates",
            ["Tent_Clan_A"] = "M_Tent_Sand", ["Tent_Clan_B"] = "M_Pickup", ["Tent_Kingdom_B"] = "M_Tent_Blue", ["proc:pavilion"] = "M_Tent_Blue",
        };
        /// <summary>现代版孪生件相对原件的缩放（ModernSpot 里写了的以那边为准）。</summary>
        static readonly Dictionary<string, float> ModernScale = new() { ["Weapon_Rack"] = 0.85f };
        /// <summary>现代模型自带的底板埋进地里多深（码，模型原始比例）。参考图都带了一块浅色垫板，摆在草地上像个白托盘（11-13 近拍查出）：
        /// 车辆和小件（卡车 / 集装箱 / 路障 / 靶架 / 发电机 / 弹药箱 / 沙袋）按量出来的厚度整块埋掉；
        /// 房子（指挥所 / 板房 / 岗楼）的底板是水泥地基，埋掉大半、只露一道边。数是 /tmp/sp2/m_slab.cs 那种“逐层量占地”量出来的，换了模型要重量。</summary>
        static readonly Dictionary<string, float> ModernSink = new()
        {
            ["M_Truck"] = 0.12f, ["M_Container"] = 0.11f, ["M_Barricade"] = 0.07f, ["M_Target"] = 0.06f, ["M_Generator"] = 0.09f, ["M_Crates"] = 0.04f, ["M_Sandbags"] = 0.06f,
            ["M_Barracks"] = 0.07f, ["M_Command"] = 0.1f, ["M_Watchtower"] = 0.08f,
        };
        /// <summary>模型自己的朝向补偿（度）：卡车在模型里是斜着 45° 停的（车头朝 −X+Z）；写摆放时 yaw 一律按“车头朝向”（0 = 朝 +Z、90 = 朝 +X），这里补上。</summary>
        static readonly Dictionary<string, float> ModernYawFix = new() { ["M_Truck"] = 45f };

        /// <summary>
        /// 现代时代孪生件的改位（键 = 原件名和它在摆放表里的坐标；值 = 现代版放哪、朝哪、多大；id 写 "=" 就是按 Modern 表换，写 "" 就是这一处什么都不放）。
        /// 布置思路（坐标是码，长条的东西 yaw 0 = 长边顺着 X、90 = 顺着 Z）：
        /// · 右边村子 → 前进营地：三栋板房长边都顺着 Z、平行排在两条岔路之间（一对一替换时朝向是 220° / 120° / 160°，乱且压柴堆）；
        ///   指挥所摆正、紧挨大路口；发电机拖车靠着房子、不站在路中间（原来水井的位置在路口正中）；卡车顺着大路停在路边（旧马车脚下那块小平台上，别挪远——别处地面不平）。
        /// · 我军营地：军帐排成一排、长边顺着盘边；第二个兵器架换成照明发电机（不然一排全是弹药箱）；卡车顺着盘边停。
        /// · 敌军营地：军帐略微歪着摆（佣兵营地不那么齐整）；两头的路障从帐篷背后挪到营地前沿两角（朝着战场才叫路障）。
        /// </summary>
        static readonly Dictionary<(string, float, float), Place> ModernSpot = new()
        {
            // 前进营地
            [("Chapel", -55.2f, -3.6f)] = new("=", -55.6f, -4.3f, 0f),
            [("Well", -52.2f, 2.0f)] = new("=", -56.5f, 0.4f, 0f),
            [("Market_Stall", -45.5f, 9f)] = new("=", -41.7f, 10.2f, 90f),
            [("Barrels_Crates", -42f, 11.6f)] = new("=", -41.7f, 13.6f, 0f),
            [("Cottage_B", -45.5f, 16f)] = new("=", -45.8f, 13.8f, 90f),
            [("Cottage_C", -56f, 12f)] = new("=", -56.8f, 11.0f, 90f),
            [("Woodpile", -57.5f, 6.5f)] = new("", 0f, 0f, 0f),                       // 让位给板房
            [("Cottage_B", -44.5f, -7.5f)] = new("=", -44.9f, -7.3f, 90f),
            [("Woodpile", -41.2f, -12.2f)] = new("M_Crates", -41.3f, -13.8f, 40f, 0.85f),   // 板房带地基后比农舍长，原来的柴堆挤不下 → 换成一小堆弹药箱、往南让一点
            [("Wagon_Supply", -43f, -1f)] = new("=", -42.9f, -0.8f, 270f),            // 车头朝营地里（−X）
            [("Wall_Stone", -52.6f, -19.75f)] = new("", 0f, 0f, 0f),                 // 山坡上这一段放不平（未来时代也没放）：留成弹药场的入口
            [("Sheep_Flock", -56.3f, -17f)] = new("=", -56.3f, -17f, 20f, 1.3f),
            [("Sheep_Flock", -55.8f, -23.5f)] = new("=", -55.8f, -23.5f, 100f, 1.2f),
            [("Watchtower_Ruin", -50f, -35f)] = new("=", -50f, -35f, 180f, 1.15f),
            [("Windmill", -56f, 31.5f)] = new("=", -56.2f, 31.5f, 0f),
            // 我军营地
            [("Tent_Kingdom_B", -8f, 45.5f)] = new("=", -7.6f, 45.5f, 0f, 1.2f),
            [("proc:pavilion", -19.5f, 45.6f)] = new("=", -19.3f, 45.6f, 0f),
            [("proc:pavilion", -27f, 45.2f)] = new("=", -27.5f, 45.5f, 0f),
            [("proc:pavilion", 11.5f, 46.6f)] = new("=", 11.5f, 46.6f, 0f),
            [("Weapon_Rack", 13.6f, 42.4f)] = new("M_Generator", 13.8f, 42.4f, 0f),
            [("Wagon_Supply", 21.5f, 43.8f)] = new("=", 22.4f, 44.3f, 90f),          // 车头朝 +X，顺着盘边
            // 敌军营地
            [("Tent_Clan_A", -9.5f, -45f)] = new("=", -10.6f, -45.2f, -6f),
            // 这一顶骨帐脚下是个小土包（x −30…−14 一带落差近 1 码），6 码长的军帐四角悬空 1 cm（巡检查出）→ 换成一摞生锈集装箱（样图 A 里赤砂这边就有），缩到土台放得下
            [("Tent_Clan_A", -21.5f, -44.3f)] = new("M_Container", -21.5f, -44.3f, 8f, 0.85f),
            [("Tent_Clan_A", 14f, -45f)] = new("=", 13.8f, -44.9f, 6f),
            [("Tent_Clan_A", 28f, -43.4f)] = new("=", 28f, -44.2f, -5f, 0.95f),
            [("Tent_Clan_B", 2f, -44.5f)] = new("=", 1.3f, -45.3f, 100f),
            [("Palisade_Clan", -31f, -47.6f)] = new("=", -32.5f, -41.4f, 12f),
            [("Palisade_Clan", 31f, -47.6f)] = new("=", 32.8f, -40.4f, -10f),
        };
        /// <summary>现代时代另外加的布景（只在现代出现）：岩丘哨位的沙袋、麦田物资点的弹药箱、石桥桥头的检查站（路障 + 沙袋，错开摆在路两边）。</summary>
        static readonly Place[] ModernExtra =
        {
            new("M_Sandbags", -50f, -32.2f, 0f), new("M_Sandbags", -52.8f, -35f, 90f),
            new("M_Crates", -52.6f, 28.8f, 30f),
            new("M_Barricade", 41.4f, 9.3f, 5f), new("M_Sandbags", 41.4f, 2.3f, -8f),
        };

        /// <summary>
        /// 深空风格（下标 4，11-13 用户选样图 C：星枪团 对 余烬矿帮，小行星战场）换掉的布景。近侧和右侧是星枪团的前进基地（钢蓝 + 白搪瓷），
        /// 远侧是余烬矿帮的矿场（焦红 + 炭黑、琥珀灯）；远角岩丘上是一段坠毁的舰体。每一类被藏掉的中古布景都有替换件（场景不能空）。
        /// 岩石 / 立石阵沿用未来风格去苔藓的玄武岩版；地面碎石、地灯、无人机也和未来共用（EraOnly.alsoIn）。
        /// 远侧（z &lt; −15）换成矿帮的版本见 SpaceRedSide；模型还没做好的（预制体不存在）自动不摆。
        /// </summary>
        public static readonly Dictionary<string, string> Space = new()
        {
            ["Chapel"] = "S_Command", ["Cottage_B"] = "S_Hab", ["Cottage_C"] = "S_Hab", ["Windmill"] = "S_Tower", ["Watchtower_Ruin"] = "S_Wreck",
            ["Tent_Kingdom_B"] = "S_Pod", ["proc:pavilion"] = "S_Pod", ["Tent_Clan_A"] = "S_Shack", ["Tent_Clan_B"] = "S_OreCart", ["Palisade_Clan"] = "S_ScrapWall",
            ["Wagon_Supply"] = "S_Hauler", ["Barrels_Crates"] = "S_Crates", ["Weapon_Rack"] = "S_Crates", ["Market_Stall"] = "S_Crates", ["Woodpile"] = "S_Crates", ["Wall_Stone"] = "S_Barrier",
            ["Tree_Oak_A"] = "S_Lamp", ["Tree_Oak_B"] = "S_Lamp", ["Well"] = "S_Lamp", ["Archery_Target"] = "S_Lamp", ["Campfire"] = "S_Lamp",
            ["Tree_Pine_A"] = "S_Spire", ["Tree_Pine_B"] = "S_Spire", ["Haystack"] = "S_Spire", ["Tree_Dead"] = "S_Ribs", ["Sheep_Flock"] = "S_OreCart", ["Bush_A"] = "F_Rock_B",
            ["Rock_A"] = "F_Rock_A", ["Rock_B"] = "F_Rock_B", ["Standing_Stones"] = "F_Standing_Stones",
        };
        /// <summary>最远一排（z &lt; −38，矿帮的营地）：星枪团的灯柱 / 货箱在那边换成残骸肋骨 / 矿车 / 岩柱。</summary>
        static readonly Dictionary<string, string> SpaceRedSide = new() { ["S_Lamp"] = "S_Ribs", ["S_Crates"] = "S_OreCart" };
        /// <summary>个别位置另外指定（键 = 原件名和摆放表坐标）：矿帮营地的篝火 → 一小车发光的余烬矿（顶替火堆）；立石圈那个小丘上立钻井架；"" = 这一处不放。</summary>
        static readonly Dictionary<(string, float, float), string> SpaceAt = new()
        {
            [("Campfire", -15.5f, -43f)] = "S_OreCart", [("Campfire", 19.5f, -42.5f)] = "S_OreCart",
            // 钻井架立在矿帮营地正中（顶替兽骨图腾）：那里地面平。立石圈的小丘上放不平（四条腿悬空 2 cm，巡检查出）
            [("Tent_Clan_B", 2f, -44.5f)] = "S_Derrick", [("Woodpile", 18.4f, -45.4f)] = "",
            [("Tent_Clan_A", -21.5f, -44.3f)] = "S_Spire",   // 这一处脚下是个土包（现代风格也是因为放不平换了件）：棚屋会斜着半埋 → 一簇岩柱
            [("Wall_Stone", -52.6f, -19.75f)] = "",     // 山坡上这一段放不平（未来 / 现代也没放）
        };
        /// <summary>深空风格里沿用 ModernSpot 位置 / 朝向的原件。</summary>
        static readonly HashSet<string> SpaceSpotFrom = new() { "Chapel", "Cottage_B", "Cottage_C", "Well", "Market_Stall", "Barrels_Crates" };
        /// <summary>深空版孪生件相对原件的缩放（按原件名）；SpaceIdScale 按深空件名（不管顶替的是谁）。</summary>
        static readonly Dictionary<string, float> SpaceScale = new() { ["Haystack"] = 0.5f, ["Campfire"] = 0.55f, ["Well"] = 0.8f, ["Archery_Target"] = 0.7f, ["Woodpile"] = 0.8f, ["Sheep_Flock"] = 0.9f, ["Bush_A"] = 1.4f };

        /// <summary>未来时代换掉的布景（11-12，样图 A：白色陶瓷 + 天青光）：教堂 → 指挥塔，农舍 → 居住舱，风车 → 通讯站。</summary>
        /// <summary>
        /// 未来时代孪生件的改位（键 = 原件名和它在摆放表里的坐标）：氏族营地后沿的四段木栅栏贴着木框、夹在帐篷后面，
        /// 蜂巢塔是胖柱子，原位会探出木框 / 压到帐篷底座（穿模巡检查出）→ 外侧两段挪到营地两角，内侧两段不放。null = 这一处不放。
        /// </summary>
        static readonly Dictionary<(string, float, float), (float x, float z)?> FutureSpot = new()
        {
            [("Palisade_Clan", -31f, -47.6f)] = (-33f, -46.8f), [("Palisade_Clan", 31f, -47.6f)] = (33f, -46.8f),
            [("Palisade_Clan", -23.5f, -48.6f)] = null, [("Palisade_Clan", 23.5f, -48f)] = null,
            // 羊圈石墙在山坡上的那一段：地面落差近 1 cm，笔直的能量围栏光板放不平（一半悬空，巡检查出）→ 不放，留成围栏的缺口
            [("Wall_Stone", -52.6f, -19.75f)] = null,
        };

        public static readonly Dictionary<string, string> Future = new()
        {
            ["Chapel"] = "F_Spire", ["Cottage_B"] = "F_Habitat", ["Cottage_C"] = "F_Habitat", ["Windmill"] = "F_Relay",
            // 11-12 用户：未来时代的场景要相应地改 → 木桶货箱 → 货运集装箱；敌军的木栅栏、瞭望塔废墟 → 黑色六边形蜂巢塔（样图 A）
            ["Barrels_Crates"] = "F_Crates", ["Palisade_Clan"] = "F_Hive", ["Watchtower_Ruin"] = "F_Hive",
            // 11-12 用户：“注意别把场景做得太空” → 原来只是藏起来的那 53 件也各有未来版（没做出来的预制体自动跳过，仍旧只是藏）
            ["Tree_Pine_A"] = "F_Crystal_A", ["Tree_Pine_B"] = "F_Crystal_A", ["Tree_Oak_A"] = "F_Pylon", ["Tree_Oak_B"] = "F_Pylon", ["Tree_Dead"] = "F_Spikes", ["Bush_A"] = "F_Crystal_B",
            ["Haystack"] = "F_Tank", ["Sheep_Flock"] = "F_Solar", ["Wall_Stone"] = "F_Barrier", ["Woodpile"] = "F_Crates", ["Wagon_Supply"] = "F_Rover", ["Market_Stall"] = "F_Tank",
            ["Well"] = "F_Pylon", ["Archery_Target"] = "F_Rack", ["Weapon_Rack"] = "F_Rack", ["Campfire"] = "F_Beacon",
            // 岩石和立石阵留着，但中古贴图上有绿苔藓，月面上不协调 → 换成去苔藓的玄武岩版（同一个模型）
            ["Rock_A"] = "F_Rock_A", ["Rock_B"] = "F_Rock_B", ["Standing_Stones"] = "F_Standing_Stones",
        };
        /// <summary>虚空蜂群那半边场地（z &lt; −15）换成蜂群的版本：紫水晶、紫光信标；光棱的白货箱 / 灯塔在那边换成骨刺 / 紫水晶。</summary>
        static readonly Dictionary<string, string> FutureSwarmSide = new()
        {
            ["F_Crystal_A"] = "F_Crystal_A_V", ["F_Crystal_B"] = "F_Crystal_B_V", ["F_Beacon"] = "F_Beacon_V", ["F_Crates"] = "F_Spikes", ["F_Pylon"] = "F_Crystal_A_V", ["F_Tank"] = "F_Spikes",
        };
        /// <summary>未来版孪生件相对原件的缩放（原件比较小的：柴堆 → 货箱）。</summary>
        static readonly Dictionary<string, float> FutureScale = new() { ["Woodpile"] = 0.7f, ["Sheep_Flock"] = 1.15f };
        /// <summary>未来时代藏起来的布景（名字前缀）：月面基地上没有树、灌木、草垛、羊群、柴堆、马车、货摊、水井、箭靶、武器架、篝火、小船、石墙。
        /// 岩石、立石阵、石桥留着。散布的草丛（Scatter）、篝火的烟和火、飞鸟（Ambient）也一起藏，见 Build 末尾。</summary>
        public static readonly string[] FutureHidden = { "Tree_", "Bush_", "Haystack", "Sheep_", "Woodpile", "Wagon_", "Market_", "Well", "Archery_", "Weapon_Rack", "Campfire", "Jetty_", "Wall_Stone" };

        public static readonly Place[] Layout =
        {
            // ── 右侧布景带：村庄（玩家右手边）
            // 村中空地：水井 + 货摊 + 木桶 + 货车
            new("Well", -52.2f, 2.0f, 0f), new("Market_Stall", -45.5f, 9f, 200f), new("Barrels_Crates", -42f, 11.6f, 35f), new("Wagon_Supply", -43f, -1f, 30f),
            // 农舍三座，各带小物件
            new("Cottage_B", -44.5f, -7.5f, 220f, 0.95f), new("Woodpile", -41.2f, -12.2f, 130f, 0.85f), new("Cottage_B", -45.5f, 16f, 120f), new("Cottage_C", -56f, 12f, 160f),
            new("Woodpile", -57.5f, 6.5f, 90f), new("Haystack", -56f, 17.5f, 0f, 0.9f),
            // 小教堂 + 院墙 + 老橡树
            new("Chapel", -55.2f, -3.6f, 75f), new("Wall_Stone", -53.2f, -9.5f, 0f), new("Bush_A", -58.4f, -9.6f, 40f, 0.55f),
            // 牧场：石墙沿去塔的小路，羊群在墙后
            // 石墙一段 3.68 码 ×1.5 = 5.5 码，首尾相接围成羊圈（西边靠木框）
            new("Wall_Stone", -52.6f, -14.1f, 90f, 1.5f), new("Wall_Stone", -52.6f, -19.75f, 90f, 1.5f), new("Wall_Stone", -52.6f, -25.25f, 90f, 1.5f),
            new("Wall_Stone", -56.3f, -12.3f, 0f, 1.5f), new("Wall_Stone", -56.3f, -27.2f, 180f, 1.5f),
            new("Sheep_Flock", -56.3f, -17f, 30f), new("Sheep_Flock", -55.8f, -23.5f, 150f, 0.9f),
            new("Haystack", -44f, -15f, 40f, 0.85f), new("Tree_Oak_A", -44f, -22f, 0f, 0.85f), new("Bush_A", -41.6f, -18.2f, 60f, 0.7f),
            // 远角岩丘：废塔 + 乱石 + 松树
            new("Watchtower_Ruin", -50f, -35f, 200f), new("Rock_A", -41.8f, -29.5f, 30f), new("Rock_B", -57.4f, -31.8f, 10f), new("Rock_B", -47.5f, -43.5f, 160f),
            new("Tree_Pine_A", -55f, -44f, 0f), new("Tree_Pine_B", -43f, -45f, 70f, 0.8f),
            // 近角：风车 + 三块麦田（ScatterBuilder.Fields）+ 草垛
            new("Windmill", -56f, 31.5f, 20f), new("Haystack", -50.2f, 32f, 0f), new("Tree_Oak_B", -42.5f, 46f, 30f, 0.8f),

            // ── 左侧布景带：河、桥、渡口、对岸树林
            new("Bridge_Stone", 48.9f, 6f, 0f, 1.3f),
            // 渡口：栈桥小船 + 岸上货箱 + 灌木
            new("Jetty_Boat", 45.6f, 22f, 90f), new("Barrels_Crates", 42.6f, 24f, 110f), new("Bush_A", 42.3f, 19.5f, 0f, 0.7f),
            // 本岸的老橡树丛
            new("Tree_Oak_A", 38.3f, -20.5f, 0f), new("Rock_B", 40.6f, -25.5f, 70f, 0.6f),
            new("Tree_Oak_B", 40.8f, 37f, 60f, 0.85f),
            // 对岸：桥头伐木点
            new("Woodpile", 55f, 11f, 200f), new("Tree_Pine_A", 56f, 16.5f, 20f, 0.8f), new("Bush_A", 55f, -5.5f, 0f, 0.75f), new("Rock_B", 55.5f, -10f, 120f, 0.75f),
            // 对岸远处：松林坡
            new("Tree_Pine_B", 54f, -36f, 0f), new("Tree_Pine_A", 51.5f, -28f, 40f, 0.9f), new("Tree_Oak_B", 55f, -21f, 120f, 0.9f),
            new("Rock_A", 51f, -42f, 30f, 0.8f), new("Bush_A", 50.5f, -17.5f, 0f, 0.8f), new("Tree_Pine_A", 56.5f, -46f, 80f, 0.8f),
            new("Tree_Pine_B", 56.8f, -13.5f, 200f, 0.8f), new("Rock_B", 58f, -17.6f, 40f, 0.6f),
            // 对岸近处：小丘上的立石圈，松树环绕
            new("Standing_Stones", 53f, 36f, 0f), new("Tree_Pine_A", 56.5f, 44.5f, 0f, 0.85f), new("Tree_Pine_B", 56f, 27.5f, 60f, 0.8f), new("Bush_A", 52f, 31f, 90f, 0.7f),

            // ── 我军营地（近侧，玩家面前的边上）
            // 帅帐 + 军旗 + 门口的货箱
            new("Tent_Kingdom_B", -8f, 45.5f, 180f), new("std:kingdom", 2.6f, 43.4f, 22f), new("Barrels_Crates", -13.6f, 47.2f, 150f, 0.9f),
            // 营帐围着篝火，篝火边一堆柴
            new("proc:pavilion", -19.5f, 45.6f, 165f), new("proc:pavilion", -27f, 45.2f, 195f, 0.92f), new("Campfire", -23.2f, 41.8f, 0f),
            new("Woodpile", -23.3f, 48.2f, 180f, 0.85f), new("Barrels_Crates", -31.8f, 42.6f, 60f, 0.85f),
            // 军械处：两个兵器架、营帐、补给车挤在一起
            new("Weapon_Rack", 8.6f, 42.6f, 180f), new("Weapon_Rack", 13.6f, 42.4f, 170f, 0.95f), new("proc:pavilion", 11.5f, 46.6f, 190f, 0.95f),
            new("Barrels_Crates", 17.2f, 46.2f, 20f), new("Wagon_Supply", 21.5f, 43.8f, 100f),
            // 小坡上的箭靶场：两组靶子 + 草垛
            new("Archery_Target", 28.5f, 45.5f, 180f), new("Haystack", 32.8f, 47f, 0f, 0.8f), new("Barrels_Crates", 33.6f, 43.4f, 80f, 0.75f),
            new("Tree_Oak_A", -36.5f, 46f, 80f, 0.8f), new("Bush_A", -39.5f, 41.8f, 30f, 0.7f),

            // ── 敌军营地（远侧）
            new("Tent_Clan_B", 2f, -44.5f, 0f), new("std:clan", -4.5f, -43.2f, 22f),
            new("Tent_Clan_A", -9.5f, -45f, -15f), new("Campfire", -15.5f, -43f, 0f), new("Tent_Clan_A", -21.5f, -44.3f, 10f, 0.95f),
            new("Tent_Clan_A", 14f, -45f, 10f), new("Campfire", 19.5f, -42.5f, 0f), new("Woodpile", 18.4f, -45.4f, 20f),
            new("Palisade_Clan", -31f, -47.6f, 0f), new("Palisade_Clan", -23.5f, -48.6f, 0f), new("Palisade_Clan", 23.5f, -48f, 0f), new("Palisade_Clan", 31f, -47.6f, 0f),
            new("Tent_Clan_A", 28f, -43.4f, 20f, 0.9f), new("Woodpile", -27.5f, -43f, 0f, 0.85f), new("Rock_B", 9.5f, -48f, 0f, 0.7f),
            new("Tree_Dead", -37f, -43.5f, 30f), new("Tree_Dead", 37.5f, -42.5f, 200f, 0.9f), new("Rock_A", 37.8f, -47.8f, 120f, 0.6f),
        };

        [MenuItem("WarTable/4 摆放布景")]
        public static void Build()
        {
            // 11-13 查清的一件事（别再“修”它）：布景脚下的平台数据 BoardMap.Pads 是静态变量，只有“1 搭沙盘”会算，重新编译脚本后就是空的；
            // 所以一直以来（包括提交版 v0.2.1112，用 git 里的场景核对过）原件的落点都是按“没压平的原始地形”取的——坡上的原件比自己的平台低
            // （瞭望塔低 45 mm、营地马车低 8 mm），用户审过、定稿的就是这个样子。带上平台数据再摆，瞭望塔会被旁边岩石的低平台拉到山脚、整座埋进山里。
            // → 原件和哥特替换件：明确按“不带平台数据”摆，和定稿逐件一致；未来时代的替换件是新做的，按真实地面（带平台）落地，见下面 Ground()。
            var pads = ComputePads();
            BoardMap.Pads = null;
            float Ground(float gx, float gz) { BoardMap.Pads = pads; float gh = BoardMap.Height(gx, gz); BoardMap.Pads = null; return gh; }
            foreach (var kv in Heights) WTArt.ImportP2(kv.Key, kv.Value);
            ProcProps.ResetCache();
            var board = GameObject.Find(BoardBuilder.Root);
            var old = board ? board.transform.Find(Root) : null;
            if (old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(Root).transform;
            if (board) root.SetParent(board.transform, false);
            int n = 0, miss = 0, oaks = 0;
            foreach (var p in Layout)
            {
                GameObject go;
                if (p.id == "proc:pavilion")
                {
                    // 程序化圆帐（P2 版帐壁贴图一直碎，见 ProcProps）
                    go = ProcProps.Pavilion(root);
                    go.transform.localRotation = Quaternion.Euler(0, p.yaw, 0);
                    go.transform.localScale = Vector3.one * p.scale;
                }
                else if (p.id.StartsWith("std:"))
                {
                    // 帅旗：程序搭的旗杆 + 飘动旗面（yaw 统一 22°，旗面顺着和篝火烟一样的风向）
                    go = StandardBuilder.Build(p.id.Substring(4), root);
                    go.transform.localRotation = Quaternion.Euler(0, p.yaw, 0);
                    go.transform.localScale = Vector3.one * p.scale;
                }
                else if (p.id.StartsWith("demo:"))
                {
                    // 原型里的道具（和角色同一套纹章，例如两军军旗）：单位是码，按棋子比例缩放
                    var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{DemoFigures.Dir}/props/{p.id.Substring(5)}.fbx");
                    if (!src) { miss++; continue; }
                    // FBX 根节点自带旋转 / 缩放（原型 glb 的根变换）：包一层外壳，外壳负责摆放，根节点保持原样
                    go = new GameObject(p.id.Substring(5));
                    go.transform.SetParent(root, false);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, go.transform);
                    var mat = BoardBuilder.Mat($"M_Demo_{p.id.Substring(5)}", "WarTable/MiniLit");
                    var texDir = $"{DemoFigures.Dir}/props/{p.id.Substring(5)}_tex";
                    if (System.IO.Directory.Exists(texDir))
                        foreach (var f in System.IO.Directory.GetFiles(texDir, "Color_*.png")) mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(f));
                    foreach (var rr0 in inst.GetComponentsInChildren<Renderer>()) rr0.sharedMaterial = mat;
                    go.transform.localRotation = Quaternion.Euler(0, p.yaw, 0);
                    go.transform.localScale = Vector3.one * DemoFigures.Scale * p.scale;
                }
                else
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{WTArt.PrefabDir}/{p.id}.prefab");
                    if (!pf) { miss++; continue; }
                    go = (GameObject)PrefabUtility.InstantiatePrefab(pf, root);
                    go.transform.localRotation = Quaternion.Euler(0, p.yaw, 0);
                    go.transform.localScale = Vector3.one * p.scale;
                }
                // 落地：脚印范围内取地面最低点再压 1 mm，坡上不悬空
                var b = go.GetComponentInChildren<Renderer>() ? Bounds(go) : new Bounds(Vector3.zero, Vector3.one * 0.02f);
                float r = p.id.StartsWith("std:") ? 2.2f : p.id == "proc:pavilion" ? 2.3f : Mathf.Max(b.extents.x, b.extents.z) / BoardMap.Yard * 0.8f;
                float y = float.MaxValue;
                for (int k = 0; k < 13; k++)
                {
                    float a = k * Mathf.PI * 2 / 12, rr = k == 12 ? 0 : r;
                    y = Mathf.Min(y, BoardMap.Height(p.x + Mathf.Cos(a) * rr, p.z + Mathf.Sin(a) * rr));
                }
                // 石桥：桥墩落在两岸（不是沉到河床）
                if (p.id == "Bridge_Stone") y = Mathf.Min(BoardMap.Height(p.x - r, p.z), BoardMap.Height(p.x + r, p.z));
                // 栈桥小船：船底吃水在水面下一点
                if (p.id == "Jetty_Boat") y = BoardMap.WaterLevel - 0.35f;
                go.transform.localPosition = new Vector3(p.x * BoardMap.Yard, y * BoardMap.Yard - 0.001f, p.z * BoardMap.Yard);
                n++;
                // 各时代的孪生件（11-12 扩成多时代）：哥特 = 1，未来 = 3；未来时代另外藏掉树 / 灌木 / 草垛 / 羊群（月面上没有）
                string gid = Gothic.TryGetValue(p.id, out var g0) ? g0 : p.id.StartsWith("Tree_Oak") && (oaks++ % 2 == 0) ? "Tree_Dead" : null;
                string fid = Future.TryGetValue(p.id, out var f0) ? f0 : null;
                if (fid != null && p.z < -15f && FutureSwarmSide.TryGetValue(fid, out var fsw) && AssetDatabase.LoadAssetAtPath<GameObject>($"{WTArt.PrefabDir}/{fsw}.prefab")) fid = fsw;
                var replaced = new List<int>();
                string mid = Modern.TryGetValue(p.id, out var m0) ? m0 : null;
                // 现代时代的改位：id "=" 按对照表换；"" = 这一处不放（原件藏起来）；别的 = 换成指定的件
                Place? ms = ModernSpot.TryGetValue((p.id, p.x, p.z), out var ms0) ? ms0 : (Place?)null;
                if (ms != null && ms.Value.id != "=") mid = ms.Value.id;
                // 深空（4）：对照表 → 远侧换矿帮版 → 个别位置另外指定
                string sid = Space.TryGetValue(p.id, out var s0) ? s0 : null;
                if (sid != null && p.z < -38f && SpaceRedSide.TryGetValue(sid, out var srs)) sid = srs;   // 只有最远那一排（矿帮营地）；右侧整条布景带都是星枪团的基地
                if (SpaceAt.TryGetValue((p.id, p.x, p.z), out var sat)) sid = sat;
                foreach (var (era, tid) in new[] { (1, gid), (2, mid), (3, fid), (4, sid) })
                {
                    if ((era == 2 || era == 4) && tid == "") { replaced.Add(era); continue; }
                    var tpf = tid != null ? AssetDatabase.LoadAssetAtPath<GameObject>($"{WTArt.PrefabDir}/{tid}.prefab") : null;
                    if (!tpf) continue;
                    // 未来时代的孪生件比原件胖的（细长木栅栏 → 蜂巢塔）：换个位置，或者这一处不放（原件照样藏起来）
                    (float x, float z)? spot = null;
                    if (era == 3 && FutureSpot.TryGetValue((p.id, p.x, p.z), out spot) && spot == null) { replaced.Add(3); continue; }
                    var tw = (GameObject)PrefabUtility.InstantiatePrefab(tpf, root);
                    tw.name = tid + "_for_" + p.id;
                    tw.transform.SetLocalPositionAndRotation(go.transform.localPosition, go.transform.localRotation);
                    // 未来时代的替换件：站在真实地面上（原件所在的平台顶面，压 1 mm）。不能照搬原件的高度——原件在坡上是半埋的（见 Build 开头），
                    // 货运车照搬马车的位置，车轮全埋在地里（11-13 近拍查出）
                    if (era == 3)
                    {
                        float tx = spot != null ? spot.Value.x : p.x, tz = spot != null ? spot.Value.z : p.z;
                        tw.transform.localPosition = new Vector3(tx * BoardMap.Yard, Ground(tx, tz) * BoardMap.Yard - 0.001f, tz * BoardMap.Yard);
                    }
                    tw.transform.localScale = Vector3.one * p.scale * (era == 3 && FutureScale.TryGetValue(p.id, out float fsc) ? fsc : era == 2 && ModernScale.TryGetValue(p.id, out float msc) ? msc : 1f);
                    if (era == 2)   // 现代的替换件同理：站在真实地面上；改了位的按 ModernSpot 摆
                    {
                        if (ms != null) PlaceModern(tw, tid, ms.Value.x, ms.Value.z, ms.Value.yaw, ms.Value.scale, Ground);
                        else PlaceModern(tw, tid, p.x, p.z, p.yaw, tw.transform.localScale.x, Ground);
                    }
                    if (era == 4)   // 深空的替换件：和现代一样站在真实地面上（模型都不带底板）
                    {
                        // 前进基地的几栋大件沿用现代风格排好的位置和朝向（ModernSpot 里按对照表换的那几项：指挥所、三栋长条营房、发电机、货箱）——
                        // 居住舱和板房一样是长条，照原件农舍的朝向摆会歪七扭八
                        bool useSpot = ms != null && ms.Value.id == "=" && SpaceSpotFrom.Contains(p.id);
                        float sx = useSpot ? ms.Value.x : p.x, sz = useSpot ? ms.Value.z : p.z, syaw = useSpot ? ms.Value.yaw : p.yaw;
                        PlaceModern(tw, tid, sx, sz, syaw, p.scale * (SpaceScale.TryGetValue(p.id, out float ssc) ? ssc : 1f), Ground);
                    }
                    tw.AddComponent<WarTable.Game.EraOnly>().era = era;
                    tw.SetActive(false);
                    replaced.Add(era);
                }
                if (FutureHidden.Any(h => p.id.StartsWith(h)) && !replaced.Contains(3)) replaced.Add(3);
                if (FutureHidden.Any(h => p.id.StartsWith(h)) && !replaced.Contains(4)) replaced.Add(4);   // 深空同样没有树 / 草垛 / 羊 / 小船：没做出替换件的也藏起来
                if (replaced.Count > 0) { var eo = go.AddComponent<WarTable.Game.EraOnly>(); eo.era = -1; eo.replacedIn = replaced.ToArray(); }   // 原件：在这些时代藏起来
            }
            // 现代时代另外加的布景和水泥停车坪
            foreach (var e in ModernExtra)
            {
                var epf = AssetDatabase.LoadAssetAtPath<GameObject>($"{WTArt.PrefabDir}/{e.id}.prefab");
                if (!epf) continue;
                var ex = (GameObject)PrefabUtility.InstantiatePrefab(epf, root);
                ex.name = e.id + "_extra";
                PlaceModern(ex, e.id, e.x, e.z, e.yaw, e.scale, Ground);
                ex.AddComponent<WarTable.Game.EraOnly>().era = 2; ex.SetActive(false);
            }
            {
                // （试过给卡车铺水泥停车坪：地形只在旧马车脚下压了 2 码的小平台，7 码的坪四周悬空近 1 cm，又不能为现代一个时代去改所有时代共用的地形 → 不铺）
                var oldPads = board ? board.transform.Find("ModernPads") : null;
                if (oldPads) Object.DestroyImmediate(oldPads.gameObject);
            }
            BoardMap.Pads = pads;   // 之后的东西（未来时代的碎石 / 水晶芽 / 地灯）要贴着真实地面，用带平台的高度
            foreach (Transform t in root) if (t.name.StartsWith("Windmill")) AddSails(t);
            if (board) AmbientBuilder.Build(board.transform);
            // 11-12 用户：“注意别把场景做得太空” → 未来时代自己的地面散布（碎石、水晶芽、太阳能田）和巡逻无人机
            if (board) { ScatterBuilder.BuildFuture(board.transform); AmbientBuilder.BuildFuture(board.transform); }
            // 未来时代：草丛、篝火的烟和火、飞鸟都不出现（挂 EraOnly：原件、在时代 3 藏起来）
            if (board)
                foreach (var nm in new[] { "Scatter", "Ambient" })
                {
                    var grp = board.transform.Find(nm); if (!grp) continue;
                    foreach (Transform t in grp) { var eo = t.GetComponent<WarTable.Game.EraOnly>() ?? t.gameObject.AddComponent<WarTable.Game.EraOnly>(); eo.era = -1; eo.replacedIn = new[] { 3, 4 }; }   // 深空（4）同样不要草丛、烟火、飞鸟
                }
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log($"[PropBuilder] 摆放 {n}，缺模型 {miss}");
        }

        /// <summary>
        /// 摆一件现代时代的布景：位置 / 朝向 / 大小按给的来；落地 = 脚下的真实地面（带平台）。占地大的件（板房 10 码长）在缓坡上取“中心和四周一圈里最低的那个”，
        /// 但最多比中心低 1.5 mm（不然坡陡的地方整栋埋进去）；自带垫板的（ModernSink）把垫板埋掉。
        /// </summary>
        static void PlaceModern(GameObject go, string id, float x, float z, float yaw, float scale, System.Func<float, float, float> ground)
        {
            go.transform.localRotation = Quaternion.Euler(0, yaw + (ModernYawFix.TryGetValue(id, out float fix) ? fix : 0f), 0);
            go.transform.localScale = Vector3.one * scale;
            var b = Bounds(go);
            float r = Mathf.Max(b.extents.x, b.extents.z) / BoardMap.Yard * 0.7f, c = ground(x, z), lo = c;
            for (int k = 0; k < 8; k++) lo = Mathf.Min(lo, ground(x + Mathf.Cos(k * Mathf.PI / 4) * r, z + Mathf.Sin(k * Mathf.PI / 4) * r));
            float y = Mathf.Max(lo, c - 0.15f) - 0.1f;
            y -= (ModernSink.TryGetValue(id, out float sink) ? sink : 0f) * scale;
            go.transform.localPosition = new Vector3(x * BoardMap.Yard, y * BoardMap.Yard, z * BoardMap.Yard);
        }

        /// <summary>
        /// 风车叶轮（程序化）：P2 做不好镂空的格栅叶片，所以塔身用 P2、叶片在这里搭——
        /// 四根主梁 + 每片一副木格栅 + 一半蒙奶油色帆布，挂 Spin 慢慢转（场景要有动感）。
        /// 轮毂位置 = 塔身包围盒的正面上部（风车模型的轴头朝正面）。
        /// </summary>
        public static Vector3 HubLocal = new Vector3(0f, 0.82f, 0.5f);   // 相对包围盒（x 居中，y 高度比例，z 正面比例）

        static void AddSails(Transform mill)
        {
            // 轴头位置：量过 P2 风车模型——顶盖屋脊沿本地 Z，轴头从 +Z 山墙伸出，中心在 (0, 0.78h, 0.32h)。
            // （之前按“离塔心最远的点”找，挑中了屋檐角，叶轮装到塔身侧面、穿过屋顶。）叶轮再往前让 0.03h，转起来不碰屋檐和塔身。
            var mf = mill.GetComponentInChildren<MeshFilter>();
            var toMill = mill.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var v in mf.sharedMesh.vertices) { var q = toMill.MultiplyPoint3x4(v); minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y); }
            float h = maxY - minY;
            var hub = new GameObject("Sails").transform;
            hub.SetParent(mill, false);
            hub.localPosition = new Vector3(0f, minY + h * 0.78f, h * (0.322f + 0.03f));
            hub.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up) * Quaternion.Euler(-8f, 0, 0);   // 轴略上仰，像真风车
            float len = h * 0.44f, w = len * 0.22f;
            // 轴：从轴头伸到叶轮
            var axle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(axle.GetComponent<Collider>());
            axle.name = "Axle";
            axle.transform.SetParent(hub, false);
            axle.transform.localRotation = Quaternion.Euler(90, 0, 0);
            axle.transform.localPosition = new Vector3(0, 0, -h * 0.02f);
            axle.transform.localScale = new Vector3(h * 0.03f, h * 0.025f, h * 0.03f);
            var go = new GameObject("Blades");
            go.transform.SetParent(hub, false);
            go.AddComponent<MeshFilter>().sharedMesh = SailMesh(len, w);
            var mr = go.AddComponent<MeshRenderer>();
            var wood = BoardBuilder.Mat("M_SailWood", "WarTable/MiniLit", m => { m.SetColor("_BaseColor", new Color(0.42f, 0.29f, 0.18f)); m.SetFloat("_Gloss", 0.1f); });
            mr.sharedMaterials = new[]
            {
                wood,
                BoardBuilder.Mat("M_SailCloth", "WarTable/MiniLit", m => { m.SetColor("_BaseColor", new Color(0.74f, 0.66f, 0.5f)); m.SetFloat("_Gloss", 0.02f); m.SetFloat("_Cull", 0); }),
            };
            axle.GetComponent<MeshRenderer>().sharedMaterial = wood;
            go.AddComponent<Spin>();
        }

        static Mesh SailMesh(float len, float w)
        {
            var v = new List<Vector3>(); var t0 = new List<int>(); var t1 = new List<int>();
            void Box(Vector3 c, Vector3 half, Quaternion q, List<int> tri)
            {
                int s = v.Count;
                for (int i = 0; i < 8; i++) v.Add(c + q * Vector3.Scale(half, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                int[] f = { 0, 2, 3, 1, 4, 5, 7, 6, 0, 1, 5, 4, 2, 6, 7, 3, 0, 4, 6, 2, 1, 3, 7, 5 };
                for (int k = 0; k < 24; k += 4) { tri.AddRange(new[] { s + f[k], s + f[k + 1], s + f[k + 2], s + f[k], s + f[k + 2], s + f[k + 3] }); }
            }
            float bar = w * 0.07f;
            Box(Vector3.zero, new Vector3(bar * 2.2f, bar * 2.2f, bar * 2f), Quaternion.identity, t0);   // 轮毂
            for (int a = 0; a < 4; a++)
            {
                var q = Quaternion.Euler(0, 0, a * 90f + 8f);
                Box(q * new Vector3(0, len * 0.5f, 0), new Vector3(bar, len * 0.5f, bar), q, t0);         // 主梁
                // 格栅：两根边框 + 横档
                Box(q * new Vector3(w, len * 0.6f, 0), new Vector3(bar * 0.6f, len * 0.4f, bar * 0.6f), q, t0);
                for (int k = 0; k <= 6; k++)
                {
                    float y = len * 0.2f + len * 0.8f * k / 6f;
                    Box(q * new Vector3(w * 0.5f, y, 0), new Vector3(w * 0.5f, bar * 0.5f, bar * 0.5f), q, t0);
                }
                // 帆布：格栅后面一层薄布
                Box(q * new Vector3(w * 0.5f, len * 0.6f, -bar), new Vector3(w * 0.48f, len * 0.39f, bar * 0.15f), q, t1);
            }
            var m = new Mesh { name = "WindmillSails" };
            m.SetVertices(v); m.subMeshCount = 2; m.SetTriangles(t0, 0); m.SetTriangles(t1, 1);
            m.RecalculateNormals(); m.RecalculateBounds();
            string path = "Assets/_WarTable/Art/Generated/WindmillSails.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            b.center -= go.transform.position;
            return b;
        }
            // ---------------------------------------------------------------- 占地 / 穿模巡检

        public struct Foot { public string id; public Vector2 c; public float r; }
        static readonly Dictionary<string, float> footCache = new();

        /// <summary>落地部分（模型底部 20% 高度内）的半径（码，未乘摆放缩放）。草簇、麦田据此避开布景件。</summary>
        public static float FootRadius(string id)
        {
            if (footCache.TryGetValue(id, out var fr)) return fr;
            float r = id.StartsWith("std:") ? 2.2f : id == "proc:pavilion" ? 2.3f : 1f;
            if (!id.StartsWith("demo:") && !id.StartsWith("std:") && !id.StartsWith("proc:"))
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{WTArt.PrefabDir}/{id}.prefab");
                var mf = pf ? pf.GetComponentsInChildren<MeshFilter>(true) : null;
                if (mf != null && mf.Length > 0)
                {
                    var f = mf[mf.Length - 1];
                    var m = pf.transform.worldToLocalMatrix * f.transform.localToWorldMatrix;
                    var vs = f.sharedMesh.vertices;
                    float minY = float.MaxValue, maxY = float.MinValue;
                    foreach (var v in vs) { var q = m.MultiplyPoint3x4(v); minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y); }
                    float best = 0f;
                    foreach (var v in vs) { var q = m.MultiplyPoint3x4(v); if (q.y < minY + (maxY - minY) * 0.2f) best = Mathf.Max(best, new Vector2(q.x, q.z).magnitude); }
                    r = best / BoardMap.Yard;
                }
            }
            footCache[id] = r;
            return r;
        }

        /// <summary>布景件脚下的平台：半径 = 底座半径，高度 = 不带平台时中心的地面高度。石桥、栈桥不垫（跨河 / 在水里）。</summary>
        public static Vector4[] ComputePads()
        {
            BoardMap.Pads = null;
            var o = new List<Vector4>();
            foreach (var f in Footprints())
            {
                if (f.id == "Bridge_Stone" || f.id == "Jetty_Boat") continue;
                o.Add(new Vector4(f.c.x, f.c.y, Mathf.Max(0.8f, f.r), BoardMap.Height(f.c.x, f.c.y)));
            }
            return o.ToArray();
        }

        public static List<Foot> Footprints()
        {
            footCache.Clear();
            var o = new List<Foot>();
            foreach (var p in Layout) o.Add(new Foot { id = p.id, c = new Vector2(p.x, p.z), r = FootRadius(p.id) * p.scale });
            return o;
        }

        static readonly string[] MayTouchRoad = { "Wagon_Supply", "Bridge_Stone", "Market_Stall", "banner" };

        /// <summary>
        /// 穿模巡检（菜单 WarTable/5）：把每件布景（用 LOD1）和每个棋子（按当前姿势烘焙）的三角面采样成 0.35 码的体素，
        /// 两件东西共用体素 = 互相插进去了；另查出框、压河、压路、压麦田。返回报告文本（空 = 全部通过）。
        /// </summary>
        /// <summary>陷地 / 悬空检查是否连原件和哥特替换件一起查（默认只查未来时代的替换件，见 ClipAudit 里的说明）。</summary>
        public static bool AuditSinkAll = false;

        [MenuItem("WarTable/5 穿模巡检")]
        public static void ClipAuditMenu() => Debug.Log("[ClipAudit]\n" + ClipAudit());

        public static string ClipAudit()
        {
            var board = GameObject.Find(BoardBuilder.Root);
            var toBoard = board.transform.worldToLocalMatrix;
            BoardMap.Pads = ComputePads();   // 平台数据重启 / 重新编译后会丢（见 Build 开头），巡检自己先算一遍
            var items = new List<(string name, Transform t, HashSet<Vector3Int> vox, Bounds b, List<Vector3> pts)>();
            const float V = 0.35f;
            void Add(string name, Transform t, IEnumerable<(Mesh mesh, Matrix4x4 m)> meshes)
            {
                var vox = new HashSet<Vector3Int>(); var pts = new List<Vector3>();
                var b = new Bounds(); bool first = true;
                foreach (var (mesh, m) in meshes)
                {
                    var mm = toBoard * m;
                    var vs = mesh.vertices; var tri = mesh.triangles;
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        Vector3 a = mm.MultiplyPoint3x4(vs[tri[i]]) / BoardMap.Yard, bb = mm.MultiplyPoint3x4(vs[tri[i + 1]]) / BoardMap.Yard, c = mm.MultiplyPoint3x4(vs[tri[i + 2]]) / BoardMap.Yard;
                        foreach (var q in new[] { a, bb, c, (a + bb + c) / 3f, (a + bb) * 0.5f, (bb + c) * 0.5f, (a + c) * 0.5f })
                        {
                            vox.Add(Vector3Int.FloorToInt(q / V)); pts.Add(q);
                            if (first) { b = new Bounds(q, Vector3.zero); first = false; } else b.Encapsulate(q);
                        }
                    }
                }
                if (!first) items.Add((name, t, vox, b, pts));
            }
            var props = board.transform.Find(Root);
            if (props)
                foreach (Transform t in props)
                {
                    var list = new List<(Mesh, Matrix4x4)>();
                    var lg = t.GetComponentInChildren<LODGroup>();
                    var rs = lg ? lg.GetLODs()[Mathf.Min(1, lg.lodCount - 1)].renderers : t.GetComponentsInChildren<Renderer>();
                    foreach (var r in rs) { var mf = r ? r.GetComponent<MeshFilter>() : null; if (mf && mf.sharedMesh) list.Add((mf.sharedMesh, mf.transform.localToWorldMatrix)); }
                    // 风车叶片之类挂在 LOD 外面的程序网格
                    foreach (var mf in t.GetComponentsInChildren<MeshFilter>()) if (!mf.GetComponent<Renderer>() || !lg || System.Array.IndexOf(lg.GetLODs().SelectMany(l => l.renderers).ToArray(), mf.GetComponent<Renderer>()) < 0) list.Add((mf.sharedMesh, mf.transform.localToWorldMatrix));
                    Add(t.name, t, list);
                }
            var armies = board.transform.Find(ArmyPreview.Root);
            if (armies)
                foreach (Transform g in armies)
                    foreach (Transform f in g)
                    {
                        var list = new List<(Mesh, Matrix4x4)>();
                        foreach (var r in f.GetComponentsInChildren<Renderer>())
                        {
                            if (r is SkinnedMeshRenderer sm) { var baked = new Mesh(); sm.BakeMesh(baked, true); list.Add((baked, sm.transform.localToWorldMatrix)); }
                            else if (r.GetComponent<MeshFilter>()) list.Add((r.GetComponent<MeshFilter>().sharedMesh, r.transform.localToWorldMatrix));
                        }
                        Add(g.name, f, list);
                    }
            var sb = new System.Text.StringBuilder();
            int nProps = props ? props.childCount : 0;
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    if (i >= nProps && j >= nProps) continue;    // 棋子之间由布阵间距保证
                    var A = items[i]; var B = items[j];
                    // 11-11：不同时代的布景（原件和它的哥特孪生件）不会同时出现，不算穿插
                    var eA = A.t ? A.t.GetComponent<WarTable.Game.EraOnly>() : null; var eB = B.t ? B.t.GetComponent<WarTable.Game.EraOnly>() : null;
                    if (!WarTable.Game.EraOnly.Coexist(eA, eB)) continue;
                    var ea = A.b; ea.Expand(V * 2);
                    if (!ea.Intersects(B.b)) continue;
                    int shared = 0;
                    foreach (var k in A.vox) if (B.vox.Contains(k)) shared++;
                    // 石墙按长度首尾拼接成一道墙，端头相接是设计如此
                    if (A.name.StartsWith("Wall_Stone") && B.name.StartsWith("Wall_Stone") && shared < 12) continue;
                    if (shared >= 4) sb.AppendLine($"穿插 {A.name}@{Fmt(A.t)} ↔ {B.name}@{Fmt(B.t)}：{shared}");
                }
            // 底座重叠：每件最底下 0.5 码以内的部分投到地面 0.3 码网格，两件底座占同一格 = 土坪 / 底边叠在一起（会闪、会穿）
            var baseCells = new List<HashSet<Vector2Int>>();
            for (int i = 0; i < nProps && i < items.Count; i++)
            {
                var hs = new HashSet<Vector2Int>();
                float y0 = items[i].b.min.y + 0.5f;
                foreach (var q in items[i].pts) if (q.y < y0) hs.Add(new Vector2Int(Mathf.FloorToInt(q.x / 0.3f), Mathf.FloorToInt(q.z / 0.3f)));
                baseCells.Add(hs);
            }
            for (int i = 0; i < baseCells.Count; i++)
                for (int j = i + 1; j < baseCells.Count; j++)
                {
                    if (items[i].name.StartsWith("Wall_Stone") && items[j].name.StartsWith("Wall_Stone")) continue;
                    var e1 = items[i].t ? items[i].t.GetComponent<WarTable.Game.EraOnly>() : null; var e2 = items[j].t ? items[j].t.GetComponent<WarTable.Game.EraOnly>() : null;
                    if (!WarTable.Game.EraOnly.Coexist(e1, e2)) continue;   // 原件和哥特孪生件不同时出现
                    int shared = 0;
                    foreach (var c in baseCells[i]) if (baseCells[j].Contains(c)) shared++;
                    if (shared >= 2) sb.AppendLine($"底座重叠 {items[i].name}@{Fmt(items[i].t)} ↔ {items[j].name}@{Fmt(items[j].t)}：{shared}");
                }
            for (int i = 0; i < nProps && i < items.Count; i++)
            {
                var it = items[i];
                float ground = it.b.min.y + 0.6f;
                int outF = 0, river = 0, road = 0, field = 0;
                foreach (var q in it.pts)
                {
                    if ((Mathf.Abs(q.x) > BoardMap.HalfX - 0.15f || Mathf.Abs(q.z) > BoardMap.HalfZ - 0.15f) && q.y > 0.3f) outF++;
                    if (q.y > ground) continue;
                    if (BoardMap.RiverDistance(q.x, q.z) < BoardMap.RiverHalfWidth) river++;
                    if (BoardMap.RoadDistance(q.x, q.z) < BoardMap.RoadHalfWidth * 0.7f) road++;
                    if (ScatterBuilder.InField(q.x, q.z)) field++;
                }
                bool wet = it.name.StartsWith("Bridge") || it.name.StartsWith("Jetty");
                // 陷地 / 悬空（11-13 加：以前只查物件之间，货运车车轮全埋在地里没查出来）：物件最低点和它脚下的真实地面比。
                // 正常是压进地里 1 mm（0.1 码）；陷下去超过 3 mm、或者底部有一片离地超过 3 mm 都报。
                // 只查未来时代的替换件（AuditSinkAll = true 时全查）：原件和哥特替换件在坡上本来就是半埋的，那是定稿的样子（见 Build 开头），不报
                if (!wet && (AuditSinkAll || it.name.StartsWith("F_") || it.name.StartsWith("M_") || it.name.StartsWith("S_")))
                {
                    float sink = BoardMap.Height(it.b.center.x, it.b.center.z) - it.b.min.y;
                    float gap = 0f; int low = 0, high = 0;
                    foreach (var q in it.pts)
                    {
                        if (q.y > it.b.min.y + 0.25f) continue;     // 只看贴着底面的那一层
                        low++; float g = q.y - BoardMap.Height(q.x, q.z);
                        if (g > 0.3f) { high++; gap = Mathf.Max(gap, g); }
                    }
                    // 现代的卡车 / 集装箱 / 路障 / 靶架是故意把自带的垫板埋进地里的（ModernSink），这部分不算陷地
                    float buried = ModernSink.Where(k => it.name.StartsWith(k.Key)).Select(k => k.Value * it.t.localScale.x).FirstOrDefault();
                    if (sink > 0.3f + buried) sb.AppendLine($"陷地 {it.name}@{Fmt(it.t)}：{sink * 10f:F1} mm");
                    else if (low > 0 && high > low * 0.25f) sb.AppendLine($"悬空 {it.name}@{Fmt(it.t)}：底面 {100 * high / low}% 离地，最多 {gap * 10f:F1} mm");
                }
                if (outF > 3) sb.AppendLine($"出框 {it.name}@{Fmt(it.t)}：{outF}");
                if (river > 3 && !wet) sb.AppendLine($"压河 {it.name}@{Fmt(it.t)}：{river}");
                if (road > 3 && !MayTouchRoad.Any(n => it.name.Contains(n))) sb.AppendLine($"压路 {it.name}@{Fmt(it.t)}：{road}");
                if (field > 3) sb.AppendLine($"压麦田 {it.name}@{Fmt(it.t)}：{field}");
            }
            // 地形网格和摆放表要同步：地形是“1 搭沙盘”按当时的摆放表压出平台的，摆放表改了位置而没重搭地形，公式和网格就对不上
            var terr = board.transform.Find("Terrain") ? board.transform.Find("Terrain").GetComponent<MeshFilter>() : null;
            if (terr && terr.sharedMesh)
            {
                var tv = terr.sharedMesh.vertices; float worst = 0f;
                for (int i = 0; i < tv.Length; i += 23)
                    if (Mathf.Abs(tv[i].x) < (BoardMap.HalfX - 1.5f) * BoardMap.Yard && Mathf.Abs(tv[i].z) < (BoardMap.HalfZ - 1.5f) * BoardMap.Yard)
                        worst = Mathf.Max(worst, Mathf.Abs(BoardMap.Height(tv[i].x / BoardMap.Yard, tv[i].z / BoardMap.Yard) * BoardMap.Yard - tv[i].y));
                if (worst > 0.0005f) sb.AppendLine($"地形网格和摆放表不同步（最大相差 {worst * 1000f:F1} mm）：先跑“1 搭沙盘”再摆放");
            }
            return sb.ToString();
        }

        static string Fmt(Transform t) { var p = t.localPosition / BoardMap.Yard; return $"({p.x:0.#},{p.z:0.#})"; }
    }
}
