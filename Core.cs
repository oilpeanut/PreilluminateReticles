using MelonLoader;
using GHPC;
using Reticle;
using GHPC.World;
using GHPC.Equipment.Optics;
using GHPC.State;
using GHPC.Player;
using GHPC.Camera;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: MelonInfo(
  typeof(PreilluminateReticles.Core),
  "PreilluminateReticles",
  "1.0.1",
  "oilpeanut",
  "https://github.com/oilpeanut/PreilluminateReticles/releases/latest"
)]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace PreilluminateReticles {
  public class Core : MelonMod {
    private ConditionalWeakTable<UsableOptic, ReticleMesh[]> reticleMeshLookup = new();

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
      UsableOptic activeOptic = CameraSlot.ActiveInstance?.PairedOptic;
      ReticleMesh[] reticleMeshes;
      float addend, brightness;

      if(activeOptic == null)
        return;
      if(Input.GetKeyDown(KeyCode.UpArrow))
        addend = 0.5f;
      else if(Input.GetKeyDown(KeyCode.DownArrow))
        addend = -0.5f;
      else
        return;

      reticleMeshLookup.TryGetValue(activeOptic, out reticleMeshes);
      if(reticleMeshes == null)
        return;

      foreach(ReticleMesh reticleMesh in reticleMeshes) {
        reticleMesh.GetLight(ReticleTree.Light.Type.NightIllumination, out brightness);
        if(brightness == float.NaN)
          continue;
        brightness += addend;
        if(brightness < 0)
          brightness = 0;
        reticleMesh.SetLight(ReticleTree.Light.Type.NightIllumination, brightness);
      }
    }
    
    public IEnumerator<bool> FindAndIlluminate(GameState gs) {
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
            reticleMeshLookup.Add(optic, childReticleMeshes);

            //make sure the found optic is day sight
            if(
              optic.name == "FLIR" ||
              optic.name == "NVS" ||
              optic.name.Contains("night", StringComparison.CurrentCultureIgnoreCase)
            ) continue;

            //illuminate reticle meshes in the optics
            foreach(ReticleMesh reticleMesh in childReticleMeshes) {
              if(!reticleMesh.disableIllumination) {
                reticleMesh.SetLight(ReticleTree.Light.Type.NightIllumination, 1f);
                illumCount++;
              }
            }

          }
        }
      }

      LoggerInstance.Msg($"Illuminated {illumCount} reticles");
      yield return true;
    }
  }
}