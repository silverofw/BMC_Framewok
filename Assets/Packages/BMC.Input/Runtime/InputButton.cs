namespace BMC
{
    /// <summary>
    /// 手把按鍵的抽象代號。名稱沿用 Xbox 佈局，實際綁到哪顆實體鍵由 InputService 決定。
    ///
    /// 【為什麼不用 InputSystem 的 InputAction】這一層的用途是「讓上層不必知道裝置」，
    /// 而 InputAction 需要一份 .inputactions 資產、需要有人負責它的啟用停用生命週期，
    /// 兩顆 PlayerInput 共用同一份資產時還會互相關掉對方的 action map。
    /// 直接輪詢裝置沒有這些狀態，也就沒有這些失效模式。
    /// </summary>
    public enum InputButton
    {
        None = 0,

        A,
        B,
        X,
        Y,

        ShoulderL,
        ShoulderR,
        TriggerL,
        TriggerR,

        StickPressL,
        StickPressR,

        Start,
        Select,
    }

    /// <summary>四方向。斜推時取絕對值較大的那一軸，不會同時成立兩個方向。</summary>
    public enum InputDirection
    {
        None = 0,
        Up,
        Down,
        Left,
        Right,
    }
}
