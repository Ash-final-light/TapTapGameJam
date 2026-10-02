using System.Collections.Generic;
using UnityEngine;

using AshFramework.UI.ScreenAdaptation.Components;

namespace AshFramework.UI.ScreenAdaptation.Services
{
/// <summary>
/// 屏幕适配相关的服务接口
/// </summary>
public sealed class ScreenAdaptService : IScreenAdaptService
{
    #region 属性和字段

    public static ScreenAdaptService Instance { get; } = new ScreenAdaptService();

    private readonly HashSet<CanvasWidthAdjuster> mSetCanvasWidthAdjusters = new HashSet<CanvasWidthAdjuster>();
    private bool mShowAdaptBorderPaused = false;

    #endregion

    #region 接口

    public void PauseShowBorder()
    {
        if (mShowAdaptBorderPaused)
        {
            Debug.LogWarning("ScreenAdaptService: PauseShowBorder was called while borders were already hidden.");
            return;
        }

        mShowAdaptBorderPaused = true;
        SetBorderVisible(false);
    }

    public void ResumeShowBorder()
    {
        if (!mShowAdaptBorderPaused)
        {
            Debug.LogWarning("ScreenAdaptService: ResumeShowBorder was called while borders were already visible.");
            return;
        }

        mShowAdaptBorderPaused = false;
        SetBorderVisible(true);
    }

    public IEnumerable<CanvasWidthAdjuster> CanvasWidthAdjusters => mSetCanvasWidthAdjusters;

    public void RegisterCanvasWidthAdjuster(CanvasWidthAdjuster adjuster)
    {
        if (adjuster == null)
            return;

        var addSuccess = mSetCanvasWidthAdjusters.Add(adjuster);
        if (!addSuccess)
        {
            Debug.LogWarning("ScreenAdaptService: the CanvasWidthAdjuster is already registered.", adjuster);
        }
        else
        {
            adjuster.SetAdaptBorderVisible(!mShowAdaptBorderPaused);
        }
    }

    public void UnregisterCanvasWidthAdjuster(CanvasWidthAdjuster adjuster)
    {
        if (adjuster == null)
            return;
        
        // 恢复默认状态
        adjuster.SetAdaptBorderVisible(true);
        
        var removeSuccess = mSetCanvasWidthAdjusters.Remove(adjuster);
        if (!removeSuccess)
        {
            Debug.LogWarning("ScreenAdaptService: tried to unregister an unknown CanvasWidthAdjuster.", adjuster);
        }
    }

    #endregion

    #region private

    private void SetBorderVisible(bool visible)
    {
        foreach (var adjuster in mSetCanvasWidthAdjusters)
        {
            adjuster.SetAdaptBorderVisible(visible);
        }
    }

    #endregion
}
}
