using System.ComponentModel;
using System.Diagnostics;

using Newtonsoft.Json;

#region Functions

//Attempts to convert the path into a valid DirectoryInfo instance, returning true if successful. Will not check if the path actually exists.
bool GetDirectory( string Path, out DirectoryInfo Dir ) {
    try {
        if ( !string.IsNullOrEmpty(Path) ) {
            Dir = new DirectoryInfo(Path);
            return true;
        }
    } catch { }
    Dir = null!;
    return false;
}

//Attempts to retrieve the first item in the collection, returning true if successful.
//bool GetFirst<T>( IList<T> Ls, out T First ) {
//    if (Ls.Count > 0 ) {
//        First = Ls[0];
//        return true;
//    }
//    First = default!;
//    return false;
//}

//Generates a random number in the given range, excluding a specific value.
int GetRandomNotIncluding( Random Rnd, int Min, int Max, int Exc ) {
    if ( Exc < Min || Exc >= Max ) { return Rnd.Next(Min, Max); }
    if ( Exc == Min ) { return Rnd.Next(Min + 1, Max); }
    if ( Exc == Max - 1 ) { return Rnd.Next(Min, Max - 1); }
    return Rnd.Next(0, 2) == 0 ? Rnd.Next(Min, Exc) : Rnd.Next(Exc + 1, Max);
}

//Gets the highest likely directory from the user's query in the collection.
T GetTyped<T>( IEnumerable<T> Possible, string Query ) where T : FileSystemInfo {
    Query = Query.ToUpperInvariant();
    SortedDictionary<int, List<T>> Dict = new SortedDictionary<int, List<T>>();
    foreach ( T Poss in Possible ) {
        int Ratio = FuzzySharp.Fuzz.WeightedRatio(Poss.Name.ToUpperInvariant(), Query);
        if ( Dict.ContainsKey(Ratio) ) {
            Dict[Ratio].Add(Poss);
        } else {
            Dict.Add(Ratio, new List<T> { Poss });
        }
    }
    //foreach ( (int Rat, List<T> Ls) in Dict ) {
    //    Console.WriteLine($"{Rat}:: '{string.Join("', '", Ls.Select(D => D.Name))}'");
    //}
    return Dict.Last().Value.First();
}

//Gets a random directory from the collection, allowing a 'Last' value to be passed to ensure the user is never offered the same directory twice in a row.
FileSystemInfo GetRandom( DirectoryInfo Root, DirectoryInfo[] Possible, int Cnt, int? Last, Random Rnd ) {
    Debug.WriteLine($"Choosing between 0..{Cnt} (excluding {Last})");
    int ChosenInd = Last.HasValue ? GetRandomNotIncluding(Rnd, 0, Cnt, Last.Value) : Rnd.Next(0, Cnt);
    Debug.WriteLine($"\tChose {ChosenInd}");
    DirectoryInfo Chosen = Possible[ChosenInd];
    while ( true ) {
        Console.Write($"Play from '{Chosen.Name}'? [Y]es/[N]o: ");
        ConsoleKey Input = Console.ReadKey().Key;
        //Console.Write($"key '{Input}/{(int)Input}'");
        Console.Write('\n');
        switch ( Input ) {
            case ConsoleKey.Y: // 'y' indicates the user wants to play from this directory.
                return Chosen;
            case ConsoleKey.N: // 'n' indicates the user wants to play from a different directory. (Another option is just to press enter)
                return GetRandom(Root, Possible, Cnt, ChosenInd, Rnd);
            case ConsoleKey.Oem2: { // '/' key indicates the user will type a specific directory name.
                Console.Write("Type a directory to play from: ");
                string? UserInput = Console.ReadLine();
                if ( UserInput is null ) { break; }
                DirectoryInfo Result = GetTyped(Possible, UserInput);
                Console.Write('\n');
                return Result;
            }
            case ConsoleKey.Oem1: { // ';' key indicates the user will type a specific .m3u(8) file name.
                Console.Write("Type a playlist file to play from: ");
                string? UserInput = Console.ReadLine();
                if ( UserInput is null ) { break; } // The '*.m3u?' wildcard below matches both '.m3u' and '.m3u8'
                FileInfo Result = GetTyped(Root.EnumerateFiles("*.m3u?", SearchOption.AllDirectories), UserInput);
                Console.Write('\n');
                return Result;
            }
            case ConsoleKey.Escape: // 'Esc' indicates the user wishes to quit the application.
                Environment.Exit(0);
                return null!;
        }
    }
}

//Deserialises the json data from the given file.
T? Read<T>( FileInfo File, JsonSerializer Serialiser ) {
    using ( FileStream FS = File.OpenRead() ) {
        using ( StreamReader SR = new StreamReader(FS) ) {
            using ( JsonTextReader JTR = new JsonTextReader(SR) ) {
                return Serialiser.Deserialize<T>(JTR);
            }
        }
    }
}

//Serialises the given data object into json data, written into the destination file (which will be cleared first if it exists, or created if it does not).
void Write<T>( FileInfo Dest, T Data, JsonSerializer Serialiser ) {
    using ( FileStream FS = Dest.Exists ? Dest.Open(FileMode.Truncate, FileAccess.Write) : Dest.Create() ) {
        using ( StreamWriter SW = new StreamWriter(FS) ) {
            Serialiser.Serialize(SW, Data);
        }
    }
}

#endregion

//Set console encoding to system default. (i.e. UTF-8 as opposed to ASCII)
Console.OutputEncoding = System.Text.Encoding.Default;
//Console.OutputEncoding = System.Text.Encoding.UTF8;
//PInvoke.SetConsoleOutputCP(65001);
//PInvoke.SetConsoleCP(65001);

//Parse user arguments.
//Supported usages are as follows:
//"F:\Music\_Albums"
//"F:\Music\_Albums" -b
//"F:\Music\_Albums" --back
//"F:\Music\_Albums" -b 2
//"F:\Music\_Albums" --back 2
//-b 2 "F:\Music\_Albums"
//--back 2 "F:\Music\_Albums"
bool ArgBackSupplied = false;
int ArgBackTimes = 0;
DirectoryInfo? Base = null;
foreach( string Arg in args ) {
    switch (Arg) {
        case "--back":
        case "-b":
            ArgBackSupplied = true;
            break;
        default:
            if ( int.TryParse(Arg, out int SupposedBackTimes) ) {
                ArgBackTimes = SupposedBackTimes;
            } else if ( GetDirectory(Arg, out DirectoryInfo ArgDir) ) {
                Base = ArgDir;
            }
            break;
    }
}


//Locate the 'settings.json' file and prepare the serialiser.
JsonSerializer Ser = new JsonSerializer { Formatting = Formatting.Indented };
FileInfo LocalFile = new FileInfo(Path.Combine(new FileInfo(Process.GetCurrentProcess().MainModule?.FileName!).DirectoryName!, "settings.json"));

KnownData KD;
//In the case the 'settings.json' file does not exist, is malformed json data, or does not supply an executable path, we create a default example file and open it in the user's default json text editor.
if ( !LocalFile.Exists || Read<KnownData>(LocalFile, Ser) is not { } KnData || string.IsNullOrEmpty(KnData.Executable) ) {
    Write(LocalFile, new KnownData(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Media Player\\wmplayer.exe").Replace('\\', '/'), "\"$(folder)\"", null), Ser);
    //_ = Process.Start("notepad.exe", $"\"{LocalFile.FullName}\"");
    try {
        _ = Process.Start($"\"{LocalFile.FullName}\"");
    } catch (Win32Exception) { //Thrown when no default '.json' editor is specified for the OS
        //"%windir%/system32/notepad.exe"
        FileInfo Notepad = new FileInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32", "notepad.exe"));
        if ( !Notepad.Exists ) {
            Console.WriteLine($"No default .json or text editor assigned. Please open \"{LocalFile.FullName}\" in your preferred text editing application.");
            _ = Console.ReadKey();

        } else {
            _ = Process.Start(Notepad.FullName, $"\"{LocalFile.FullName}\"");
        }
    }
    Environment.Exit(0);
    return;
} else {
    KD = KnData;
    if (!string.IsNullOrEmpty(KD.DefaultAlbumDirectory) && GetDirectory(KD.DefaultAlbumDirectory, out DirectoryInfo DefAlbDir) ) {
        Base = DefAlbDir;
    }
}
Base ??= new DirectoryInfo(Environment.CurrentDirectory);

if ( ArgBackSupplied ) {
    if ( ArgBackTimes <= 0 ) { ArgBackTimes = 1; }
    for ( int I = 0; I < ArgBackTimes; I++ ) {
        if ( Base.Parent is { } Parent ) {
            Base = Parent;
        } else {
            Console.WriteLine($"Directory back-travel limit exceeded. No parents found for directory '{Base.FullName}'");
            break;
        }
    }
}

//Find all album directories and enumerate
DirectoryInfo[] Options = Base.GetDirectories();
int Count = Options.Length;

if ( Count == 0 ) {
    Console.WriteLine("No child directories could be found. Ensure that a directory is given as an argument, or that the application is ran from a main folder containing multiple album directories.");
    Environment.Exit(0);
    return;
}

//Get the user to choose an album directory at random. The method ensures the user is never supplied the same directory twice. In the case there is only one directory, we will always use that regardless.
FileSystemInfo UserChosen = Count == 0 ? Options[0] : GetRandom(Base, Options, Count, null, new Random());
Console.WriteLine($"Will play from '{UserChosen.Name}'.");

//If the 'settings.json' file is valid, we start the supplied executable and arguments, replacing $(folder) with the chosen folder name.
//TODO: Update $(folder) variable to $(source) as the application now supports both directories and playlist files. WARNING: This is a breaking change.
_ = Process.Start(KD.Executable.Replace('/', '\\').Trim(' '), KD.Args.Replace("$(folder)", UserChosen.FullName));
Environment.Exit(0);