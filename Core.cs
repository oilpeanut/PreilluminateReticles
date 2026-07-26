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
    private readonly RLType[] handledLightTypes = [RLType.NightIllumination, RLType.Powered];

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

      StateController.RunOrDefer(
        GameState.PlayerReady,
        new GameStateEventHandler(FindAndIlluminate),
        GameStatePriority.Lowest
      );
    }

    public override void OnUpdate() {
      base.OnUpdate();
      UsableOptic activeOptic;
      ReticleMesh[] reticleMeshes;
      float addend, brightness;

      if(Input.GetKeyDown(KeyCode.UpArrow))
        addend = 0.2f;
      else if(Input.GetKeyDown(KeyCode.DownArrow))
        addend = -0.2f;
      else
        return;

      activeOptic = CameraSlot.ActiveInstance?.PairedOptic;
      if(activeOptic == null)
        return;

      reticleMeshes = reticleMeshLookup[activeOptic.GetInstanceID()];
      if(reticleMeshes == null)
        return;

      foreach(ReticleMesh reticleMesh in reticleMeshes) {
        foreach(RLType lightType in handledLightTypes) {
          reticleMesh.GetLight(lightType, out brightness);
          if(brightness == float.NaN)
            continue;

          brightness += addend;
          if(brightness < 0)
            brightness = 0;
          reticleMesh.SetLight(lightType, brightness);
        }
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