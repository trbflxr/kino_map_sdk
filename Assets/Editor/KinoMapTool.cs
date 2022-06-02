using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Editor {
  internal class MapEntry {
    internal bool Selected;
    internal string Name;
    internal SceneAsset Scene;
    internal Texture2D LoadScreen;

    internal MapEntry(string name) {
      Name = name;
      Scene = null;
      LoadScreen = null;
    }
  }

  public class KinoMapTool : EditorWindow {
    // do not edit any lines below
    private const int MAP_TOOL_VERSION = 100;
    private const string MAP_EXT = ".knmap";
    private const string OSX_SUFFIX = "_osx";
    private const string WIN_SUFFIX = "_win";
    private const string BUILD_DIR = "Build";
    private const string CACHE_FILE = BUILD_DIR + "/map_cache.kn";

    private const int BLOCK_SIZE = 4096;
    private const int HEADER_OFFSET = 191;
    private const string FILE_HEADER = " _   _______   \n"
                                       + "| | / /_   _\\  \n"
                                       + "| |/ /  | |    \n"
                                       + "|    \\  | |    \n"
                                       + "| |\\  \\_| |_   \n"
                                       + "\\_| \\_/\\___/   \n"
                                       + " _   _ _____   \n"
                                       + "| \\ | |  _  |  \n"
                                       + "|  \\| | | | |  \n"
                                       + "| . ` | | | |  \n"
                                       + "| |\\  \\ \\_/ /  \n"
                                       + "\\_| \\_/\\___/   ";

    private const float GROUPS_OFFSET = 10.0f;
    private const float MAPS_OFFSET = 5.0f;

    private string creatorName_;
    private readonly List<MapEntry> maps_;

    private Vector2 scrollPos_ = Vector2.zero;

    public KinoMapTool() {
      creatorName_ = "Kino";
      maps_ = new List<MapEntry>();
    }

    [MenuItem("Kino/MapTool")]
    public static void ShowWindow() {
      GetWindow(typeof(KinoMapTool));
    }

    [MenuItem("Kino/Open maps folder")]
    private static void OpenMapsFolder() {
      string mapsPath = Path.Combine(Directory.GetCurrentDirectory(), BUILD_DIR);
      if (Directory.Exists(mapsPath)) {
        Process.Start(mapsPath);
      }
      else {
        Debug.LogError("Kino: Unable to open maps folder, the folder doesn't exists");
      }
    }

    private void OnFocus() {
      LoadCache();
    }

    private void OnGUI() {
      bool changed = false;

      GUILayout.Label("Creator name:", EditorStyles.boldLabel);
      string prevName = creatorName_;
      creatorName_ = EditorGUILayout.TextField(creatorName_);
      EditorGUILayout.Space(GROUPS_OFFSET);
      if (prevName != creatorName_) {
        changed = true;
      }

      GUILayout.Label("Maps to build:", EditorStyles.boldLabel);
      int toRemove = -1;

      // maps list
      scrollPos_ = GUILayout.BeginScrollView(scrollPos_);
      for (int i = 0; i < maps_.Count; ++i) {
        var map = maps_[i];

        bool selected = map.Selected;
        map.Selected = EditorGUILayout.BeginToggleGroup(map.Name, map.Selected);
        if (selected != map.Selected) {
          changed = true;
        }

        string prevMapName = map.Name;
        map.Name = EditorGUILayout.TextField("Map name", map.Name);
        if (prevMapName != map.Name) {
          changed = true;
        }

        var prevScene = map.Scene;
        map.Scene = EditorGUILayout.ObjectField(new GUIContent("Scene"), map.Scene, typeof(SceneAsset), true) as SceneAsset;
        if (prevScene != map.Scene) {
          changed = true;
        }

        var prevLoadScreen = map.LoadScreen;
        map.LoadScreen = EditorGUILayout.ObjectField(new GUIContent("Load screen (.png / .jpg)"), map.LoadScreen, typeof(Texture2D), true) as Texture2D;
        if (prevLoadScreen != map.LoadScreen) {
          changed = true;
        }

        if (GUILayout.Button($"Remove map '{map.Name}'")) {
          toRemove = i;
        }

        EditorGUILayout.EndToggleGroup();
        EditorGUILayout.Space(MAPS_OFFSET);
      }

      if (toRemove != -1) {
        maps_.RemoveAt(toRemove);
        changed = true;
      }

      EditorGUILayout.Space(GROUPS_OFFSET);
      if (GUILayout.Button("Add new map entry")) {
        maps_.Add(new MapEntry($"KinoMap_{maps_.Count}"));
      }
      GUILayout.EndScrollView();

      // cache
      EditorGUILayout.Space(GROUPS_OFFSET);
      EditorGUILayout.BeginHorizontal();
      if (GUILayout.Button("Reload cache")) {
        LoadCache();
      }
      if (GUILayout.Button("Wipe cache")) {
        WipeCache();
      }
      EditorGUILayout.EndHorizontal();

      // build
      EditorGUILayout.Space(GROUPS_OFFSET);
      EditorGUILayout.BeginHorizontal();
      if (GUILayout.Button("Build for all platforms")) {
        BuildForAllPlatforms();
      }
      if (GUILayout.Button($"Build for '{EditorUserBuildSettings.activeBuildTarget}'")) {
        BuildForCurrentPlatform();
      }
      EditorGUILayout.EndHorizontal();

      // update cache if anything changed
      if (changed) {
        SaveCache();
      }
    }

    private void BuildForAllPlatforms() {
      BuildMaps(BuildTarget.StandaloneWindows64);
      BuildMaps(BuildTarget.StandaloneOSX);
    }

    private void BuildForCurrentPlatform() {
      BuildMaps(EditorUserBuildSettings.activeBuildTarget);
    }

    private void BuildMaps(BuildTarget target) {
      Debug.Log($"Kino: Build started for '{target}'");

      if (!Directory.Exists(BUILD_DIR)) {
        Directory.CreateDirectory(BUILD_DIR);
      }

      foreach (var map in maps_) {
        if (map.Selected) {
          Debug.Log($"Kino: Processing map '{map.Name}'");
          BuildMapBundle(target, map);
        }
      }
    }

    private void BuildMapBundle(BuildTarget target, MapEntry map) {
      if (map.Scene == null || map.LoadScreen == null) {
        EditorUtility.DisplayDialog("Error processing the map entry", $"Some of map entry '{map.Name}' fields is not set", "OK");
      }

      string bundleId = target switch {
        BuildTarget.StandaloneOSX => map.Name + OSX_SUFFIX,
        BuildTarget.StandaloneWindows => map.Name + WIN_SUFFIX,
        BuildTarget.StandaloneWindows64 => map.Name + WIN_SUFFIX,
        _ => throw new Exception($"Kino: Selected build platform is nut supported: {target}")
      };

      string bundleFullName = $"{bundleId}{MAP_EXT}";
      var builds = new List<AssetBundleBuild> {
        new AssetBundleBuild {
          assetBundleName = bundleFullName,
          assetNames = new[] { AssetDatabase.GetAssetPath(map.Scene) }
        }
      };

      BuildPipeline.BuildAssetBundles(BUILD_DIR, builds.ToArray(), BuildAssetBundleOptions.ForceRebuildAssetBundle, target);

      LaunchMapTool(bundleFullName, map);
    }

    private void LaunchMapTool(string bundleName, MapEntry map) {
      string filePath = $"{BUILD_DIR}/{bundleName}";
      if (!File.Exists(filePath)) {
        Debug.LogError($"Kino: Unable to process map bundle, file '{bundleName}' doesn't exists");
        return;
      }

      var header = CreateMapHeader(map);
      if (header == null) {
        Debug.LogError("Kino: Unable to process map bundle, an error occured while creating the map header");
        return;
      }

      string tempFile = Path.GetTempFileName();
      using (var writer = new FileStream(tempFile, FileMode.Open, FileAccess.Write)) {
        using (var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read)) {
          writer.Write(header, 0, header.Length);

          var buffer = new byte[BLOCK_SIZE];
          while (true) {
            int bytesRead = reader.Read(buffer, 0, BLOCK_SIZE);
            if (bytesRead <= 0) {
              break;
            }
            writer.Write(buffer, 0, bytesRead);
          }
        }
      }

      File.Copy(tempFile, filePath, true);
      File.Delete(tempFile);
    }

    private byte[] CreateMapHeader(MapEntry map) {
      long unixTime = ((DateTimeOffset) DateTime.Now).ToUnixTimeSeconds();

      var loadScreenPath = AssetDatabase.GetAssetPath(map.LoadScreen);
      if (!File.Exists(loadScreenPath)) {
        Debug.LogError($"Kino: Unable to process map '{map.Name}', loadScreen file '{loadScreenPath}' doesn't exists on the disc");
        return null;
      }

      var loadScreenData = File.ReadAllBytes(loadScreenPath);

      using (var stream = new MemoryStream()) {
        using (var writer = new BinaryWriter(stream)) {
          writer.Write(FILE_HEADER);
          writer.Write(MAP_TOOL_VERSION);
          writer.Write(unixTime);
          writer.Write(map.Name);
          writer.Write(creatorName_);
          writer.Write(0);
          writer.Write(loadScreenData.Length);
          writer.Write(loadScreenData);
        }
        return stream.GetBuffer();
      }
    }

    private void LoadCache() {
      EnsureCacheFolder();

      if (!File.Exists(CACHE_FILE)) {
        Debug.LogWarning($"Kino: Unable to load maps cache, file is not exists");
        return;
      }

      maps_.Clear();
      using (var stream = new FileStream(CACHE_FILE, FileMode.Open, FileAccess.Read)) {
        using (var reader = new BinaryReader(stream)) {
          int version = reader.ReadInt32();
          creatorName_ = reader.ReadString();

          int mapsCount = reader.ReadInt32();
          for (int i = 0; i < mapsCount; ++i) {
            var map = new MapEntry("new_map") {
              Selected = reader.ReadBoolean(),
              Name = reader.ReadString()
            };

            string scenePath = reader.ReadString();
            string loadScreenPath = reader.ReadString();

            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            var loadScreen = AssetDatabase.LoadAssetAtPath<Texture2D>(loadScreenPath);

            map.Scene = scene;
            map.LoadScreen = loadScreen;

            maps_.Add(map);
          }
        }
      }
    }

    private void SaveCache() {
      EnsureCacheFolder();

      using (var stream = new FileStream(CACHE_FILE, FileMode.Create, FileAccess.Write)) {
        using (var writer = new BinaryWriter(stream)) {
          writer.Write(MAP_TOOL_VERSION);
          writer.Write(creatorName_);
          writer.Write(maps_.Count);
          foreach (var map in maps_) {
            writer.Write(map.Selected);
            writer.Write(map.Name);

            string scenePath = map.Scene != null ? AssetDatabase.GetAssetPath(map.Scene) : string.Empty;
            string loadScreenPath = map.LoadScreen != null ? AssetDatabase.GetAssetPath(map.LoadScreen) : string.Empty;

            writer.Write(scenePath);
            writer.Write(loadScreenPath);
          }
        }
      }
    }

    private void WipeCache() {
      EnsureCacheFolder();

      try {
        if (File.Exists(CACHE_FILE)) {
          File.Delete(CACHE_FILE);
        }
      }
      catch (Exception e) {
        Debug.LogError($"Kino: Unable to wipe map cache, exception: {e}");
      }
    }

    private static void EnsureCacheFolder() {
      if (!Directory.Exists(BUILD_DIR)) {
        Directory.CreateDirectory(BUILD_DIR);
      }
    }
  }
}