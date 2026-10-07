#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TapTap.Systems.Editor
{
    public static class LoadingTipImporter
    {
        [MenuItem("Tools/Spotlight/从CSV导入加载文案")]
        public static void Import()
        {
            const string directory="Assets/Configs/Spotlight/";
            var config=AssetDatabase.LoadAssetAtPath<LoadingTipConfig>(directory+"LoadingTips.asset");
            if(!config)throw new InvalidOperationException("请先搭建系统资源。");
            var rows=Parse(File.ReadAllText(directory+"LoadingTips.csv",Encoding.UTF8));
            var result=new List<LoadingTip>();var ids=new HashSet<string>(StringComparer.Ordinal);
            for(int i=1;i<rows.Count;i++)
            {
                var row=rows[i];if(row.Length==1 && string.IsNullOrWhiteSpace(row[0]))continue;
                if(row.Length!=4 || string.IsNullOrWhiteSpace(row[0]) || !ids.Add(row[0]) || !int.TryParse(row[2],out int weight) || weight<0 || !bool.TryParse(row[3],out bool enabled))
                    throw new FormatException("加载文案 CSV 第 "+(i+1)+" 行无效。");
                result.Add(new LoadingTip{Id=row[0],Text=row[1],Weight=weight,Enabled=enabled});
            }
            Undo.RecordObject(config,"Import Loading Tips");config.Tips=result.ToArray();EditorUtility.SetDirty(config);AssetDatabase.SaveAssets();Debug.Log("[Spotlight] 加载文案已导入 "+result.Count+" 条。");
        }
        public static List<string[]> Parse(string csv)
        {
            var rows=new List<string[]>();var cells=new List<string>();var text=new StringBuilder();bool quoted=false;
            for(int i=0;i<csv.Length;i++)
            {
                char c=csv[i];
                if(c=='"'){if(quoted && i+1<csv.Length && csv[i+1]=='"'){text.Append('"');i++;}else quoted=!quoted;}
                else if(c==',' && !quoted){cells.Add(text.ToString());text.Clear();}
                else if((c=='\n'||c=='\r') && !quoted){if(c=='\r' && i+1<csv.Length && csv[i+1]=='\n')i++;cells.Add(text.ToString());text.Clear();rows.Add(cells.ToArray());cells.Clear();}
                else text.Append(c);
            }
            if(quoted)throw new FormatException("CSV 引号未闭合。");
            if(text.Length>0||cells.Count>0){cells.Add(text.ToString());rows.Add(cells.ToArray());}
            return rows;
        }
    }
}
#endif
