using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 玩家配色板：服务器按 playerIndex 分配，最多 4 名玩家。
    /// </summary>
    public static class PlayerPalette
    {
        /// <summary>玩家数量上限（题目要求 1-4 人）。</summary>
        public const int MaxPlayers = 4;

        private static readonly Color[] Palette =
        {
            new Color(0.30f, 0.65f, 1.00f), // 蓝
            new Color(1.00f, 0.45f, 0.35f), // 橙红
            new Color(0.45f, 0.90f, 0.45f), // 绿
            new Color(0.95f, 0.85f, 0.35f), // 黄
        };

        /// <summary>按玩家编号取颜色，越界时回落到 0 号色。</summary>
        public static Color GetColor(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= Palette.Length)
                return Palette[0];

            return Palette[playerIndex];
        }
    }
}
