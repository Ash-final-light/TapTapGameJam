using System.Collections.Generic;

using AshFramework.UI.ScreenAdaptation.Components;

namespace AshFramework.UI.ScreenAdaptation.Services
{
public interface IScreenAdaptService
{
    void PauseShowBorder();
    void ResumeShowBorder();
    IEnumerable<CanvasWidthAdjuster> CanvasWidthAdjusters { get; }
    void RegisterCanvasWidthAdjuster(CanvasWidthAdjuster adjuster);
    void UnregisterCanvasWidthAdjuster(CanvasWidthAdjuster adjuster);
}
}
