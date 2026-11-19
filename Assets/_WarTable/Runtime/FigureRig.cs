using UnityEngine;

namespace WarTable
{
    /// <summary>
    /// 棋子预制体上显示层要用的引用（DemoFigures 搭预制体时填）：骑手 / 坐骑的动画、
    /// 按兵种武器槽顺序排列的武器（原件 + 低模，切武器时整组显隐）、盾、脊柱（上半身转向用）。
    /// </summary>
    public class FigureRig : MonoBehaviour
    {
        public FigureAnimator rider;
        public FigureAnimator mount;
        public Transform figure;
        public Transform spine;
        /// <summary>现代时代（11-13）：弓兵手里是步枪——弓类武器改播步枪动作（预备 / 瞄准 / 射击同弩兵），步枪握在右手。</summary>
        public bool rifleBows;
        /// <summary>第 i 件武器（原型 troop.weapons[i]）的所有渲染物体（原件 + 低模）。</summary>
        public WeaponSet[] weapons;
        public GameObject[] shield;

        [System.Serializable]
        public class WeaponSet { public string id; public GameObject[] parts; }

        public void ShowWeapon(int index)
        {
            for (int i = 0; i < weapons.Length; i++)
                foreach (var p in weapons[i].parts) if (p && p.activeSelf != (i == index)) p.SetActive(i == index);
        }

        public void ShowShield(bool on)
        {
            foreach (var s in shield) if (s && s.activeSelf != on) s.SetActive(on);
        }
    }
}
