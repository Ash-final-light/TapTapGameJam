#pragma warning disable IDE0010
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace AshFramework.UI.ScreenAdaptation.Android
{
public class DeviceNotchDataManager
{
    public readonly static DeviceNotchDataManager Instance = new DeviceNotchDataManager();

    private class DeviceNotchData
    {
        public string Model;
        public float NotchHeightRatio;
        public int CheckWidth;
    }

    private const string ResourcePath = "Config/device_notch_data";

    private Dictionary<string, DeviceNotchData> modelData = new Dictionary<string, DeviceNotchData>();

    public void Init()
    {
        modelData.Clear();

        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
        {
            Debug.LogWarning(
                $"Optional notch fallback data was not found at Assets/Resources/{ResourcePath}.csv. " +
                "Screen.safeArea and Android display-cutout APIs will still be used.");
            return;
        }

        ParseFile(asset.text);

        Debug.Log("DeviceNotchDataManager inited");
    }

    private void ParseFile(string text)
    {
        using (var sw = new StringReader(text))
        {
            // 忽略第一行
            sw.ReadLine();

            string line;
            while (!string.IsNullOrEmpty(line = sw.ReadLine()))
            {
                string[] columns = line.Split(',');
                if (columns.Length < 4)
                    continue;

                string model = columns[0].Trim().Trim('"');
                if (string.IsNullOrEmpty(model) ||
                    !float.TryParse(columns[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float notchHeightRatio) ||
                    !int.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int checkWidth))
                {
                    continue;
                }

                var data = new DeviceNotchData()
                {
                    Model = model,
                    NotchHeightRatio = notchHeightRatio,
                    CheckWidth = checkWidth
                };

                modelData[model] = data;
            }
        }
    }

    public bool HasNotch(string model, out int height)
    {
        height = 0;

        DeviceNotchData data;
        if (!modelData.TryGetValue(model, out data))
            return false;

        switch (Screen.orientation)
        {
            case ScreenOrientation.LandscapeLeft:
            case ScreenOrientation.LandscapeRight:
                height = (int)(Screen.width * (data.NotchHeightRatio / 100.0f) + 0.5f);
                return true;

            case ScreenOrientation.Portrait:
            case ScreenOrientation.PortraitUpsideDown:
                height = (int)(Screen.height * (data.NotchHeightRatio / 100.0f) + 0.5f);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 判断是否渲染到刘海区
    /// </summary>
    /// <param name="model"></param>
    /// <returns>如果返回false，表示不渲染到刘海区
    /// 注意：返回true，不表示就会渲染到刘海区</returns>
    public bool DoRenderBehindNotch(string model)
    {
        DeviceNotchData data;
        if (!modelData.TryGetValue(model, out data))
            return true;

        if (data.CheckWidth <= 0)
            return true;

        switch (Screen.orientation)
        {
            case ScreenOrientation.LandscapeLeft:
            case ScreenOrientation.LandscapeRight:
                return Screen.width > data.CheckWidth;

            case ScreenOrientation.Portrait:
            case ScreenOrientation.PortraitUpsideDown:
                return Screen.height > data.CheckWidth;

            default:
                return true;
        }
    }
}
}
#pragma warning restore IDE0010
