using UnityEngine;
using MelonLoader;
using GHPC;
using Reticle;
using GHPC.World;
using GHPC.Equipment.Optics;
using GHPC.State;
using GHPC.Player;
using GHPC.Camera;
using System.Diagnostics;
using GHPC.Utility;

[assembly: MelonInfo(
  typeof(PreilluminateReticles.Core),
  "PreilluminateReticles",
  "1.1.0-testing",
  "oilpeanut",
  "https://github.com/oilpeanut/PreilluminateReticles/releases/latest"
)]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace PreilluminateReticles {
  using RLType = ReticleTree.Light.Type;

  public class Core : MelonMod {
    private readonly Stopwatch stopwatch = new();
    private readonly Dictionary<int, ReticleMesh[]> reticleMeshLookup = new();
    //for preserving reticle brightnesses across vanilla game modification
    //reticle instanceID => [{index of handledLightType => brightness}, ...]
    private readonly Dictionary<int, float[]> reticleStates = new();
    private readonly RLType[] handledLightTypes = [RLType.NightIllumination, RLType.Powered];
    private const float ILLUM_OFF_THRESHOLD = 0.0001f;
    private float brightnessStep = 0.1f;

    public override void OnInitializeMelon() {
      LoggerInstance.Msg("Initialized.");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName) {
      //makeing sure game is in a scene that has optics to illuminate
      if(
        sceneName.StartsWith("LOADER_") ||
        (Char.IsLower(sceneName, 1) && !sceneName.StartsWith("Flex_"))
        /*campaign missions tend to be prefixed with Flex_*/
      ) return;

      reticleMeshLookup.Clear();
      reticleStates.Clear();

      StateController.RunOrDefer(
        GameState.PlayerReady,
        new GameStateEventHandler(FindAndIlluminate),
        GameStatePriority.Lowest
      );
      CameraSlot.ActiveInstanceChanged += tryRestoreBrightness;
    }

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName) {
      base.OnSceneWasUnloaded(buildIndex, sceneName);
      CameraSlot.ActiveInstanceChanged -= tryRestoreBrightness;
    }

    public override void OnUpdate() {
      base.OnUpdate();

      float addend;
      if(Input.GetKeyDown(KeyCode.UpArrow))
        addend = brightnessStep;
      else if(Input.GetKeyDown(KeyCode.DownArrow))
        addend = - brightnessStep;
      else
        return;

      ReticleMesh[] reticleMeshes = getActiveReticleMeshes();
      if(reticleMeshes == null)
        return;

      float[] brightnesses;
      foreach(ReticleMesh reticleMesh in reticleMeshes) {
        retrieveReticleBrightnesses(reticleMesh, out brightnesses);
        //clone so it doesn't write 0 brightness directly to reticleStates without checks
        brightnesses = (float[])brightnesses.Clone();
        for(int i = 0; i < brightnesses.Length; i++) {
          if(float.IsNaN(brightnesses[i]))
            continue;
          brightnesses[i] += addend;
          if(brightnesses[i] < ILLUM_OFF_THRESHOLD)
            brightnesses[i] = 0;
        }
        storeReticleBrightnesses(reticleMesh, brightnesses);
        applyReticleBrightnesses(reticleMesh, brightnesses);
      }
    }

    public override void OnLateUpdate() {
      base.OnLateUpdate();
      float[] brightnesses;
      float gameSetBrightness;
      ReticleMesh[] activeReticles;

      if(InputUtil.MainPlayer?.GetButtonDown("Reticle Illumination") ?? false) {
        activeReticles = getActiveReticleMeshes();
        if(activeReticles == null)
          return;

        foreach(ReticleMesh rm in activeReticles) {
          //only apply stored brightness when game turns illum on
          rm.GetLight(RLType.NightIllumination, out gameSetBrightness);
          if(retrieveReticleBrightnesses(rm, out brightnesses) && gameSetBrightness >= ILLUM_OFF_THRESHOLD)
            applyReticleBrightnesses(rm, brightnesses);
        }
      }
    }

    private ReticleMesh[] getActiveReticleMeshes() {
      UsableOptic activeOptic = CameraSlot.ActiveInstance?.PairedOptic;
      ReticleMesh[] rms = null;
      if(activeOptic == null)
        return null;
      reticleMeshLookup.TryGetValue(activeOptic.GetInstanceID(), out rms);
      return rms;
    }

    private bool retrieveReticleBrightnesses(ReticleMesh rm, out float[] brightnesses, bool doGetLight = true) {
      bool hasRecord = reticleStates.TryGetValue(rm.GetInstanceID(), out brightnesses);
      if(!hasRecord && doGetLight) {
        brightnesses = new float[handledLightTypes.Length];
        for(int i = 0; i < handledLightTypes.Length; i++)
          rm.GetLight(handledLightTypes[i], out brightnesses[i]);
      }
      return hasRecord;
    }

    private void applyReticleBrightnesses(ReticleMesh rm, float[] brightnesses) {
      for(int i = 0; i < handledLightTypes.Length; i++) {
        if(brightnesses[i] == float.NaN)
          continue;
        rm.SetLight(handledLightTypes[i], neutralizeNightBrightness(brightnesses[i], rm));
      }
    }

    private void storeReticleBrightnesses(ReticleMesh rm, float[] brightnesses) {
      //remember last night illum brightness
      if(brightnesses[0] >= ILLUM_OFF_THRESHOLD)
        reticleStates[rm.GetInstanceID()] = brightnesses;
    }

    //setLight() brightness argument is multiplied by nightBrightness at night
    private float neutralizeNightBrightness(float brightness, ReticleMesh mesh) {
      if(CelestialSky.IsCurrentlyDaytime())
        return brightness;
      brightness /= mesh.nightBrightness;
      return brightness >= ILLUM_OFF_THRESHOLD ? brightness : 0;
    }

    private void tryRestoreBrightness(CameraSlot cs) {
      float[] brightnesses;
      bool reticleBrightnessChanged;
      ReticleMesh[] rms = getActiveReticleMeshes();
      if(rms == null)
        return;
      foreach(ReticleMesh rm in rms) {
        reticleBrightnessChanged = retrieveReticleBrightnesses(rm, out brightnesses, false);
        if(!reticleBrightnessChanged)
          continue;
        applyReticleBrightnesses(rm, brightnesses);
      }
    }

    public IEnumerator<bool> FindAndIlluminate(GameState gs) {
      swStart();
      List<Unit> vehiclesInTeam;
      UsableOptic[] parentOptics;
      ReticleMesh[] childReticleMeshes;
      Faction playerFaction;
      uint illumCount = 0;

      //get list of vehicles in player's faction
      playerFaction = PlayerInput.Instance.CurrentPlayerUnit.Allegiance;
      vehiclesInTeam = SceneUnitsManager.AllUnitsByFaction[(int)playerFaction];

      //iterates over the vehicles to find usable optics
      foreach (Unit vic in vehiclesInTeam) {
        parentOptics = vic.gameObject.GetComponentsInChildren<UsableOptic>(true);
        if(parentOptics != null) { 
          foreach(UsableOptic optic in parentOptics) {

            //caching meshes for later use
            childReticleMeshes = optic.gameObject.GetComponentsInChildren<ReticleMesh>(true);
            reticleMeshLookup.Add(optic.GetInstanceID(), childReticleMeshes);

            //make sure the found optic is day sight
            if(
              optic.name == "FLIR" ||
              optic.name == "NVS" ||
              optic.name.Contains("night", StringComparison.CurrentCultureIgnoreCase)
            ) continue;

            //illuminate reticle meshes in the optics
            foreach(ReticleMesh reticleMesh in childReticleMeshes) {
              if(!reticleMesh.disableIllumination) {
                reticleMesh.SetLight(RLType.NightIllumination, 1f);
                illumCount++;
              }
            }

          }
        }
      }

      swStop();
      LoggerInstance.Msg($"Illuminated {illumCount} reticles.");
      yield return true;
    }

    [Conditional("DEBUG")]
    private void swStart() =>
      stopwatch.Restart();

    [Conditional("DEBUG")]
    private void swStop() {
      stopwatch.Stop();
      LoggerInstance.Msg($"Illumination took {stopwatch.ElapsedMilliseconds} ms.");
    }
  }
}