#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TapTap.Systems.Editor
{
    public static class SpotlightValidation
    {
        private static readonly List<string> report=new();
        public static void GenerateAndValidate()
        { SpotlightAssetBuilder.Generate();Run();TapTap.Danmaku.Editor.DanmakuValidation.Run(); }
        [MenuItem("Tools/Spotlight/运行系统验证")]
        public static void Run()
        {
            report.Clear();
            var config=AssetDatabase.LoadAssetAtPath<SystemConfig>("Assets/Configs/Spotlight/SystemConfig.asset");
            Require(config && config.Controls && config.UIPrefab,"系统配置引用完整");
            Test("绑定默认值/冲突/恢复",()=>Bindings(config));
            Test("声音比例与试听回滚",()=>AudioAndSettings(config));
            Test("保存失败回滚",()=>SaveFailure(config));
            Test("局外存档/损坏/版本",()=>Progress(config));
            Test("暂停句柄与原播放状态",Pause);
            Test("命令一次消费",Commands);
            Test("1/5/10/20/30/60/120 FPS 保持60Hz时钟",RenderRates);
            Test("CSV引号/逗号/换行",Csv);
            Test("Prefab可编辑引用/场景入口",()=>Assets(config));
            Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/SpotlightValidation.txt",report);Debug.Log(string.Join("\n",report));
            if(report.Exists(s=>s.StartsWith("FAIL",StringComparison.Ordinal)))throw new Exception("Spotlight 系统验证失败。");
        }
        private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        private static void Test(string name,Action action){try{action();report.Add("PASS "+name);}catch(Exception e){report.Add("FAIL "+name+": "+e.Message);}}
        private static void Bindings(SystemConfig config)
        {
            var asset=UnityEngine.Object.Instantiate(config.Controls);var service=new BindingService(asset);
            Require(service.Validate(asset).Success,"默认键位无效");
            Require(asset.FindAction("Gameplay/Skill1").bindings[0].effectivePath=="<Keyboard>/q","Q 默认错误");
            Require(!service.TrySet(asset,"Skill1","<Keyboard>/e").Success,"冲突未拒绝");
            Require(!service.TrySet(asset,"Skill1","<Keyboard>/escape").Success,"Esc 可绑定");
            Require(service.TrySet(asset,"Skill1","<Keyboard>/j").Success,"合法改键失败");
            string json=asset.SaveBindingOverridesAsJson();asset.RemoveAllBindingOverrides();
            Require(service.Load(asset,json).Success && asset.FindAction("Gameplay/Skill1").bindings[0].effectivePath=="<Keyboard>/j","改键读写不一致");
            Require(!service.Load(asset,"bad-json").Success,"坏键位未识别");UnityEngine.Object.DestroyImmediate(asset);
        }
        private static string TemporaryDirectory()=>Path.Combine(Path.GetTempPath(),"SpotlightChecks",Guid.NewGuid().ToString("N"));
        private static void AudioAndSettings(SystemConfig config)
        {
            var asset=UnityEngine.Object.Instantiate(config.Controls);var binding=new BindingService(asset);var audio=new AudioVolumeService();var display=new FakeDisplay();
            var go=new GameObject("AudioCheck");var source=go.AddComponent<AudioSource>();source.volume=.8f;audio.Register(source,AudioChannel.Music);
            var store=new JsonFileStore(TemporaryDirectory());var service=new SettingsService(store,config.Defaults,binding,audio,display);
            var session=service.BeginEdit();session.Controls.Disable();session.Draft.MasterVolume=50;session.Draft.MusicVolume=50;session.Draft.VolumeScale=.2f;service.Preview(session);
            Require(Mathf.Abs(source.volume-.2f)<.0001f,"音量应是基础×总×音乐");service.Cancel(session);Require(Mathf.Abs(source.volume-.8f)<.0001f,"取消未回滚音量");
            session=service.BeginEdit();session.Controls.Disable();session.Draft.RenderFps=30;session.Draft.MusicVolume=35;bool complete=false;
            var iterator=service.Apply(session,r=>{Require(r.Success,r.Error);complete=true;});while(iterator.MoveNext()){}
            Require(complete && store.Read<SettingsData>("settings.json").MusicVolume==35,"应用未保存");
            session.Draft.MusicVolume=1;service.Cancel(session);Require(service.Current.MusicVolume==35,"取消撤销了已应用设置");
            session=service.BeginEdit();service.Reset(session);Require(session.Draft.MusicVolume==100,"重置未回默认");service.Cancel(session);
            Require(store.Read<SettingsData>("settings.json").MusicVolume==35,"重置未应用污染文件");
            audio.Unregister(source);audio.Register(source,AudioChannel.Music);
            Require(Mathf.Abs(source.volume-.28f)<.0001f,"音源重新启用后重复缩放");
            UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(asset);
        }
        private static void SaveFailure(SystemConfig config)
        {
            string blocker=TemporaryDirectory();Directory.CreateDirectory(Path.GetDirectoryName(blocker));File.WriteAllText(blocker,"blocked");
            var asset=UnityEngine.Object.Instantiate(config.Controls);var display=new FakeDisplay();var service=new SettingsService(new JsonFileStore(blocker),config.Defaults,new BindingService(asset),new AudioVolumeService(),display);
            var session=service.BeginEdit();session.Controls.Disable();session.Draft.RenderFps=30;bool failed=false;
            var iterator=service.Apply(session,r=>failed=!r.Success);while(iterator.MoveNext()){}
            Require(failed && service.Current.RenderFps==60 && display.Last.RenderFps==60,"保存失败未回退");service.Cancel(session);UnityEngine.Object.DestroyImmediate(asset);
        }
        private static void Progress(SystemConfig config)
        {
            var store=new JsonFileStore(TemporaryDirectory());var service=new ProgressSaveService(store,config.Levels);
            Require(service.Status==SaveStatus.Missing,"首次进入不是无档");Require(service.Save(config.Levels.FirstEntryId,new[]{new ProgressValue{Id="growth",Value=7}}).Success,"保存失败");
            service.Inspect();Require(service.Status==SaveStatus.Valid && service.Current.CharacterValues[0].Value==7,"继续数据丢失");
            store.Write("progress.json",new ProgressSaveData{Version=99,LevelEntryId=config.Levels.FirstEntryId});service.Inspect();Require(service.Status==SaveStatus.UnsupportedVersion,"版本未检查");
            File.WriteAllText(Path.Combine(store.DirectoryPath,"progress.json"),"{broken");service.Inspect();Require(service.Status==SaveStatus.Corrupt,"损坏未检查");
        }
        private static void Pause()
        {
            var service=new PauseService();var running=new FakeTarget{Running=true};var stopped=new FakeTarget();service.Register(running);service.Register(stopped);
            var a=service.Acquire();var b=service.Acquire();Require(!running.Running,"未暂停");a.Dispose();Require(!running.Running,"嵌套时提前恢复");b.Dispose();
            Require(running.Running&&!stopped.Running,"恢复了原先停止目标");b.Dispose();Require(running.Resumes==1,"重复释放重复恢复");
        }
        private static void Commands(){var buffer=new GameplayCommandBuffer();buffer.Queue(GameplayCommand.Skill1);Require(buffer.TryConsume(out _)&&!buffer.TryConsume(out _),"命令重复消费");buffer.Queue(GameplayCommand.UseItem);buffer.Clear();Require(buffer.Count==0,"清理未生效");}
        private static void RenderRates()
        {
            foreach(int fps in new[]{1,5,10,20,30,60,120})
            {
                var clock=new TapTap.Danmaku.DanmakuTickClock();int ticks=0;
                for(int frame=0;frame<fps*2;frame++){clock.AddTime(1.0/fps);while(clock.ConsumeTick())ticks++;}
                Require(ticks==120,fps+" FPS 的2秒不是120 Tick");
            }
        }
        private static void Csv(){var rows=LoadingTipImporter.Parse("Id,Text,Weight,Enabled\r\na,\"逗号,与\"\"引号\"\"\n换行\",1,true\n");Require(rows.Count==2&&rows[1][1]=="逗号,与\"引号\"\n换行","CSV 内容丢失");}
        private static void Assets(SystemConfig config)
        {
            Require(config.UIPrefab.Settings.KeyRows.Length==9,"键位行数不符");Require(config.UIPrefab.Hud.Slots.Length==10,"道具槽数不符");
            Require(config.UIPrefab.Settings.Volumes.Length==4,"缺第四项音量");
            foreach(var text in config.UIPrefab.GetComponentsInChildren<TMP_Text>(true))Require(text.font!=null,"字体未指定: "+text.name);
            var scenes=EditorBuildSettings.scenes;foreach(string path in new[]{config.TitleScene,config.LoadingScene,config.Levels.Entries[0].ScenePath})Require(Array.Exists(scenes,s=>s.enabled&&s.path==path),"构建场景缺失: "+path);
        }
        private sealed class FakeDisplay:IDisplaySettings
        {public SettingsData Last;public bool Supports(int w,int h,DisplayMode m)=>w>0&&h>0;public void Apply(SettingsData d)=>Last=d.Copy();public void CaptureActual(SettingsData d){} }
        private sealed class FakeTarget:IPauseTarget
        {public bool Running;public int Resumes;public bool IsRunning=>Running;public void Pause()=>Running=false;public void Resume(){Running=true;Resumes++;}}
        public static void BuildPC()
        {
            Directory.CreateDirectory("Builds/SpotlightPC");
            var scenes=new List<string>();foreach(var scene in EditorBuildSettings.scenes)if(scene.enabled)scenes.Add(scene.path);
            var result=BuildPipeline.BuildPlayer(scenes.ToArray(),"Builds/SpotlightPC/Spotlight.exe",BuildTarget.StandaloneWindows64,BuildOptions.Development);
            File.WriteAllText("Logs/SpotlightPCBuild.txt",result.summary.result+" | errors="+result.summary.totalErrors+" | size="+result.summary.totalSize);
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("PC 构建失败");
        }
    }
}
#endif
