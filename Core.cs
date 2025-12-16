using MelonLoader;
using GHPC;
using Reticle;
using GHPC.World;
using GHPC.Equipment.Optics;
using GHPC.State;

[assembly: MelonInfo(typeof(PreilluminateReticles.Core), "PreilluminateReticles", "1.0.0", "oilpeanut", "https://github.com/oilpeanut/PreilluminateReticles/releases/latest")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace PreilluminateReticles {
  public class Core : MelonMod {
    public override void OnInitializeMelon() {
      LoggerInstance.Msg("Initialized.");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName) {
      //makeing suring game is in a scene that has optics to illuminate
      if(sceneName.StartsWith("LOADER_") || sceneName.EndsWith("_Scene"))
        return;
      StateController.RunOrDefer(GameState.MissionUnitsLoaded, new GameStateEventHandler(FindAndIlluminate), GameStatePriority.Lowest);
    }

    public IEnumerator<bool> FindAndIlluminate(GameState gs) {
      //initializing stuff
      List<Unit> vehiclesInScene;
      UsableOptic[] parentOptics;
      ReticleMesh childReticleMesh;
      uint illumCount = 0;
      //digging through game object hierachy in the scene for a list of playable vehicles
      vehiclesInScene = [.. SceneUnitsManager.Instance.AllUnitsInScene];
      //iterates over the vehicles to find usable optics
      foreach (Unit obj in vehiclesInScene) {
        parentOptics = obj.gameObject.GetComponentsInChildren<UsableOptic>(true);
        if(parentOptics != null) { 
          foreach(UsableOptic optic in parentOptics) {
            //make sure the found optic is day sight
            if(optic.name == "FLIR" || optic.name == "NVS" || optic.name.Contains("night", StringComparison.CurrentCultureIgnoreCase))
              continue;
            childReticleMesh = optic.reticleMesh;
            //checks reticle illumination state and toggles accordingly
            if(!childReticleMesh.disableIllumination && childReticleMesh.lights[0].value == 0) {
              if(EnableIllumination(optic))
                illumCount++;
            }
          }
        }
      }
      LoggerInstance.Msg($"Illuminated {illumCount} optics");
      yield return true;
    }

    public bool EnableIllumination(UsableOptic optic) {
      if(optic.reticleMesh != null && !optic.reticleMesh.disableIllumination) {
        ReticleTree.Light.Type type = ReticleTree.Light.Type.NightIllumination;
        optic.reticleMesh.SetLight(type, 1f);
        return true;
      }
      return false;
    }
  }
}