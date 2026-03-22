using System;
using System.Collections.Generic;

namespace DanceLibraryFFXIV;

public static class EmoteData
{
    public static readonly IReadOnlyDictionary<string, string> DanceEmotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        {
          "/dance",
          "Dance"
        },
        {
          "/stepdance",
          "Step Dance"
        },
        {
          "/harvestdance",
          "Harvest Dance"
        },
        {
          "/hdance",
          "Harvest Dance"
        },
        {
          "/balldance",
          "Ball Dance"
        },
        {
          "/mdance",
          "Manderville Dance"
        },
        {
          "/mandervilledance",
          "Manderville Dance"
        },
        {
          "/mmambo",
          "Manderville Mambo"
        },
        {
          "/mandervillemambo",
          "Manderville Mambo"
        },
        {
          "/sundropdance",
          "Sundrop Dance"
        },
        {
          "/sundance",
          "Sundrop Dance"
        },
        {
          "/mogdance",
          "Moogle Dance"
        },
        {
          "/moonlift",
          "Moonlift Dance"
        },
        {
          "/yoldance",
          "Yol Dance"
        },
        {
          "/lalihop",
          "Lali Hop"
        },
        {
          "/laliho",
          "Lali Hop"
        },
        {
          "/lophop",
          "Lop Hop"
        },
        {
          "/thavdance",
          "Thavnairian Dance"
        },
        {
          "/tdance",
          "Thavnairian Dance"
        },
        {
          "/golddance",
          "Gold Dance"
        },
        {
          "/gdance",
          "Gold Dance"
        },
        {
          "/beesknees",
          "Bee's Knees"
        },
        {
          "/bees knees",
          "Bee's Knees"
        },
        {
          "/songbird",
          "Songbird"
        },
        {
          "/edance",
          "Eastern Dance"
        },
        {
          "/easterndance",
          "Eastern Dance"
        },
        {
          "/bombdance",
          "Bomb Dance"
        },
        {
          "/boxstep",
          "Box Step"
        },
        {
          "/sidestep",
          "Side Step"
        },
        {
          "/getfantasy",
          "Get Fantasy"
        },
        {
          "/popotostep",
          "Popoto Step"
        },
        {
          "/sabotender",
          "Senor Sabotender"
        },
        {
          "/heeltoe",
          "Heel Toe"
        },
        {
          "/goobbuedo",
          "Goobbue Do"
        },
        {
          "/mysterymachine",
          "Goobbue Do"
        },
        {
          "/flamedance",
          "Flame Dance"
        },
        {
          "/ladance",
          "Little Ladies' Dance"
        },
        {
          "/littleladiesdance",
          "Little Ladies' Dance"
        },
        {
          "/crimsonlotus",
          "Crimson Lotus"
        },
        {
          "/uchiwasshoi",
          "Uchiwasshoi"
        },
        {
          "/wasshoi",
          "Wasshoi"
        },
        {
          "/paintblack",
          "Paint It Black"
        },
        {
          "/paintblue",
          "Paint It Blue"
        },
        {
          "/paintred",
          "Paint It Red"
        },
        {
          "/paintyellow",
          "Paint It Yellow"
        }
    };

    public static readonly IReadOnlyDictionary<string, string> GameCommandOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        {
            "/thavnairian dance",
            "/tdance"
        },
        {
            "/sit on ground",
            "/groundsit"
        },
        {
            "/paint it black",
            "/paintblack"
        },
        {
            "/paint it blue",
            "/paintblue"
        },
        {
            "/paint it red",
            "/paintred"
        },
        {
            "/paint it yellow",
            "/paintyellow"
        },
        {
            "/bee's knees",
            "/beesknees"
        },
        {
            "/push-ups",
            "/pushups"
        },
        {
            "/sit-ups",
            "/situps"
        },
        {
            "/greeting",
            "/greet"
        },
        {
            "/confused",
            "/disturbed"
        },
        {
            "/cheer wave: yellow",
            "/cheerwy"
        },
        {
            "/cheerwave: yellow",
            "/cheerwy"
        },
        {
            "/cheerwave:yellow",
            "/cheerwy"
        },
        {
            "/cheer wave: violet",
            "/cheerwv"
        },
        {
            "/cheerwave: violet",
            "/cheerwv"
        },
        {
            "/cheerwave:violet",
            "/cheerwv"
        },
        {
            "/cheer on: bright",
            "/cheerow"
        },
        {
            "/cheeron: bright",
            "/cheerow"
        },
        {
            "/cheeron:bright",
            "/cheerow"
        },
        {
            "/cheer on: blue",
            "/cheerob"
        },
        {
            "/cheeron: blue",
            "/cheerob"
        },
        {
            "/cheeron:blue",
            "/cheerob"
        },
        {
            "/cheer jump: green",
            "/cheerjg"
        },
        {
            "/cheerjump: green",
            "/cheerjg"
        },
        {
            "/cheer jump:green",
            "/cheerjg"
        },
        {
            "/cheerjump:green",
            "/cheerjg"
        },
        {
            "/cheer jump: red",
            "/cheerjr"
        },
        {
            "/cheerjump: red",
            "/cheerjr"
        },
        {
            "/cheer jump:red",
            "/cheerjr"
        },
        {
            "/cheerjump:red",
            "/cheerjr"
        }
    };

    public static bool IsDance(string command)
    {
        var key = NormalizeCommand(command);
        return DanceEmotes.ContainsKey(key);
    }

    public static string GetDisplayName(string command)
    {
        var key = NormalizeCommand(command);
        if (DanceEmotes.TryGetValue(key, out var displayName))
            return displayName;
        var str1 = key.TrimStart('/');
        if (str1.Length == 0)
            return key;
        var strArray1 = str1.Split(' ');
        for (var index1 = 0; index1 < strArray1.Length; ++index1)
        {
            if (strArray1[index1].Length > 0)
            {
                var strArray2 = strArray1;
                var index2 = index1;
                var upperInvariant = char.ToUpperInvariant(strArray1[index1][0]);
                var readOnlySpan1 = new ReadOnlySpan<char>(ref upperInvariant);
                var str2 = strArray1[index1];
                var readOnlySpan2 = str2.Substring(1, str2.Length - 1);
                var str3 = readOnlySpan1.ToString() + readOnlySpan2.ToString();
                strArray2[index2] = str3;
            }
        }
        return string.Join(' ', strArray1);
    }

    public static string GetExecuteCommand(string command)
    {
        var key = NormalizeCommand(command);
        return GameCommandOverrides.TryGetValue(key, out var str) ? str : key.Replace(" ", "");
    }

    public static string NormalizeCommand(string command)
    {
        var lowerInvariant = command.Trim().ToLowerInvariant();
        return !lowerInvariant.StartsWith('/') ? "/" + lowerInvariant : lowerInvariant;
    }
}
