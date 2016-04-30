﻿using DMagic.Part_Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KerboKatz;
using DMagic;

namespace KerboKatz.ASS
{
  public class Activator : IScienceActivator
  {
    AutomatedScienceSampler _AutomatedScienceSamplerInstance;
    AutomatedScienceSampler IScienceActivator.AutomatedScienceSampler
    {
      get { return _AutomatedScienceSamplerInstance; }
      set { _AutomatedScienceSamplerInstance = value; }
    }

    public bool CanRunExperiment(ModuleScienceExperiment baseExperiment, float currentScienceValue)
    {
      var currentExperiment = baseExperiment as DMModuleScienceAnimate;
      if (currentScienceValue < _AutomatedScienceSamplerInstance.craftSettings.threshold)
      {
        _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Science value is less than cutoff threshold: ", currentScienceValue, "<", _AutomatedScienceSamplerInstance.craftSettings.threshold);
        return false;
      }
      if (!currentExperiment.rerunnable && !_AutomatedScienceSamplerInstance.craftSettings.oneTimeOnly)
      {
        _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Runing rerunable experiments is disabled");
        return false;
      }
      if (currentExperiment.Inoperable)
      {
        _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Experiment is inoperable");
        return false;
      }
      if (currentExperiment.Deployed)
      {
        _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Experiment is deployed");
        return false;
      }
      if (!currentExperiment.experiment.IsUnlocked())
      {
        _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Experiment is locked");
        return false;
      }
      return DMAPI.experimentCanConduct(currentExperiment);
    }

    public void DeployExperiment(ModuleScienceExperiment baseExperiment)
    {
      var currentExperiment = baseExperiment as DMModuleScienceAnimate;
      DMAPI.deployDMExperiment(currentExperiment, _AutomatedScienceSamplerInstance.craftSettings.hideScienceDialog);
    }

    public ScienceSubject GetScienceSubject(ModuleScienceExperiment baseExperiment)
    {
      
      var currentExperiment = baseExperiment as DMModuleScienceAnimate;
      if (DMAPI.isAsteroidGrappled(baseExperiment))
      {
        return DMAPI.getAsteroidSubject(currentExperiment);
      }
      else
      {
        ExperimentSituations situation = ScienceUtil.GetExperimentSituation(FlightGlobals.ActiveVessel);
        var biome = DMAPI.getBiome(baseExperiment, situation);
        _AutomatedScienceSamplerInstance.Log(biome, "_", situation, "_", ResearchAndDevelopment.GetExperimentSubject(ResearchAndDevelopment.GetExperiment(currentExperiment.experimentID), situation, FlightGlobals.currentMainBody, biome) == null);
        return ResearchAndDevelopment.GetExperimentSubject(ResearchAndDevelopment.GetExperiment(currentExperiment.experimentID), situation, FlightGlobals.currentMainBody, biome);
      }
    }

    public float GetScienceValue(ModuleScienceExperiment baseExperiment, Dictionary<string, int> shipCotainsExperiments, ScienceSubject currentScienceSubject)
    {
      var currentExperiment = baseExperiment as DMModuleScienceAnimate;
      var scienceExperiment = ResearchAndDevelopment.GetExperiment(baseExperiment.experimentID);
      if (DMAPI.isAsteroidGrappled(currentExperiment))
      {
        return Utilities.Science.GetScienceValue(shipCotainsExperiments, scienceExperiment, currentScienceSubject/*, null, GetNextScienceValue*/);
      }
      else
      {
        return Utilities.Science.GetScienceValue(shipCotainsExperiments, scienceExperiment, currentScienceSubject);
      }
    }
    public List<Type> GetValidTypes()
    {
      var types = new List<Type>();
      types.Add(typeof(DMModuleScienceAnimate));

      Utilities.LoopTroughAssemblies((type) =>
      {
        if (type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(DMModuleScienceAnimate)))
        {
          types.Add(type);
        }
      });
      return types;
    }
    public bool CanReset(ModuleScienceExperiment baseExperiment)
    {
      var currentExperiment = baseExperiment as DMModuleScienceAnimate;
      if (!currentExperiment.Inoperable)
      {
        _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Experiment isn't inoperable");
        return false;
      }
      if (!currentExperiment.Deployed)
      {
        _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Experiment isn't deployed!");
        return false;
      }
      if ((currentExperiment as IScienceDataContainer).GetScienceCount() > 0)
      {
        _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Experiment has data!");
        return false;
      }
      if (!currentExperiment.resettable)
      {
        _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Experiment isn't resetable");
        return false;
      }
      bool hasScientist = false;
      foreach (var crew in FlightGlobals.ActiveVessel.GetVesselCrew())
      {
        if (crew.trait == "Scientist")
        {
          hasScientist = true;
          break;
        }
      }
      if (!hasScientist)
      {
        _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Vessel has no scientist");
        return false;
      }
      return true;
    }

    public void Reset(ModuleScienceExperiment baseExperiment)
    {
      var currentExperiment = baseExperiment as DMModuleScienceAnimate;
      _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Reseting experiment");
      currentExperiment.ResetExperiment();
    }

    private string CurrentBiome(DMModuleScienceAnimate baseModuleExperiment)
    {
      var experimentSituation = ScienceUtil.GetExperimentSituation(FlightGlobals.ActiveVessel);
      if (!baseModuleExperiment.experiment.BiomeIsRelevantWhile(experimentSituation))
        return string.Empty;
      
      if ((baseModuleExperiment.bioMask & (int)experimentSituation) == 0)
        return string.Empty;

      var currentVessel = FlightGlobals.ActiveVessel;
      var currentBody = FlightGlobals.currentMainBody;
      if (currentVessel != null && currentBody != null)
      {
        if (!string.IsNullOrEmpty(currentVessel.landedAt))
        {
          //big thanks to xEvilReeperx for this one.
          return Vessel.GetLandedAtString(currentVessel.landedAt);
        }
        else
        {
          return ScienceUtil.GetExperimentBiome(currentBody, currentVessel.latitude, currentVessel.longitude);
        }
      }
      else
      {
        _AutomatedScienceSamplerInstance.Log("currentVessel && currentBody == null");
      }
      return string.Empty;
    }

    public bool CanTransfer(ModuleScienceExperiment baseExperiment, ModuleScienceContainer moduleScienceContainer)
    {
      var currentExperiment = baseExperiment as DMModuleScienceAnimate;
      if ((currentExperiment as IScienceDataContainer).GetScienceCount() == 0)
      {
        _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Experiment has no data skiping transfer. Data found: ", (currentExperiment as IScienceDataContainer).GetScienceCount(),"_", currentExperiment.experimentNumber);
        return false;
      }
      if (!currentExperiment.IsRerunnable())
      {
        if (!_AutomatedScienceSamplerInstance.craftSettings.transferAllData)
        {
          _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Experiment isn't rerunnable and transferAllData is turned off.");
          return false;
        }
      }
      if (!_AutomatedScienceSamplerInstance.craftSettings.dumpDuplicates)
      {
        foreach (var data in (currentExperiment as IScienceDataContainer).GetData())
        {
          if (moduleScienceContainer.HasData(data))
          {
            _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": Target already has experiment and dumping is disabled.");
            return false;
          }
        }
      }
      _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": We can transfer the science!");
      return true;
    }
    public void Transfer(ModuleScienceExperiment baseExperiment, ModuleScienceContainer moduleScienceContainer)
    {
      var currentExperiment = baseExperiment as DMModuleScienceAnimate;
      _AutomatedScienceSamplerInstance.Log(currentExperiment.experimentID, ": transfering");
      moduleScienceContainer.StoreData(new List<IScienceDataContainer>() { currentExperiment as DMModuleScienceAnimate }, _AutomatedScienceSamplerInstance.craftSettings.dumpDuplicates);
    }
  }
}
