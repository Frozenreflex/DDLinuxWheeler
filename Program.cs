using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Gdk;
using Gtk;
using SharpHook;
using SharpHook.Native;
using Window = Gdk.Window;

namespace DDLinuxWheeler;

public static class Program
{
    
    private static Window _window;
    //private static readonly EventSimulator Simulator = new();
    private static readonly SimpleGlobalHook Hook = new();
    private static bool _busy;
    private static bool _debug;
    private static int _tries = 200;
    private static int _wait = 500;
    
    //color values taken from existing wheeler script
    public static readonly ByteColor Potion = new(0x64, 0x00, 0x00);
    public static readonly ByteColor Sword = new(0x5F, 0xAE, 0xCC);
    public static readonly ByteColor Crystal = new(0x06, 0xE2, 0xFE);
    public static readonly ByteColor Goblin = new(0x23, 0x26, 0x00);
    
    //mana was missing from the existing wheeler script for some reason
    public static readonly ByteColor Mana = new(0x22, 0x03, 0x64);

    //beneficial effects
    public static readonly WheelDefinition DebuffEnemies = new(Goblin, Goblin, Crystal);
    public static readonly WheelDefinition BuffPlayers = new(Crystal, Crystal, Crystal);
    public static readonly WheelDefinition DamagePercent = new(Sword, Sword, Sword);
    public static readonly WheelDefinition KillPercent = new(Sword, Sword, Crystal);
    public static readonly WheelDefinition Stun = new(Goblin, Goblin, Potion);
    public static readonly WheelDefinition SlowMotion = new(Potion, Potion, Mana);
    public static readonly WheelDefinition Upgrade = new(Mana, Mana, Mana);
    public static readonly WheelDefinition Heal = new(Potion, Potion, Potion);
    
    //negative effects
    public static readonly WheelDefinition GoldenEnemies = new(Goblin, Goblin, Goblin);
    public static readonly WheelDefinition BuffEnemies = new(Goblin, Crystal, Crystal);
    public static readonly WheelDefinition Downgrade = new(Goblin, Mana, Mana);
    public static readonly WheelDefinition NoRepair = new(Sword, Mana, Mana);

    public static readonly List<MacroDefinitionInformation> AvailableMacros = new()
    {
        new("debuffenemies", "Debuff Enemies", DebuffEnemies),
        new("buffplayers", "Buff Players", BuffPlayers),
        new("damagepercent", "Damage Percent", DamagePercent),
        new("killpercent", "Kill Percent", KillPercent),
        new("stun", "Stun", Stun),
        new("slowmotion", "Slow Motion", SlowMotion),
        new("upgrade", "Upgrade", Upgrade),
        new("heal", "Heal", Heal),
        
        new("goldenenemies", "Golden Enemies", GoldenEnemies),
        new("buffenemies", "Buff Enemies", BuffEnemies),
        new("downgrade", "Downgrade", Downgrade),
        new("norepair", "No Repair", NoRepair),
    };
    public static readonly Dictionary<KeyCode, MacroDefinitionInformation> ActiveMacros = new();

    private static void Main(string[] args)
    {
        //TODO: im not super happy with this
        var lowerArgs = args.Select(i => i.ToLower()).ToList();
        if (lowerArgs.Any(i => i is "-d" or "--debug")) _debug = true;
        var len = args.Length;
        for (var i = 0; i < len; i++)
        {
            var item = lowerArgs[i];
            if (i + 1 >= len) continue;
            switch (item)
            {
                case "--trytime" when float.TryParse(args[i+1], out var f):
                    _tries = (int) f * 100;
                    break;
                case "-w" or "--wait" when float.TryParse(args[i+1], out var f2):
                    _wait = (int) f2 * 1000;
                    break;
            }
            foreach (var macro in AvailableMacros.Where(macro => item == "--" + macro.ArgName.ToLower()))
            {
                if (Enum.TryParse<KeyCode>("Vc" + args[i + 1], out var key)) ActiveMacros.Add(key, macro);
            }
        }
        
        
        if (args.Any(i => i is "-h" or "--help") || ActiveMacros.Count == 0)
        {
            Console.WriteLine("usage: DDLinuxWheeler [options] <macro> <macrokey> ...");
            Console.WriteLine("  options:");
            Console.WriteLine("    -h, --help          Shows this");
            Console.WriteLine("    -d, --debug         Enables printing debug information");
            Console.WriteLine("    --trytime <float>   Sets the amount of time, in seconds, that a roll can be attempted, default 2 seconds");
            Console.WriteLine("    -w, --wait <float>  Sets the amount of time, in seconds, to wait before the first roll is attempted, default 0.5 seconds");
            Console.WriteLine("  macros:");
            Console.WriteLine("    heal, upgrade, slowmotion, stun, killpercent, damagepercent, buffplayers, debuffenemies, goldenenemies, buffenemies, downgrade, norepair");
            Console.WriteLine("\nexample: DDLinuxWheeler --trytime 1.5 --heal 3 --buffplayers 4 --upgrade 5");
            Console.WriteLine("\nList of valid keycodes available here (https://github.com/TolikPylypchuk/SharpHook/blob/master/SharpHook/Native/KeyCode.cs), remove \"Vc\" to get the correct input");
            Console.WriteLine("Control + Key to use macro");
            Console.WriteLine("Warning: due to a bug with the input library I'm using (https://github.com/kwhat/libuiohook/issues/150), key inputs are not suppressed, if you want to\nuse this over the number hotkeys you will have to replace them with Wheel O' Fortuna or the script will fail!\n\n");
            return;
        }
        
        Application.Init();
        Console.WriteLine("DDLinuxWheeler by Frozenreflex\nIn 10 seconds, the active window will be selected, be sure to have Dungeon Defenders as your active window!");
        Thread.Sleep(10000);
        
        //why is this obsoleted?
#pragma warning disable CS0612
        _window = Screen.Default.ActiveWindow;
#pragma warning restore CS0612
        
        Console.WriteLine("Active window selected, have fun wheeling! Press any key in this window to close");
        Hook.KeyPressed += HookOnKeyPressed;
        //todo: im not sure if the hook needs to be on another thread or the readkey, input supression doesn't work
        //on linux yet so whenever it does check this
        var exitThread = new Thread(() =>
        {
            Console.ReadKey();
            Hook.Dispose();
            Environment.Exit(0);
        });
        exitThread.Start();
        Hook.Run();
    }

    private static void HookOnKeyPressed(object sender, KeyboardHookEventArgs e)
    {
        if ((e.RawEvent.Mask & ModifierMask.Ctrl) <= 0 || !ActiveMacros.TryGetValue(e.Data.KeyCode, out var v)) return;
        e.SuppressEvent = true;

        if (_busy) return;
        var thread = new Thread(() =>
        {
            Wheel(v.Definition, v.PrintName);
        });
        thread.Start();
    }

    private static void PressKey(string key)
    {
        //this is more reliable than eventsimulator, ?????
        DebugLog("Pressing key " + key);
        var proc = new System.Diagnostics.Process
        {
            StartInfo =
            {
                FileName = "xdotool",
                Arguments = "key " + key,
                UseShellExecute = false,
                RedirectStandardError = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false
            }
        };
        proc.Start();
    }

    private static IEnumerable<ByteColor> GetColors(int x, int y, int width, int height)
    {
        var pixbuf = new Pixbuf(_window, x, y, width, height);
        var data = pixbuf.ReadPixelBytes().Data;
        var count = data.Length / 3;
        var result = new List<ByteColor>(count);
        for (var i = 0; i < count; i++) result.Add(new ByteColor(data[3 * i], data[3 * i + 1], data[3 * i + 2]));
        return result;
    }

    //rough replication of autohotkey's PixelSearch function
    private static bool PixelSearch(int x1, int y1, int x2, int y2, ByteColor color, int variation = 2) =>
        GetColors(x1, y1, x2 - x1, y2 - y1).Any(c => color.Compare(c, variation));

    private static bool SlotMatch(int x1, int y1, int x2, int y2, ByteColor color)
    {
        DebugLog($"Looking for slot match at x1 {x1} y1 {y1} x2 {x2} y2 {y2}");
        var failState = _tries;
        while (failState > 0)
        {
            if (PixelSearch(x1, y1, x2, y2, color))
            {
                PressKey("space");
                Thread.Sleep(50);
                return true;
            }
            Thread.Sleep(10);
            failState--;
        }
        return false;
    }

    private static void DebugLog(string content)
    {
        if (_debug) Console.WriteLine(content);
    }

    private static void Wheel(ByteColor one, ByteColor two, ByteColor three)
    {
        _busy = true;
        Console.WriteLine(DoWheel(one, two, three) ? "Finished wheeling successfully" : "Took too long to wheel");
        _busy = false;
    }

    private static void Wheel(WheelDefinition def) => Wheel(def.One, def.Two, def.Three);

    private static void Wheel(WheelDefinition def, string name)
    {
        Console.WriteLine("Wheeling " + name);
        Wheel(def);
    }
    private static bool DoWheel(ByteColor one, ByteColor two, ByteColor three)
    {
        _window.GetGeometry(out var x, out var y, out var width, out var height);
        PressKey("3");
        Thread.Sleep(_wait);
        
        //these values are computed for 1080p, so 1920 width, i dont know how the wheel scales with res, but the
        //other wheeler script's x values didn't work well
        const int aspect = 1920;
        
        //the leftmost border of the wheel
        const int start = 660;
        
        //the rightmost border of the wheel
        const int end = 1260;
        
        //the total width of the wheel
        const int totalWidth = end - start;
        
        //the width of each roller in the wheel
        const int partWidth = totalWidth / 3;
        
        //how much padding between each edge, to prevent errors
        const int padding = 10;
        
        var x1 = width * (start + padding) / aspect;
        var x2 = width * (start + partWidth - padding) / aspect;

        //magic constants from the other wheeler script, these seem to work fine
        var y1 = (int)(height * 0.46296f);
        var y2 = (int)(height * 0.53240f);
        
        if (!SlotMatch(x1, y1, x2, y2, one)) return false;

        x1 = width * (start + partWidth + padding) / aspect;
        x2 = width * (start + 2*partWidth - padding) / aspect;
        
        if (!SlotMatch(x1, y1, x2, y2, two)) return false;

        x1 = width * (start + 2*partWidth + padding) / aspect;
        x2 = width * (start + 3*partWidth - padding) / aspect;
        
        return SlotMatch(x1, y1, x2, y2, three);
    }
}

//this has to be a struct or else my ram dies lmao
public struct ByteColor
{
    public byte R;
    public byte G;
    public byte B;

    public ByteColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public bool Compare(ByteColor other, int variation = 2)
    {
        return (Math.Abs(R - other.R) <= variation) && 
               (Math.Abs(G - other.G) <= variation) && 
               (Math.Abs(B - other.B) <= variation);
    }
}

//eh we can make this a struct too
public struct WheelDefinition
{
    public ByteColor One;
    public ByteColor Two;
    public ByteColor Three;

    public WheelDefinition(ByteColor one, ByteColor two, ByteColor three)
    {
        One = one;
        Two = two;
        Three = three;
    }
}

public class MacroDefinitionInformation
{
    public string ArgName; // name to use when setting the key using args
    public string PrintName; //name to use when printing wheel info
    public WheelDefinition Definition;

    public MacroDefinitionInformation(string arg, string print, WheelDefinition def)
    {
        ArgName = arg;
        PrintName = print;
        Definition = def;
    }
}