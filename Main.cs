using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace WrapperGenerator
{
    public partial class Main : Form
    {
        IWrapperBuilder[] Builders = (from Asm in AppDomain.CurrentDomain.GetAssemblies()
                                      from Typ in Asm.GetTypes()
                                      where typeof(IWrapperBuilder).IsAssignableFrom(Typ) && !Typ.IsInterface
                                      select (IWrapperBuilder)Activator.CreateInstance(Typ)).ToArray();

        IWrapperBuilder CurrentBuilder
        {
            get
            {
                return (from x in Builders where x.Name == CBoxMode.Text select x).Single();
            }
        }
        public Main()
        {
            InitializeComponent();

            foreach (var Builder in Builders)
                CBoxMode.Items.Add(Builder.Name);

            CBoxMode.SelectedIndex = 0;
        }

        private void SelectFileClicked(object sender, EventArgs e)
        {
            OpenFileDialog Dialog = new OpenFileDialog();
            Dialog.Filter = "All Supported Files|*.c;*.h;*.dll|All Files|*.*";
            Dialog.Title = "Select a File";
            if (Dialog.ShowDialog() != DialogResult.OK)
                return;

            tbFilePath.Text = Dialog.FileName;
            BeginInvoke(new MethodInvoker(async () => await PostFileSelect(Dialog.FileName)));
        }

        private void ModeChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tbFilePath.Text) || !File.Exists(tbFilePath.Text))
                return;
            BeginInvoke(new MethodInvoker(async () => await PostFileSelect(tbFilePath.Text)));
        }
        private void RegexChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tbFilePath.Text) || !File.Exists(tbFilePath.Text))
                return;
            BeginInvoke(new MethodInvoker(async () => await PostFileSelect(tbFilePath.Text)));
        }

        string LastFile;
        async Task PostFileSelect(string FileName)
        {
            IntPtr Handler = IntPtr.Zero;
            string[] Symbols = new string[0];

            if (Path.GetExtension(FileName).ToLower() == ".dll")
            {
                Symbols = GetExports(FileName);
                Handler = LoadLibraryW(FileName);

                if (Marshal.GetLastWin32Error() == 0x000000c1 && LastFile != FileName)
                    MessageBox.Show("This Library isn't to the current architeture of the WrapperGenerator instance.", "WrapperGenerator", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                LastFile = FileName;
                FileName = await Decompile(FileName);
            }

            if (!File.Exists(FileName))
            {
                MessageBox.Show("Failed to Open the File:\n" + FileName, "WrapperGenerator", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string[] Source = await File.ReadAllLinesAsync(FileName);

            SourceParser Parser = new SourceParser(Source);
            var Functions = (from x in Parser.Parse()
                             where
                                   !x.Name.StartsWith("sub_") &&
                                   !x.Name.StartsWith("SEH_")
                             select x).ToArray();


            if (Handler != IntPtr.Zero)
                Functions = (from x in Functions
                             where
                                   GetProcAddress(Handler, x.Name) != IntPtr.Zero || Symbols.Where(z => z.Equals(x.Name, StringComparison.InvariantCultureIgnoreCase)).Any()
                             select x).ToArray();

            if (tbRegex.Text.Trim() != string.Empty && IsValidRegex(tbRegex.Text))
                Functions = (from x in Functions
                             where
                                   Regex.IsMatch(x.Name, tbRegex.Text) ||
                                   Regex.IsMatch(x.ToString(), tbRegex.Text)
                             select x).ToArray();

            var Builder = CurrentBuilder;

            tbCodeBox.Text = Builder.BuildWrapper(Path.GetFileNameWithoutExtension(FileName), Functions);
        }

        private async Task<string> Decompile(string FileName)
        {
            string OutFile = Path.Combine(Path.GetDirectoryName(FileName), Path.GetFileNameWithoutExtension(FileName) + ".c");
            if (File.Exists(OutFile))
                return OutFile;

            Text = "Decompiling...";
            Enabled = false;
            bool x64 = MessageBox.Show("This is a x64 application?", "Decompiler", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            string IDA = SearchIDA(x64);
            if (IDA == null)
            {
                MessageBox.Show("Please, Decompile your library using IDA PRO and try again.", "IDA PRO Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            //-Ohexrays:-nosave:-new:outfile:ALL -A "C:\Users\Marcus\Documents\My Games\Ustrack\恋×シンアイ彼女\koikake.exe"

            ProcessStartInfo ProcSI = new ProcessStartInfo();
            ProcSI.FileName = IDA;
            ProcSI.Arguments = $"\"-Ohexrays:-nosave:-new:{Path.GetFileNameWithoutExtension(FileName)}:ALL\" -A \"{FileName}\"";
            ProcSI.WorkingDirectory = Path.GetDirectoryName(FileName);
            ProcSI.CreateNoWindow = true;
            ProcSI.UseShellExecute = false;

            var Proc = Process.Start(ProcSI);
            await Proc.WaitForExitAsync();

            Text = "WrapperGenerator";
            Enabled = true;

            return OutFile;
        }

        private string LastIDADir = null;
        private string SearchIDA(bool x64)
        {
            string[] Names = x64 ? new string[] { "idat64.exe", "idaw64.exe", "idaq64.exe", "ida64.exe" } : new string[] { "idat.exe", "idaw.exe", "idaq.exe", "ida.exe" };
            if (LastIDADir != null)
            {
                foreach (string Name in Names)
                {
                    string FullPath = Path.Combine(LastIDADir, Name);
                    if (File.Exists(FullPath))
                        return FullPath;
                }
            }


            var paths = SearchWindowPath(Names);
            if (paths.Count > 0)
                LastIDADir = paths.First();


            //string X64ProgFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).Replace(" (x86)", "");
            //string X86ProgFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            //X64ProgFiles = X64ProgFiles.Substring(3);
            //X86ProgFiles = X86ProgFiles.Substring(3);

            //List<string> AllProgramFiles = new List<string>();

            //foreach (DriveInfo Drive in DriveInfo.GetDrives())
            //{
            //    string ProgFilesPath = Path.Combine(Drive.RootDirectory.FullName, X64ProgFiles);
            //    if (Directory.Exists(ProgFilesPath))
            //        AllProgramFiles.AddRange(Directory.GetDirectories(ProgFilesPath));


            //    ProgFilesPath = Path.Combine(Drive.RootDirectory.FullName, X86ProgFiles);
            //    if (Directory.Exists(ProgFilesPath))
            //        AllProgramFiles.AddRange(Directory.GetDirectories(ProgFilesPath));
            //}

            //foreach (string Dir in AllProgramFiles)
            //{
            //    foreach (string Name in Names)
            //    {
            //        LastIDADir = Dir;
            //        string FullPath = Path.Combine(Dir, Name);
            //        if (File.Exists(FullPath))
            //            return FullPath;
            //    }
            //}

            return null;
        }

        /// <summary>
        /// if multi,return first
        /// </summary>
        /// <param name="key"></param>
        /// <param name="processName"></param>
        /// <returns></returns>
        public static List<string> SearchWindowPath(params string[] processNames)
        {
            Dictionary<IntPtr, WindowInfo> HwndAndPids = new Dictionary<IntPtr, WindowInfo>();

            EnumDesktopWindows(IntPtr.Zero, (hWnd, lParam) =>
            {
                var winInfo = GetWindowInfo(hWnd);
                if (winInfo.IsDefault) return true; //没拿到窗口信息,就直接返回了
                HwndAndPids.Add(hWnd, winInfo);
                return true;
            }, IntPtr.Zero);

            var paths = HwndAndPids.Values.Where(x => processNames.Any(y => x.ProcessName.Contains(y)))
                .Select(x => Process.GetProcessById(x.PID).MainModule.FileName);
            return paths.ToList();
        }

        public static WindowInfo GetWindowInfo(IntPtr hwnd)
        {
            if (GetWindowProcessId(hwnd, out var pid))
            {
                var proc = GetProcessById(pid);
                if (proc != null)
                    return new WindowInfo { Hwnd = hwnd, PID = pid, ProcessName = proc.ProcessName };
            }
            return default;
        }

        public static Process GetProcessById(int pid)
        {
            try
            {
                return Process.GetProcessById(pid);
            }
            catch (Exception e)
            {
                return null;
            }
        }
        public struct WindowInfo
        {
            public IntPtr Hwnd;
            public int PID;
            public string ProcessName;
            public bool IsDefault => Hwnd == default(IntPtr);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);

        public static bool GetWindowProcessId(IntPtr hwnd, out int pid) => GetWindowThreadProcessId(hwnd, out pid) != 0;

        // Code from https://pinvoke.net/default.aspx/user32/EnumWindows.html
        // and from https://www.experts-exchange.com/questions/24331722/Getting-the-list-of-Open-Window-Handles-in-C.html
        // and a condescending bit from http://csharphelper.com/blog/2016/08/list-desktop-windows-in-c/

        public delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "EnumDesktopWindows",
        ExactSpelling = false, CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsCallback lpEnumCallbackFunction, IntPtr lParam);

        private static bool IsValidRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;

            try
            {
                Regex.Match("", pattern);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return true;
        }

        private static string[] GetExports(string Module)
        {
            IntPtr hCurrentProcess = Process.GetCurrentProcess().Handle;

            ulong baseOfDll;
            bool status;

            // Initialize sym.
            // Please read the remarks on MSDN for the hProcess
            // parameter.
            status = SymInitialize(hCurrentProcess, null, false);

            if (status == false)
            {
                return null;
            }

            baseOfDll = SymLoadModuleEx(hCurrentProcess, IntPtr.Zero, Module, null, 0, 0, IntPtr.Zero, 0);

            if (baseOfDll == 0)
            {
                Console.Out.WriteLine("Failed to load module.");
                SymCleanup(hCurrentProcess);
                return null;
            }

            List<string> Exports = new List<string>();
            // Enumerate symbols. For every symbol the 
            // callback method EnumSyms is called.
            SymEnumerateSymbols64(hCurrentProcess, baseOfDll, (Name, Addr, Size, Context) =>
            {
                Exports.Add(Name);
                return true;
            }, IntPtr.Zero);

            // Cleanup.
            SymCleanup(hCurrentProcess);

            return Exports.ToArray();
        }
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibraryW(string FileName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymInitialize(IntPtr hProcess, string UserSearchPath, [MarshalAs(UnmanagedType.Bool)] bool fInvadeProcess);

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymCleanup(IntPtr hProcess);

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern ulong SymLoadModuleEx(IntPtr hProcess, IntPtr hFile,
            string ImageName, string ModuleName, long BaseOfDll, int DllSize, IntPtr Data, int Flags);

        [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymEnumerateSymbols64(IntPtr hProcess, ulong BaseOfDll, SymEnumerateSymbolsProc64Delegate EnumSymbolsCallback, IntPtr UserContext);

        //[UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate bool SymEnumerateSymbolsProc64Delegate(string SymbolName, ulong SymbolAddress, uint SymbolSize, IntPtr UserContext);
    }
}
