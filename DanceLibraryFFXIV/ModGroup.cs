using System;
using System.Collections.Generic;

namespace DanceLibraryFFXIV;

[Serializable]
public class ModGroup
{
    public string Name { get; set; } = "New Group";

    public bool IsCollapsed { get; set; }

    public List<string> ModDirectories { get; set; } = new List<string>();
}
