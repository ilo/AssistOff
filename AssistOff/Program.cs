using System;
using System.IO;
using System.Web.Script.Serialization;  // for JSON parsing
using System.Runtime.InteropServices;   // For WinAPI 

// quick program to detect if flight assist is on and simulate the 'h' key press to disable it.
// Go to settings > controls > miscelanous flight > Flight Assist; set toggle, and bind the 'h' key.
// Specify your user folder for the journal files:
// Assistoff.exe "C:\\Users\\ilo\\Saved Games\\Frontier Developments\\Elite Dangerous\\"


namespace AssistOff
{
    class Program
    {

        // Status.json file processing, we are only interested in tstamp and flag status.
        // http://hosting.zaonce.net/community/journal/v16/Journal_Manual_v16.pdf
        // Sample: { "timestamp":"2018-03-27T17:42:15Z", "event":"Status", "Flags":16777480, "Pips":[2,8,2], "FireGroup":2, "GuiFocus":0 } 
        class StatusInfo
        {
            public string timestamp { get; set; }
            public string Event { get; set; }
            public int flags { get; set; }
        }

        // Default key for Flight Assist is 'h', using that scancode for the key press simulation
        // https://www.win.tue.nl/~aeb/linux/kbd/scancodes-1.html
        static ushort DEFAULT_KEY = 0x23;

        // Only considering those flags in use for the different cases
        enum StatusFlags
        {
            STATUS_DOCKED       = 0x00000001,
            STATUS_LANDINGEAR   = 0x00000004,
            STATUS_FLIGHTASSIST = 0x00000020,
            STATUS_FSDCOOLDOWN  = 0x00040000,
            STATUS_INSRV        = 0x04000000,
        }

        // Struct used by SendInput, tweaked for keyboard emulation only, since mouse and other input
        // devices are not considered.
        struct INPUT
        {
            public UInt32 type;
            public ushort wVk;
            public ushort wScan;
            public UInt32 dwFlags;
            public UInt32 time;
            public UIntPtr dwExtraInfo;
            public UInt32 uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        // Struct used by SendInput
        enum SendInputFlags
        {
            KEYEVENTF_EXTENDEDKEY = 0x0001,
            KEYEVENTF_KEYUP       = 0x0002,
            KEYEVENTF_UNICODE     = 0x0004,
            KEYEVENTF_SCANCODE    = 0x0008,
        }

        // Import SendInput Windows API
        [DllImport("user32.dll")]
        static extern UInt32 SendInput(UInt32 nInputs,
          [MarshalAs(UnmanagedType.LPArray, SizeConst = 1)] INPUT[] pInputs, Int32 cbSize);

        // Simulate key press and release
        // ScanCode: hardware scancode of key to be sent
        // https://stackoverflow.com/questions/12899622/how-do-i-send-release-key-with-directinput
        public static void SimulateKeyPress( ushort ScanCode )
        {
            INPUT[] InputData = new INPUT[1];

            // Prepare and send key press
            InputData[0].type = 1; //INPUT_KEYBOARD
            InputData[0].wScan = (ushort)ScanCode;
            InputData[0].dwFlags = (uint)SendInputFlags.KEYEVENTF_SCANCODE;
            SendInput(1, InputData, Marshal.SizeOf(InputData[0]));

            // Hold key for 0.1 secs
            System.Threading.Thread.Sleep(100);

            // Prepare and send key release
            InputData[0].dwFlags = (uint)(SendInputFlags.KEYEVENTF_KEYUP);
            SendInput(1, InputData, Marshal.SizeOf(InputData[0]));
        }

        // Main program function
        static void Main()
        {
            string[] args = System.Environment.GetCommandLineArgs();

            // If a directory is not specified, exit program.
            if (args.Length < 2)
            {
                // Display the proper way to call the program.
                Console.WriteLine("Usage: AssistOff.exe path)");
                Console.WriteLine(" path: location of Elite: Dangerous Status.json file");
                Console.WriteLine(" usually \"C:\\Users\\User Name\\Saved Games\\Frontier Developments\\Elite Dangerous\\\"");
                return;
            }

            // Create a new FileSystemWatcher for the desired folder and set its properties.
            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.Path = args[1];

            // Watch for changes in LastAccess and LastWrite times, and the renaming of files or directories.
            watcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            
            // Only watch the status json file 
            watcher.Filter = "*.json";

            // Add event handlers, file new created and changed
            watcher.Changed += new FileSystemEventHandler(OnChanged);
            watcher.Created += new FileSystemEventHandler(OnChanged);

            // Begin watching.
            watcher.EnableRaisingEvents = true;

            // Wait for the user to quit the program.
            Console.WriteLine("Press \'q\' to quit the program.");
            while (Console.Read() != 'q') ;
        }


        // Define the event handlers, we want to track when the status.json file is being changed to 
        // re-enable Flight Assist in case it is being switched on automatically.
        private static void OnChanged(object source, FileSystemEventArgs e)
        {

            // Specify what is done when a file is changed, created, or deleted, double check it is 
            // the Status.json file, 
            if (e.Name == "Status.json")
            {
                try {
                    using (StreamReader r = new StreamReader(e.FullPath))
                    {
                        // Read the json file.
                        var json = r.ReadToEnd();
                        var JSONObj = new JavaScriptSerializer().Deserialize<StatusInfo>(json);
                        Console.WriteLine("Status - Docked: {0} - Landing: {1} - InSRV: {2} - FA Off: {3}",
                            (JSONObj.flags & (int)StatusFlags.STATUS_DOCKED) > 0,
                            (JSONObj.flags & (int)StatusFlags.STATUS_LANDINGEAR) > 0,
                            (JSONObj.flags & (int)StatusFlags.STATUS_INSRV) > 0,
                            (JSONObj.flags & (int)StatusFlags.STATUS_FLIGHTASSIST) > 0
                        );

                        // Main case: flight assist is ON and the landing gear is not deployed and player is not in the SRV
                        if ( (JSONObj.flags & (int)StatusFlags.STATUS_FLIGHTASSIST) == 0 &&
                             (JSONObj.flags & (int)StatusFlags.STATUS_LANDINGEAR)   == 0 &&
                             (JSONObj.flags & (int)StatusFlags.STATUS_INSRV)        == 0
                           ) {

                            Console.WriteLine("Disabling Flight Assist");
                            // Sending the H key, for other keys see the link 
                            // https://www.win.tue.nl/~aeb/linux/kbd/scancodes-1.html
                            SimulateKeyPress(DEFAULT_KEY);
                        }

                    }
                } catch (Exception ex)
                {
                    // throttling because our program might be trying to read the file while
                    // still being saved by Elite Dangerous, throwing exceptions every now and then.
                }
            }
        }
    }
}

