using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace DanceLibraryFFXIV;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
  public int Version { get; set; } = 1;

  public bool IsMainWindowVisible { get; set; }

  public Dictionary<string, Dictionary<string, List<string>>> ModOptionOverrides { get; set; } = [];

  public Dictionary<string, string> ModCategories { get; set; } = [];

  public HashSet<string> FavoriteMods { get; set; } = [];

  public Dictionary<string, List<ModGroup>> CategoryGroups { get; set; } = [];

  public Dictionary<string, List<string>> UngroupedOrder { get; set; } = [];

  public List<string> CustomCategories { get; set; } = [];

  public Dictionary<string, int> ModStarRatings { get; set; } = [];

  public HashSet<string> BlockedMods { get; set; } = [];

  public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
