using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using KSP.Game;
using KSP.Sim.impl;
using KSP.UI;
using KSP.UI.Binding;
using KSP.UI.Binding.Core;
using KSP.UI.Flight;
using Newtonsoft.Json;
using SpaceWarp;
using SpaceWarp.API.Assets;
using SpaceWarp.API.Mods;
using SpaceWarp.API.UI;
using SpaceWarp.API.UI.Appbar;
using UnityEngine;

namespace EvenBetterTimeWarp;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(SpaceWarpPlugin.ModGuid, SpaceWarpPlugin.ModVer)]
public class EvenBetterTimeWarpPlugin : BaseSpaceWarpPlugin
{
    // These are useful in case some other mod wants to add a dependency to this one
    public const string ModGuid = MyPluginInfo.PLUGIN_GUID;
    public const string ModName = MyPluginInfo.PLUGIN_NAME;
    public const string ModVer = MyPluginInfo.PLUGIN_VERSION;

    private static readonly string[] DefaultTimeWarpLevels = {
        "1",
        "2",
        "4",
        "10",
        "50",
        "100",
        "1000",
        "10000",
        "100000",
        "1000000",
        "10000000"
    };
    
    private bool _foundTimeWarpButton;

    private bool _isWindowOpen;
    private Rect _windowRect;

    private const string ToolbarFlightButtonID = "BTN-EvenBetterTimeWarpFlight";

    public static EvenBetterTimeWarpPlugin Instance { get; private set; }

    private ConfigEntry<KeyboardShortcut> _physicsWarpShortcut;
    public bool IsPhysicsWarpShortcutPressed { get; private set; }
    
    private bool _dropdownEnabled;
    private string _presetDropDown = "";
    private string _presetTextBox = "";
    
    private readonly HashSet<string> _textBoxNames = new();

    private bool _couldLoadSettings;
    public EvenBetterTimeWarpSettings Settings { get; private set; } = new();

    private string PresetConfigPath => PluginFolderPath + Path.DirectorySeparatorChar + "presets.json";
    private string DefaultPresetsPath => PluginFolderPath + Path.DirectorySeparatorChar + "presets_default.json";

    private GameObject[] _warpButtonGameObjects = new GameObject[11];
    private bool _clearedGameObjects = true;
    
    /// <summary>
    /// Runs when the mod is first initialized.
    /// </summary>
    public override void OnInitialized()
    {
        base.OnInitialized();

        Instance = this;

        _physicsWarpShortcut = Config.Bind("General", "PhysicsWarpShortcut", new KeyboardShortcut(KeyCode.LeftAlt),
            "Keyboard shortcut to activate physics warp");
        
        // Register Flight AppBar button
        Appbar.RegisterAppButton(
            "Even Better Time Warp",
            ToolbarFlightButtonID,
            AssetManager.GetAsset<Texture2D>($"{SpaceWarpMetadata.ModID}/images/icon.png"),
            SetWindowOpen
        );

        // Register all Harmony patches in the project
        Harmony.CreateAndPatchAll(typeof(EvenBetterTimeWarpPlugin).Assembly);

        if (!File.Exists(PresetConfigPath) && File.Exists(DefaultPresetsPath))
            File.Copy(DefaultPresetsPath, PresetConfigPath);
        
        if (File.Exists(PresetConfigPath))
            Settings = JsonConvert.DeserializeObject<EvenBetterTimeWarpSettings>(File.ReadAllText(PresetConfigPath));
        else
            return;
        
        foreach (string level in Settings.TimeWarpLevels)
            if (level is null)
                return;
        
        for (int i = 0; i < PhysicsSettings.TimeWarpLevels.Length; i++)
            if (float.TryParse(Settings.TimeWarpLevels[i], out float value))
                PhysicsSettings._physicsSettings._timeWarpLevels[i] = new TimeWarp.TimeWarpLevel(value, 0.0f);
            else
                return;

        _couldLoadSettings = true;
    }

    private void LoadDefaults()
    {
        Settings.TimeWarpLevels = (string[]) DefaultTimeWarpLevels.Clone();
        Settings.PhysicsWarpLevels = new bool[11];
        
        for (int i = 0; i < PhysicsSettings.TimeWarpLevels.Length; i++)
            PhysicsSettings._physicsSettings._timeWarpLevels[i] =
                new TimeWarp.TimeWarpLevel(float.Parse(DefaultTimeWarpLevels[i]), 0.0f);
    }
    
    private void SetWindowOpen(bool open)
    {
        if (!open)
            Game.Input.Enable();
        
        _isWindowOpen = open;
        GameObject.Find(ToolbarFlightButtonID)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(open);
    }

    private void Update()
    {
        // the ? operator isn't recommended for UnityEngine.Object, so we need lots of long null checks
        if (Game is null)
            return;
        
        if (Game.GlobalGameState is null)
            return;

        var state = Game.GlobalGameState.GetGameState();
        if (state is null)
            return;
        
        if (state.GameState is GameState.MainMenu or GameState.WarmUpLoading or GameState.Loading)
        {
            if (!_clearedGameObjects)
            {
                _foundTimeWarpButton = false;
                _warpButtonGameObjects = new GameObject[11];
                _clearedGameObjects = true;
            }
            
            return;
        }
        
        IsPhysicsWarpShortcutPressed = _physicsWarpShortcut.Value.IsPressed();
        
        if (_foundTimeWarpButton)
            return;

        _clearedGameObjects = false;

        var readNumber = FindObjectOfType<TimeWarpInstrument>()?.gameObject
            .GetChild("Container")
            .GetChild("GRP-Title")
            .GetChild("GRP_label")
            .GetChild("TXT_label")
            .GetComponent<UIValue_ReadNumber_Text>();

        if (readNumber is null)
            return;

        readNumber.textFormat = "= {0:N2}x";

        RefreshAllTooltips();

        var timeWarpButton = GetTimeWarpButton(10);
        if (timeWarpButton is null)
            return;
        
        var writeEnum = timeWarpButton.GetComponent<UIValue_WriteEnum_Button>();
        if (writeEnum is null)
            return;

        _foundTimeWarpButton = true;
        writeEnum.mappedEnumValues.Add("10");        
    }

    private void OnDestroy()
    {
        File.WriteAllText(PresetConfigPath, JsonConvert.SerializeObject(Settings));
    }

    /// <summary>
    /// Draws a simple UI window when <code>this._isWindowOpen</code> is set to <code>true</code>.
    /// </summary>
    private void OnGUI()
    {
        // Set the UI
        GUI.skin = Skins.ConsoleSkin;

        if (_isWindowOpen)
        {
            _windowRect = GUILayout.Window(
                GUIUtility.GetControlID(FocusType.Passive),
                _windowRect,
                _ => Instance.FillWindow(),
                "Even Better Time Warp",
                GUILayout.Height(350),
                GUILayout.Width(350),
                GUILayout.ExpandHeight(true),
                GUILayout.ExpandWidth(true)
            );
        }
    }

    /// <summary>
    /// Defines the content of the UI window drawn in the <code>OnGui</code> method.
    /// </summary>
    private void FillWindow()
    {
        if (!_couldLoadSettings)
        {
            GUI.color = Color.red;
            GUILayout.Label("Failed to load presets.json!");
            return;
        }

        GUILayout.Label("Time warp levels:");

        for (int i = 0; i < 11; i++)
            TimeWarpLevelInput(i);
        
        if (GUILayout.Button("Load defaults"))
        {
            LoadDefaults();
            RefreshAllTooltips();
        }
        
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical();
        string dropdownText = _presetDropDown.Length == 0 ? "Select preset" : _presetDropDown;
        dropdownText += _dropdownEnabled ? " ▲" : " ▼";
        _dropdownEnabled ^= GUILayout.Button(dropdownText);

        if (_dropdownEnabled)
        {
            foreach (string preset in Settings.Presets.Keys)
                if (GUILayout.Button(preset))
                {
                    _presetDropDown = preset;
                    _dropdownEnabled = false;
                }
        }

        GUILayout.EndVertical();
        
        if (_presetDropDown.Length > 0 && GUILayout.Button("Load preset"))
        {
            PhysicsSettings._physicsSettings._timeWarpLevels = (TimeWarp.TimeWarpLevel[]) Settings.Presets[_presetDropDown].Item1.Clone();
            Settings.PhysicsWarpLevels = (bool[]) Settings.Presets[_presetDropDown].Item2.Clone();
            
            for (int i = 0; i < PhysicsSettings.TimeWarpLevels.Length; i++)
                Settings.TimeWarpLevels[i] = PhysicsSettings.TimeWarpLevels[i].TimeScaleFactor.ToString(CultureInfo.InvariantCulture);
            
            RefreshAllTooltips();
        }

        if (_presetDropDown.Length > 0 && GUILayout.Button("Delete preset"))
        {
            Settings.Presets.Remove(_presetDropDown);
            _presetDropDown = "";
        }
        
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        _presetTextBox = InputBlockingTextBox(_presetTextBox, "preset");
        if (GUILayout.Button("Save preset"))
            Settings.Presets[_presetTextBox] = (PhysicsSettings.TimeWarpLevels, Settings.PhysicsWarpLevels);
        GUILayout.EndHorizontal();
        
        if (GUI.Button(new Rect(_windowRect.width - 18, 2, 16, 16), "x"))
        {
            SetWindowOpen(false);
            GUIUtility.ExitGUI();
        }
        
        if (_textBoxNames.Contains(GUI.GetNameOfFocusedControl()))
            Game.Input.Disable();
        else
            Game.Input.Enable();

        GUI.DragWindow(new Rect(0, 0, 10000, 500));
    }

    /// <summary>
    /// Draw a text box that blocks game input when focused
    /// </summary>
    private string InputBlockingTextBox(string contents, string controlName, params GUILayoutOption[] options)
    {
        _textBoxNames.Add(controlName);
        GUI.SetNextControlName(controlName);
        return GUILayout.TextField(contents, options);
    }

    private void TimeWarpLevelInput(int i)
    {
        GUILayout.BeginHorizontal();

        string newLevel = InputBlockingTextBox(Settings.TimeWarpLevels[i], $"time_warp_level_{i}", GUILayout.Width(200));
        bool changed = newLevel != Settings.TimeWarpLevels[i];
        Settings.TimeWarpLevels[i] = newLevel;
        
        bool numberIsValid = float.TryParse(Settings.TimeWarpLevels[i], out float level);
        if (numberIsValid && changed)
        {
            PhysicsSettings._physicsSettings._timeWarpLevels[i] = new TimeWarp.TimeWarpLevel(level, 0.0f);
            var button = GetTimeWarpButton(i);
            if (button is not null)
            {
                var tooltip = button.GetComponent<BasicTextTooltipData>();
                if (tooltip is not null)
                {
                    FormattableString tooltipText = $"{PhysicsSettings.TimeWarpLevels[i].TimeScaleFactor:N2}x";
                    tooltip.TooltipTitleKey = tooltipText.ToString();
                }
            }
        }

        Settings.PhysicsWarpLevels[i] = PhysicsSettings.TimeWarpLevels[i].TimeScaleFactor < 1.0 
                                        || GUILayout.Toggle(Settings.PhysicsWarpLevels[i], "Force physics warp");

        GUILayout.EndHorizontal();

        if (!numberIsValid)
        {
            var normalColor = GUI.color;
            GUI.color = Color.red;
            GUILayout.Label("Invalid number");
            GUI.color = normalColor;
        }
    }

    private static string GetTimeWarpButtonGameObjectName(int index) =>
        index is 0 or 10 ? $"_TimeWarpButton_{index}" : $"TimeWarpButton_{index}";

    private GameObject GetTimeWarpButton(int index)
    {
        if (_warpButtonGameObjects[index] is null)
            _warpButtonGameObjects[index] = GameObject.Find(GetTimeWarpButtonGameObjectName(index));

        return _warpButtonGameObjects[index];
    }

    private void RefreshAllTooltips()
    {
        for (int i = 0; i < 11; i++)
        {
            var button = GetTimeWarpButton(i);
            if (button is null)
                break;
            
            var tooltip = button.GetComponent<BasicTextTooltipData>();
            if (tooltip is null)
                break;

            FormattableString tooltipText = $"{PhysicsSettings.TimeWarpLevels[i].TimeScaleFactor:N2}x";
            tooltip.TooltipTitleKey = tooltipText.ToString();
        }
    }
}