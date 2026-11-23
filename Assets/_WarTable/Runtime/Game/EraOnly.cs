using UnityEngine;

namespace WarTable.Game
{
    /// <summary>
    /// 只在某个时代出现的布景（11-11 用户：不同时代的场地要有简单变化、有自己风格的建筑）。
    /// PropBuilder 给有哥特版的布景（教堂、农舍、风车、一半橡树）在同一位置摆一个哥特孪生件：原件 era = 0，孪生件 era = 1；
    /// EraLook 按 Settings.Era 显隐。
    /// </summary>
    public class EraOnly : MonoBehaviour
    {
        /// <summary>孪生件：只在这个时代出现；原件：−1。</summary>
        public int era;
        /// <summary>原件（era = −1）：在这些时代被孪生件替换、要藏起来。</summary>
        public int[] replacedIn;
        /// <summary>孪生件：除了 era 之外还在这些风格里出现（11-13：深空和未来共用去苔藓的岩石、碎石散布、无人机）。</summary>
        public int[] alsoIn;

        public bool ActiveIn(int cur) => era >= 0 ? era == cur || (alsoIn != null && System.Array.IndexOf(alsoIn, cur) >= 0) : replacedIn == null || System.Array.IndexOf(replacedIn, cur) < 0;
        /// <summary>两件布景有没有可能同时出现（穿模巡检用）。</summary>
        public static bool Coexist(EraOnly a, EraOnly b)
        {
            if (!a || !b) return true;
            for (int k = 0; k < Settings.Eras; k++) if (a.ActiveIn(k) && b.ActiveIn(k)) return true;
            return false;
        }
    }
}
