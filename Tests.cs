using System;
using System.IO;
using TrayPassword;

class Tests
{
    static int checks;
    static void Check(bool ok, string label)
    { if (!ok) throw new Exception(label); checks++; }
    static void Invalid(Preferences p)
    {
        bool failed = false;
        try { Passwords.Generate(p); } catch (ArgumentException) { failed = true; }
        Check(failed, "Invalid settings were accepted");
    }
    static void Main(string[] args)
    {
        foreach (int size in new[] { 4, 8, 20, 128 })
        foreach (bool firstUpper in new[] { false, true })
        for (int mask = 1; mask < 8; mask++)
        {
            var p = new Preferences { Length = size, Letters = (mask & 1) != 0,
                Numbers = (mask & 2) != 0, Symbols = (mask & 4) != 0, SymbolList = "!!@@#", FirstUpper = firstUpper };
            var categories = Passwords.Categories(p);
            string alphabet = string.Join("", categories.ToArray());
            for (int n = 0; n < 300; n++)
            {
                string value = Passwords.Generate(p);
                Check(value.Length == size, "Wrong length");
                if (firstUpper) Check(Passwords.UpperSet.IndexOf(value[0]) >= 0, "Missing capital prefix");
                for (int i = 0; i < value.Length; i++)
                    Check((i == 0 && firstUpper ? Passwords.UpperSet : alphabet).IndexOf(value[i]) >= 0, "Unexpected character");
                foreach (string category in categories)
                    Check(value.IndexOfAny(category.ToCharArray()) >= 0, "Missing required category");
            }
        }
        var one = new Preferences { Length = 4, Letters = false, Numbers = false, Symbols = true, SymbolList = "!!!!" };
        Check(Passwords.Generate(one) == "!!!!", "Single-symbol set failed");
        Invalid(new Preferences { Letters = false, Numbers = false, Symbols = false });
        Invalid(new Preferences { SymbolList = "" });
        Invalid(new Preferences { SymbolList = "! " });
        Invalid(new Preferences { SymbolList = "!a" });
        Invalid(new Preferences { Length = 3 });
        Invalid(new Preferences { Length = 129 });
        string path = Path.Combine(Path.GetFullPath(args[0]), "roundtrip.xml");
        var saved = new Preferences { Length = 37, Letters = false, Numbers = true, Symbols = true, SymbolList = "<&>\"'", FirstUpper = true };
        saved.Save(path);
        var loaded = Preferences.Load(path);
        Check(loaded.Length == 37 && !loaded.Letters && loaded.Numbers && loaded.Symbols && loaded.FirstUpper && loaded.SymbolList == saved.SymbolList,
            "Settings did not survive reload");
        saved.Length = 42; saved.Save(path);
        Check(Preferences.Load(path).Length == 42, "Atomic settings replacement failed");
        Check(Preferences.Load(path + ".absent").Length == 20, "Missing settings defaults failed");
        File.WriteAllText(path, "<Preferences><Length>24</Length><Letters>false</Letters><Numbers>true</Numbers><Symbols>false</Symbols><SymbolList>!</SymbolList></Preferences>");
        var legacy = Preferences.Load(path);
        Check(legacy.Length == 24 && !legacy.FirstUpper && !legacy.Letters && legacy.Numbers && !legacy.Symbols,
            "Existing settings compatibility failed");
        File.WriteAllText(path, "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///C:/sensitive'>]><Preferences>&e;</Preferences>");
        bool rejected = false;
        try { Preferences.Load(path); } catch { rejected = true; }
        Check(rejected, "XML external entities were accepted");
        Console.WriteLine("PASS: 16,800 passwords across every category combination, capital-prefix setting and lengths 4/8/20/128; " + checks + " checks. Settings reload/replacement, legacy compatibility and invalid input checks passed.");
    }
}
