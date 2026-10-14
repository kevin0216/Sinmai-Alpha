using HarmonyLib;
using Manager.MaiStudio;
using Manager;
using SinmaiAlpha.Hosting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System;

namespace SinmaiAlpha.Difficulty;

public static partial class ExtraDifficulty
{
    [HarmonyPatch(typeof(DataManager), "LoadMusicBase")]
    public static class ThemeIndexPatch
    {
        [HarmonyPrepare]
        public static bool Prepare()
        {
            musicsField = AccessTools.Field(typeof(DataManager), "_musics");
            if (AccessTools.Method(typeof(DataManager), "LoadMusicBase") != null)
            {
                return musicsField != null;
            }
            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(DataManager __instance)
        {
            try
            {
                if (System.Threading.Interlocked.Exchange(ref themeIndexStarted, 1) == 0)
                {
                    BuildThemeIndex(musicsField?.GetValue(__instance) as IEnumerable<KeyValuePair<int, MusicData>>);
                }
            }
            catch (Exception ex)
            {
                // A raw loader thread: nothing may escape. noThemes stays false, which is the old behaviour.
                if (!(ex is System.Threading.ThreadAbortException))
                {
                    SinmaiAlpha.Compatibility.SafeLog.Msg("[ExtraDifficulty] Flag index skipped, theme hooks stay active: " + ex.Message);
                }
            }
        }
    }

    private static class FlagWatch
    {
        private const uint NotifyFileName = 1u;

        private const uint NotifyDirName = 2u;

        private const uint WaitTimeout = 258u;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "FindFirstChangeNotificationW", ExactSpelling = true)]
        private static extern IntPtr FindFirstChangeNotification(string lpPathName, bool bWatchSubtree, uint dwNotifyFilter);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern bool FindCloseChangeNotification(IntPtr hChangeHandle);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern uint WaitForMultipleObjects(uint nCount, IntPtr[] lpHandles, bool bWaitAll, uint dwMilliseconds);

        internal static bool Add(List<IntPtr> handles, string folder, bool subtree)
        {
            IntPtr intPtr = FindFirstChangeNotification(folder, subtree, NotifyFileName | NotifyDirName);
            if (intPtr == IntPtr.Zero || intPtr == new IntPtr(-1))
            {
                return false;
            }
            handles.Add(intPtr);
            return true;
        }

        internal static void Close(List<IntPtr> handles)
        {
            foreach (IntPtr handle in handles)
            {
                FindCloseChangeNotification(handle);
            }
            handles.Clear();
        }

        // Takes ownership of the handles. Sets noThemes back to false when a folder changes or anything fails.
        internal static void Start(List<IntPtr> handles)
        {
            IntPtr[][] array = new IntPtr[(handles.Count + 63) / 64][];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = handles.Skip(i * 64).Take(64).ToArray();
            }
            new System.Threading.Thread(Run)
            {
                IsBackground = true,
                Name = "Sinmai-Alpha flag watch"
            }.Start(array);
        }

        private static void Run(object state)
        {
            IntPtr[][] array = (IntPtr[][])state;
            try
            {
                while (true)
                {
                    for (int i = 0; i < array.Length; i++)
                    {
                        // Finite waits keep the thread abortable at shutdown.
                        if (WaitForMultipleObjects((uint)array[i].Length, array[i], bWaitAll: false, (i == 0) ? 500u : 0u) != WaitTimeout)
                        {
                            noThemes = false;
                            SinmaiAlpha.Compatibility.SafeLog.Msg("[ExtraDifficulty] A watched option folder changed; theme hooks active");
                            return;
                        }
                    }
                }
            }
            catch (Exception)
            {
                noThemes = false;
            }
            finally
            {
                foreach (IntPtr[] handles in array)
                {
                    foreach (IntPtr handle in handles)
                    {
                        FindCloseChangeNotification(handle);
                    }
                }
            }
        }
    }

    private static volatile bool noThemes;

    private static int themeIndexStarted;

    private static FieldInfo musicsField;

    private static string ChartFolder(string chartPath)
    {
        if (string.IsNullOrWhiteSpace(chartPath))
        {
            return null;
        }
        string directoryName = System.IO.Path.GetDirectoryName(FileSystem.ResolvePath(chartPath));
        if (string.IsNullOrEmpty(directoryName))
        {
            return null;
        }
        return directoryName;
    }

    private static bool FolderHasFlag(string folder)
    {
        return Directory.GetFiles(folder, "*" + MarkerFileName).Length != 0;
    }

    private static string WatchFolder(string musicFolder)
    {
        if (!Directory.Exists(musicFolder))
        {
            return null;
        }
        string directoryName = System.IO.Path.GetDirectoryName(musicFolder);
        string text = (string.IsNullOrEmpty(directoryName) ? null : System.IO.Path.GetDirectoryName(directoryName));
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(System.IO.Path.GetDirectoryName(text)) || IsJunction(musicFolder) || IsJunction(directoryName))
        {
            return musicFolder;
        }
        return text;
    }

    private static bool IsJunction(string folder)
    {
        return (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;
    }

    private static void BuildThemeIndex(IEnumerable<KeyValuePair<int, MusicData>> musics)
    {
        if (musics == null)
        {
            return;
        }
        int num = 0;
        HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<int, MusicData> music in musics)
        {
            // ThemeFor's own range: 0 < id < 100000, difficulties 0-4 that exist in notesData (CustomLevelFlags
            // uses the same range).
            if (music.Key <= 0 || music.Key >= 100000)
            {
                continue;
            }
            num++;
            var notesData = music.Value?.notesData;
            if (notesData == null)
            {
                continue;
            }
            for (int i = 0; i <= 4 && i < notesData.Count; i++)
            {
                // Throws where MarkerTheme would throw or warn: the gate then stays open.
                string text = ChartFolder(notesData[i]?.file?.path);
                if (text != null)
                {
                    hashSet.Add(text);
                }
            }
        }
        // An empty table (a skipped or rerouted load) proves nothing.
        if (num == 0 || hashSet.Count == 0)
        {
            return;
        }
        List<IntPtr> list = new List<IntPtr>();
        bool flag = false;
        try
        {
            // Watch first, read second, so a flag created during the scan is either listed or signalled.
            HashSet<string> hashSet2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string item in hashSet)
            {
                string directoryName = System.IO.Path.GetDirectoryName(item);
                if (string.IsNullOrEmpty(directoryName))
                {
                    return;
                }
                hashSet2.Add(directoryName);
            }
            HashSet<string> watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string item2 in hashSet2)
            {
                string text = WatchFolder(item2);
                if (text == null)
                {
                    return;
                }
                watched.Add(text);
            }
            foreach (string folder in watched)
            {
                if (!FlagWatch.Add(list, folder, subtree: true))
                {
                    return;
                }
            }
            foreach (string item3 in hashSet)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(item3);
                }
                catch (FileNotFoundException)
                {
                    continue;
                }
                catch (DirectoryNotFoundException)
                {
                    continue;
                }
                // A subtree watch does not follow a junction, so watch its target too.
                if ((attributes & FileAttributes.ReparsePoint) != 0 && !FlagWatch.Add(list, item3, subtree: false))
                {
                    return;
                }
                if (FolderHasFlag(item3))
                {
                    return;
                }
            }
            noThemes = true;
            try
            {
                FlagWatch.Start(list);
                flag = true;
            }
            catch (Exception)
            {
                noThemes = false;
                throw;
            }
            SinmaiAlpha.Compatibility.SafeLog.Msg("[ExtraDifficulty] No " + MarkerFileName + " next to any of " + num + " songs; theme hooks idle until an option folder changes");
        }
        finally
        {
            if (!flag)
            {
                FlagWatch.Close(list);
            }
        }
    }
}
