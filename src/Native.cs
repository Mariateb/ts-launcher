// TS Launcher - Windows interop: controller (XInput), process list with parents, .lnk resolution.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace TSLauncher
{
    public static class Pad
    {
        [StructLayout(LayoutKind.Sequential)]
        struct State { public uint Packet; public ushort Buttons; public byte LT; public byte RT; public short LX; public short LY; public short RX; public short RY; }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint Get14(uint i, out State s);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern uint Get910(uint i, out State s);
        static int api = 1;   // 1 = xinput1_4 (Win 8+), 2 = xinput9_1_0, 0 = none

        public const int Up = 0x1, Down = 0x2, Left = 0x4, Right = 0x8, Start = 0x10, Back = 0x20,
                         LB = 0x100, RB = 0x200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

        public static bool Read(int index, out int buttons, out int lx, out int ly)
        {
            buttons = 0; lx = 0; ly = 0;
            var s = new State(); uint r = 1167;
            if (api == 1) { try { r = Get14((uint)index, out s); } catch (Exception) { api = 2; } }
            if (api == 2) { try { r = Get910((uint)index, out s); } catch (Exception) { api = 0; } }
            if (api == 0 || r != 0) return false;
            buttons = s.Buttons; lx = s.LX; ly = s.LY;
            return true;
        }
    }

    public sealed class ProcInfo
    {
        public int Pid, ParentPid;
        public string Name;          // e.g. th17.exe
        public string Path;          // full image path when readable
        public DateTime? Created;
    }

    public static class Procs
    {
        const uint TH32CS_SNAPPROCESS = 0x2;
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32W
        {
            public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);

        /// <summary>All processes created after 'since' (with parent ids, paths and creation times).</summary>
        public static List<ProcInfo> CreatedSince(DateTime since)
        {
            var list = new List<ProcInfo>();
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
            try
            {
                var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W)) };
                if (!Process32FirstW(snap, ref e)) return list;
                do
                {
                    int pid = (int)e.th32ProcessID;
                    if (pid <= 4) continue;
                    var info = Describe(pid);
                    if (info == null || info.Created == null || info.Created.Value < since) continue;
                    info.ParentPid = (int)e.th32ParentProcessID;
                    info.Name = e.szExeFile;
                    list.Add(info);
                } while (Process32NextW(snap, ref e));
            }
            finally { CloseHandle(snap); }
            return list;
        }

        static ProcInfo Describe(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var p = new ProcInfo { Pid = pid };
                long c, x, k, u;
                if (GetProcessTimes(h, out c, out x, out k, out u)) p.Created = DateTime.FromFileTime(c);
                var sb = new StringBuilder(1024); int size = sb.Capacity;
                if (QueryFullProcessImageNameW(h, 0, sb, ref size)) p.Path = sb.ToString(0, size);
                return p;
            }
            finally { CloseHandle(h); }
        }

        public static DateTime? CreationTime(int pid)
        {
            var d = Describe(pid);
            return d == null ? null : d.Created;
        }

        /// <summary>True while the process is still running (false if it exited or can't be opened).</summary>
        public static bool IsAlive(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try { uint code; return GetExitCodeProcess(h, out code) && code == 259; }   // 259 = STILL_ACTIVE
            finally { CloseHandle(h); }
        }
    }

    public sealed class LinkInfo { public string Target, Arguments, WorkingDirectory; }

    public static class ShellLinks
    {
        /// <summary>Reads a .lnk through the WScript.Shell COM object (late bound, no interop assembly needed).</summary>
        public static LinkInfo Resolve(string lnk)
        {
            object shell = null, sc = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                shell = Activator.CreateInstance(t);
                sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                var st = sc.GetType();
                Func<string, string> get = n => Convert.ToString(st.InvokeMember(n, System.Reflection.BindingFlags.GetProperty, null, sc, null));
                return new LinkInfo { Target = get("TargetPath"), Arguments = get("Arguments"), WorkingDirectory = get("WorkingDirectory") };
            }
            catch { return null; }
            finally
            {
                if (sc != null && Marshal.IsComObject(sc)) Marshal.ReleaseComObject(sc);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
            }
        }
    }
}
