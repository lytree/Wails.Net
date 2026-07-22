namespace Wails.Net.Application.Screens;

/// <summary>
/// 屏幕对齐方向。
/// 对应 Wails v3 Go 版本 <c>Alignment</c> 枚举（screenmanager.go）。
/// </summary>
public enum Alignment
{
    /// <summary>
    /// 屏幕位于父屏幕的上方。
    /// </summary>
    Top = 0,

    /// <summary>
    /// 屏幕位于父屏幕的右侧。
    /// </summary>
    Right = 1,

    /// <summary>
    /// 屏幕位于父屏幕的下方。
    /// </summary>
    Bottom = 2,

    /// <summary>
    /// 屏幕位于父屏幕的左侧。
    /// </summary>
    Left = 3,
}

/// <summary>
/// 偏移参考点。
/// 对应 Wails v3 Go 版本 <c>OffsetReference</c> 枚举（screenmanager.go）。
/// </summary>
public enum OffsetReference
{
    /// <summary>
    /// 偏移相对于起始边（顶部或左侧）计算。
    /// </summary>
    Begin = 0,

    /// <summary>
    /// 偏移相对于结束边（底部或右侧）计算。
    /// </summary>
    End = 1,
}

/// <summary>
/// 描述子屏幕相对于父屏幕的放置位置。
/// 对应 Wails v3 Go 版本 <c>ScreenPlacement</c> 结构（screenmanager.go）。
/// <para>
/// 参考图示（S 为本屏幕，P 为父屏幕）：
/// S 与 P 右对齐，正向偏移，BEGIN（顶部）偏移参考：
/// <code>
/// .           +------------+   +
/// .           |            |   | offset
/// .           |     P      |   v
/// .           |            +--------+
/// .           |            |        |
/// .           +------------+   S    |
/// .                        |        |
/// .                        +--------+
/// </code>
/// </para>
/// <para>
/// 该类型为引用类型以支持 <see cref="Apply"/> 方法直接修改 <see cref="Screen"/> 的坐标。
/// </para>
/// </summary>
public sealed class ScreenPlacement
{
    /// <summary>
    /// 获取或设置要放置的子屏幕。
    /// </summary>
    public Screen Screen { get; set; }

    /// <summary>
    /// 获取或设置父屏幕（参考屏幕）。
    /// </summary>
    public Screen Parent { get; set; }

    /// <summary>
    /// 获取或设置子屏幕相对于父屏幕的对齐方向。
    /// </summary>
    public Alignment Alignment { get; set; }

    /// <summary>
    /// 获取或设置偏移量（DIP 逻辑像素）。
    /// </summary>
    public int Offset { get; set; }

    /// <summary>
    /// 获取或设置偏移参考点。
    /// </summary>
    public OffsetReference OffsetReference { get; set; }

    /// <summary>
    /// 使用指定参数构造 <see cref="ScreenPlacement"/> 实例。
    /// </summary>
    /// <param name="screen">子屏幕。</param>
    /// <param name="parent">父屏幕。</param>
    /// <param name="alignment">对齐方向。</param>
    /// <param name="offset">偏移量（DIP）。</param>
    /// <param name="offsetReference">偏移参考点。</param>
    public ScreenPlacement(Screen screen, Screen parent, Alignment alignment, int offset, OffsetReference offsetReference)
    {
        Screen = screen;
        Parent = parent;
        Alignment = alignment;
        Offset = offset;
        OffsetReference = offsetReference;
    }

    /// <summary>
    /// 根据对齐方向和偏移量计算并应用屏幕的新坐标。
    /// 对应 Wails v3 Go 版本 <c>ScreenPlacement.apply()</c>。
    /// <para>
    /// 计算规则：
    /// <list type="bullet">
    /// <item>TOP/BOTTOM 对齐：沿 X 轴偏移，Y 轴根据 TOP（向上）或 BOTTOM（向下）对齐。</item>
    /// <item>LEFT/RIGHT 对齐：沿 Y 轴偏移，X 轴根据 LEFT（向左）或 RIGHT（向右）对齐。</item>
    /// <item>END 偏移参考：偏移量从父屏幕结束边反向计算。</item>
    /// <item>偏移量被限制在 [-screen.Size, parent.Size] 范围内。</item>
    /// </list>
    /// </para>
    /// </summary>
    public void Apply()
    {
        var parentBounds = Parent.Bounds;
        var screenBounds = Screen.Bounds;

        var newX = parentBounds.X;
        var newY = parentBounds.Y;
        var offset = Offset;

        if (Alignment is Alignment.Top or Alignment.Bottom)
        {
            if (OffsetReference == OffsetReference.End)
            {
                offset = parentBounds.Width - offset - screenBounds.Width;
            }

            offset = Math.Min(offset, parentBounds.Width);
            offset = Math.Max(offset, -screenBounds.Width);
            newX += offset;
            if (Alignment == Alignment.Top)
            {
                newY -= screenBounds.Height;
            }
            else
            {
                newY += parentBounds.Height;
            }
        }
        else
        {
            if (OffsetReference == OffsetReference.End)
            {
                offset = parentBounds.Height - offset - screenBounds.Height;
            }

            offset = Math.Min(offset, parentBounds.Height);
            offset = Math.Max(offset, -screenBounds.Height);
            newY += offset;
            if (Alignment == Alignment.Left)
            {
                newX -= screenBounds.Width;
            }
            else
            {
                newX += parentBounds.Width;
            }
        }

        Screen.Move(newX, newY);
    }
}
