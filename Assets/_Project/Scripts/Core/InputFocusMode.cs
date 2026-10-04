namespace SphereRoom.Core
{
    /// <summary>
    /// 本地输入焦点：互斥的两态，决定「鼠标归谁用」。
    /// 必须与 <see cref="InputFocus"/> 搭配使用——光标可见性与视角采样必须同源，
    /// 否则会出现「鼠标出来了但视角还在跟着鼠标转」。
    /// </summary>
    public enum InputFocusMode : byte
    {
        /// <summary>菜单焦点：鼠标可见可点 UI；玩家不采样移动与视角。</summary>
        Menu = 0,

        /// <summary>对局焦点：鼠标锁定并隐藏；玩家采样移动与视角（第一人称常态）。</summary>
        Gameplay = 1,
    }
}
