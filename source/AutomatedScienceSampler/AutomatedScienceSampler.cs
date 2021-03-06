﻿using KerboKatz.Assets;
using KerboKatz.Extensions;
using KerboKatz.Toolbar;
using KSP.UI;
using KSP.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace KerboKatz.ASS
{
  [KSPAddon(KSPAddon.Startup.Flight, false)]
  public class AutomatedScienceSampler : KerboKatzBase<Settings>, IToolbar
  {
    private Dictionary<string, int> shipCotainsExperiments = new Dictionary<string, int>();
    private Dictionary<Type, IScienceActivator> activators = new Dictionary<Type, IScienceActivator>();
    private double nextUpdate;
    private float CurrentFrame;
    private float lastFrameCheck;
    private static List<GameScenes> _activeScences = new List<GameScenes>() { GameScenes.FLIGHT };
    private Sprite _icon = AssetLoader.GetAsset<Sprite>("icon56", "Icons/AutomatedScienceSampler", "AutomatedScienceSampler/AutomatedScienceSampler");
    private List<ModuleScienceExperiment> experiments;
    private List<ModuleScienceContainer> scienceContainers;
    private Dropdown transferScienceUIElement;
    private bool isReady;
    string settingsUIName;
    private KerbalEVA kerbalEVAPart;
    private Vessel parentVessel;
    public PerCraftSetting craftSettings;
    private Transform uiContent;

    private float frameCheck { get { return 1 / settings.spriteFPS; } }
    #region init/destroy
    public AutomatedScienceSampler()
    {
      modName = "AutomatedScienceSampler";
      displayName = "Automated Science Sampler";
      settingsUIName = "AutomatedScienceSampler";
      tooltip = "Use left click to turn AutomatedScienceSampler on/off.\n Use shift+left click to open the settings menu.";
      requiresUtilities = new Version(1, 3, 6);
      ToolbarBase.instance.Add(this);
      LoadSettings("AutomatedScienceSampler", "Settings");
      Log("Init done!");
    }


    public override void OnAwake()
    {
      if (!(HighLogic.CurrentGame.Mode == Game.Modes.CAREER || HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX))
      {
        Log("Game mode not supported!");
        Destroy(this);
        return;
      }
      GetScienceActivators();
      LoadUI(settingsUIName, "AutomatedScienceSampler/AutomatedScienceSampler");

      GameEvents.onVesselChange.Add(OnVesselChange);
      GameEvents.onVesselWasModified.Add(OnVesselChange);
      GameEvents.onCrewOnEva.Add(GoingEva);
      Log("Awake");
    }
    private void GetScienceActivators()
    {
      Log("Starting search");
      Utilities.LoopTroughAssemblies(CheckTypeForScienceActivator);

      DefaultActivator.instance = activators[typeof(ModuleScienceExperiment)] as DefaultActivator;
    }

    private void CheckTypeForScienceActivator(Type type)
    {
      if (type.GetInterfaces().Contains(typeof(IScienceActivator)) && type.GetConstructor(Type.EmptyTypes) != null)
      {
        var activator = Activator.CreateInstance(type) as IScienceActivator;
        activator.AutomatedScienceSampler = this;
        Log("Found", activator.GetType());
        foreach (var validType in activator.GetValidTypes())
        {
          Log("...for type: ", validType);
          activators.Add(validType, activator);
        }
      }
    }

    protected override void AfterDestroy()
    {
      GameEvents.onVesselChange.Remove(OnVesselChange);
      GameEvents.onVesselWasModified.Remove(OnVesselChange);
      GameEvents.onCrewOnEva.Remove(GoingEva);
      ToolbarBase.instance.Remove(this);
      Log("AfterDestroy");
    }

    private void GetCraftSettings()
    {
      string guid;
      if (settings.perCraftSetting)
      {
        if (FlightGlobals.ActiveVessel.isEVA)
        {
          if (parentVessel != null)
            guid = parentVessel.id.ToString();
          else
            guid = "EVA";
        }
        else
        {
          guid = FlightGlobals.ActiveVessel.id.ToString();
        }
      }
      else
      {
        guid = "Single";
      }

      craftSettings = settings.GetSettingsForCraft(guid);
      UpdateUIVisuals();
    }
    #endregion
    #region ui
    protected override void OnUIElemntInit(UIData uiWindow)
    {
      var prefabWindow = uiWindow.gameObject.transform as RectTransform;
      uiContent = prefabWindow.FindChild("Content");
      UpdateUIVisuals();

      InitToggle(uiContent, "UsePerCraftSettings", settings.perCraftSetting, OnPerCraftSettingChange);
      InitToggle(uiContent, "Debug", settings.debug, OnDebugChange);
      InitSlider(uiContent, "SpriteFPS", settings.spriteFPS, OnSpriteFPSChange);
      transferScienceUIElement = InitDropdown(uiContent, "TransferScience", OnTransferScienceChange);


    }

    private void UpdateUIVisuals()
    {
      if (craftSettings != null)
      {
        InitInputField(uiContent, "Threshold", craftSettings.threshold.ToString(), OnThresholdChange, true);
        InitToggle(uiContent, "SingleRunExperiments", craftSettings.oneTimeOnly, OnSingleRunExperimentsChange, true);
        InitToggle(uiContent, "ResetExperiments", craftSettings.resetExperiments, OnResetExperimentsChange, true);
        InitToggle(uiContent, "HideScienceDialog", craftSettings.hideScienceDialog, OnHideScienceDialogChange, true);
        InitToggle(uiContent, "TransferAllData", craftSettings.transferAllData, OnTransferAllDataChange, true);
        InitToggle(uiContent, "DumpDuplicates", craftSettings.dumpDuplicates, OnDumpDuplicatesChange, true);
      }
    }

    private void OnDumpDuplicatesChange(bool arg0)
    {
      craftSettings.dumpDuplicates = arg0;
      SaveSettings();
      Log("OnDumpDuplicatesChange");
    }

    private void OnTransferAllDataChange(bool arg0)
    {
      craftSettings.transferAllData = arg0;
      SaveSettings();
      Log("OnTransferAllDataChange");
    }

    private void OnSpriteFPSChange(float arg0)
    {
      settings.spriteFPS = arg0;
      SaveSettings();
      Log("OnSpriteFPSChange");
    }

    private void OnTransferScienceChange(int arg0)
    {
      craftSettings.currentContainer = arg0;
      Log("OnTransferScienceChange ");
      if (craftSettings.currentContainer != 0 && scienceContainers.Count >= craftSettings.currentContainer)
      {
        StartCoroutine(DisableHighlight(0.25f, scienceContainers[craftSettings.currentContainer - 1].part));
      }
      SaveSettings();
    }

    private IEnumerator DisableHighlight(float time, Part part)
    {
      part.SetHighlight(true, false);
      yield return new WaitForSeconds(time);
      part.SetHighlight(false, false);
    }


    private void OnPerCraftSettingChange(bool arg0)
    {
      settings.perCraftSetting = arg0;
      SaveSettings();
      GetCraftSettings();
      Log("OnPerCraftSettingChange");
    }
    private void OnDebugChange(bool arg0)
    {
      settings.debug = arg0;
      SaveSettings();
      Log("OnDebugChange");
    }

    private void OnHideScienceDialogChange(bool arg0)
    {
      craftSettings.hideScienceDialog = arg0;
      SaveSettings();
      Log("OnHideScienceDialog");
    }

    private void OnResetExperimentsChange(bool arg0)
    {
      craftSettings.resetExperiments = arg0;
      SaveSettings();
      Log("OnResetExperimentsChange");
    }

    private void OnSingleRunExperimentsChange(bool arg0)
    {
      craftSettings.oneTimeOnly = arg0;
      SaveSettings();
      Log("onSingleRunExperimentsChange");
    }

    private void OnThresholdChange(string arg0)
    {
      craftSettings.threshold = arg0.ToFloat();
      SaveSettings();
      Log("onThresholdChange");
    }
    private void OnToolbar()
    {
      Log((craftSettings == null), " ", craftSettings.guid, " ", FlightGlobals.ActiveVessel.id);
      if (craftSettings == null)
        GetCraftSettings();
      if (Input.GetMouseButtonUp(1))
      {
        var uiData = GetUIData(settingsUIName);
        if (uiData == null || uiData.canvasGroup == null)
          return;
        settings.showSettings = !settings.showSettings;
        if (settings.showSettings)
        {
          FadeCanvasGroup(uiData.canvasGroup, 1, settings.uiFadeSpeed);
        }
        else
        {
          FadeCanvasGroup(uiData.canvasGroup, 0, settings.uiFadeSpeed);
        }
      }
      else
      {
        craftSettings.runAutoScience = !craftSettings.runAutoScience;
        if (!craftSettings.runAutoScience)
        {
          icon = AssetLoader.GetAsset<Sprite>("icon56", "Icons/AutomatedScienceSampler", "AutomatedScienceSampler/AutomatedScienceSampler");//Utilities.GetTexture("icon56", "AutomatedScienceSampler/Textures");
        }
      }
      SaveSettings();
    }
    #endregion

    private void OnVesselChange(Vessel data)
    {
      Log("OnVesselChange");
      GetCraftSettings();
      UpdateShipInformation();
    }
    private void GoingEva(GameEvents.FromToAction<Part, Part> parts)
    {
      Log("GoingEva");
      parentVessel = parts.from.vessel;
      nextUpdate = Planetarium.GetUniversalTime() + 1;
    }
    void Update()
    {
      if (!isReady)
        return;
      if (!FlightGlobals.ready)
        return;
      if (craftSettings == null)
        GetCraftSettings();

      if (!craftSettings.runAutoScience)
        return;
      if (FlightGlobals.ActiveVessel.packed)
        return;
      if (!FlightGlobals.ActiveVessel.IsControllable)
        return;
      if (!CheckEVA())
        return;
      #region icon
      if (lastFrameCheck + frameCheck < Time.time)
      {
        var frame = Time.deltaTime / frameCheck;
        if (CurrentFrame + frame < 55)
          CurrentFrame += frame;
        else
          CurrentFrame = 0;
        icon = AssetLoader.GetAsset<Sprite>("icon" + (int)CurrentFrame, "Icons/AutomatedScienceSampler", "AutomatedScienceSampler/AutomatedScienceSampler");//Utilities.GetTexture("icon" + (int)CurrentFrame, "ForScienceContinued/Textures");
        lastFrameCheck = Time.time;
      }

      #endregion
      var sw = new System.Diagnostics.Stopwatch();
      sw.Start();
      if (nextUpdate == 0)
      {//add some delay so it doesnt run as soon as the vehicle launches
        if (!FlightGlobals.ready)
          return;
        nextUpdate = Planetarium.GetUniversalTime() + 1;
        UpdateShipInformation();
        return;
      }

      if (!FlightGlobals.ready || !Utilities.canVesselBeControlled(FlightGlobals.ActiveVessel))
        return;
      if (Planetarium.GetUniversalTime() < nextUpdate)
        return;
      nextUpdate = Planetarium.GetUniversalTime() + 1;
      Log(sw.Elapsed.TotalMilliseconds);
      foreach (var experiment in experiments)
      {
        IScienceActivator activator;
        if (!activators.TryGetValue(experiment.GetType(), out activator))
        {
          Log("Activator for ", experiment.GetType(), " not found! Using default!");
          activator = DefaultActivator.instance;
        }
        var subject = activator.GetScienceSubject(experiment);
        var value = activator.GetScienceValue(experiment, shipCotainsExperiments, subject);
        if (activator.CanRunExperiment(experiment, value))
        {
          Log("Deploying ", experiment.part.name, " for :", value, " science! ", subject.id);
          activator.DeployExperiment(experiment);
          AddToContainer(subject.id);
        }
        else if (BasicTransferCheck() && activator.CanTransfer(experiment, scienceContainers[craftSettings.currentContainer - 1]))
        {
          activator.Transfer(experiment, scienceContainers[craftSettings.currentContainer - 1]);
        }
        else if (craftSettings.resetExperiments && activator.CanReset(experiment))
        {
          activator.Reset(experiment);
        }

        Log("Experiment checked in: ", sw.Elapsed.TotalMilliseconds);
      }
      Log("Total: ", sw.Elapsed.TotalMilliseconds);
    }

    private bool BasicTransferCheck()
    {
      if (craftSettings.currentContainer == 0)
        return false;
      if (craftSettings.currentContainer > scienceContainers.Count)
        return false;
      if (scienceContainers[craftSettings.currentContainer - 1].vessel != FlightGlobals.ActiveVessel)
        return false;
      return true;
    }

    private bool CheckEVA()
    {
      if (FlightGlobals.ActiveVessel.isEVA)
      {
        if (kerbalEVAPart == null)
        {
          var kerbalEVAParts = FlightGlobals.ActiveVessel.FindPartModulesImplementing<KerbalEVA>();
          kerbalEVAPart = kerbalEVAParts.First();
        }
        if (craftSettings.doEVAOnlyIfGroundedWhenLanded && (parentVessel.Landed || parentVessel.Splashed) && (kerbalEVAPart.OnALadder || (!FlightGlobals.ActiveVessel.Landed && !FlightGlobals.ActiveVessel.Splashed)))
        {
          return false;
        }
      }

      return true;
    }
    private void UpdateShipInformation()
    {
      isReady = false;

      while (transferScienceUIElement.options.Count > 1)
      {
        transferScienceUIElement.options.RemoveAt(transferScienceUIElement.options.Count - 1);
      }
      experiments = FlightGlobals.ActiveVessel.FindPartModulesImplementing<ModuleScienceExperiment>();
      scienceContainers = FlightGlobals.ActiveVessel.FindPartModulesImplementing<ModuleScienceContainer>();
      shipCotainsExperiments.Clear();
      foreach (ModuleScienceContainer currentContainer in scienceContainers)
      {
        AddOptionToDropdown(transferScienceUIElement, currentContainer.part.partInfo.title);
        foreach (var data in currentContainer.GetData())
        {
          AddToContainer(data.subjectID);
        }
      }
      transferScienceUIElement.value = craftSettings.currentContainer;
      isReady = true;
    }

    private void AddToContainer(string subjectID, int add = 0)
    {
      if (shipCotainsExperiments.ContainsKey(subjectID))
      {
        Log(subjectID, "_Containing");
        shipCotainsExperiments[subjectID] = shipCotainsExperiments[subjectID] + 1 + add;
      }
      else
      {
        Log(subjectID, "_New");
        shipCotainsExperiments.Add(subjectID, add + 1);
      }
    }
    #region IToolbar
    public List<GameScenes> activeScences
    {
      get
      {
        return _activeScences;
      }
    }

    public UnityAction onClick
    {
      get
      {
        return OnToolbar;
      }
    }

    public Sprite icon
    {
      get
      {
        return _icon;
      }
      private set
      {
        if (_icon != value)
        {
          _icon = value;
          ToolbarBase.UpdateIcon(modName, icon);
        }
      }
    }

    #endregion
  }
}