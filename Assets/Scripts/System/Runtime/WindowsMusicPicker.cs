using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TapTap.Systems.Music
{
    public static class WindowsMusicPicker
    {
        public sealed class Result { public string Path, Error; }
        public static Task<Result> Pick()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var completion = new TaskCompletionSource<Result>();
            IntPtr owner = GetActiveWindow();
            string originalDirectory = System.IO.Directory.GetCurrentDirectory();
            var thread = new Thread(() =>
            {
                IntPtr filter = IntPtr.Zero, buffer = IntPtr.Zero, title = IntPtr.Zero;
                Result result;
                try
                {
                    // Keep the structure blittable. Unity 2022 Mono cannot calculate
                    // Marshal.SizeOf for an OPENFILENAME containing StringBuilder.
                    filter = Marshal.StringToHGlobalUni("音频 (MP3 / WAV)\0*.mp3;*.wav\0\0");
                    title = Marshal.StringToHGlobalUni("导入歌曲");
                    buffer = Marshal.AllocHGlobal(32768 * 2); Marshal.Copy(new byte[32768 * 2], 0, buffer, 32768 * 2);
                    var dialog = new OpenFileName { Owner = owner, Filter = filter,
                        File = buffer, MaxFile = 32768, Title = title, FilterIndex = 1,
                        Flags = 0x00080000 | 0x00000008 | 0x00001000 | 0x00000800 | 0x02000000 };
                    dialog.Size = Marshal.SizeOf(typeof(OpenFileName));
                    if (GetOpenFileName(ref dialog)) result = new Result { Path = Marshal.PtrToStringUni(buffer) };
                    else { uint error = CommDlgExtendedError(); result = new Result { Error = error == 0 ? null : "文件选择失败 (" + error + ")。" }; }
                }
                catch (Exception e) { result = new Result { Error = "无法打开文件选择器：" + e.Message }; }
                finally
                {
                    if (filter != IntPtr.Zero) Marshal.FreeHGlobal(filter);
                    if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                    if (title != IntPtr.Zero) Marshal.FreeHGlobal(title);
                }
                // OFN_NOCHANGEDIR is not sufficient on all Windows shell versions.
                // Restore before publishing completion so callers keep stable paths.
                try { System.IO.Directory.SetCurrentDirectory(originalDirectory); }
                catch (Exception e) { result.Error = "无法恢复工作目录：" + e.Message; }
                completion.TrySetResult(result);
            }) { IsBackground = true, Name = "MusicFilePicker" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
#else
            return Task.FromResult(new Result { Error = "歌曲导入目前支持 Windows。" });
#endif
        }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int Size;
            public IntPtr Owner, Instance;
            public IntPtr Filter;
            public IntPtr CustomFilter;
            public int MaxCustomFilter, FilterIndex;
            public IntPtr File;
            public int MaxFile;
            public IntPtr FileTitle;
            public int MaxFileTitle;
            public IntPtr InitialDirectory, Title;
            public int Flags;
            public short FileOffset, FileExtension;
            public IntPtr DefaultExtension, CustomData, Hook, TemplateName, Reserved;
            public int ReservedValue, FlagsEx;
        }
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetOpenFileName(ref OpenFileName dialog);
        [DllImport("comdlg32.dll")] private static extern uint CommDlgExtendedError();
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
#endif
    }
}
