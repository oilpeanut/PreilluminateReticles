using MelonLoader;
using GHPC;
using Reticle;
using GHPC.World;
using GHPC.Equipment.Optics;
using GHPC.State;
using GHPC.Player;

[assembly: MelonInfo(typeof(PreilluminateReticles.Core), "PreilluminateReticles", "1.0.1-testing", "oilpeanut", "https://github.com/oilpeanut/PreilluminateReticles/releases/latest")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace PreilluminateReticles {
  public class Core : MelonMod {
    public override void OnInitializeMelon() {
      LoggerInstance.Msg("Initialized.");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName) {
      //makeing sure game is in a scene that has optics to illuminate
      LoggerInstance.Msg($"Current scene: {sceneName}");
      if(sceneName.StartsWith("LOADER_") || (Char.IsLower(sceneName, 1) && !sceneName.StartsWith("Flex_")/*campaign missions tend to be prefixed with this*/))
        return;
      StateController.RunOrDefer(GameState.PlayerReady, new GameStateEventHandler(FindAndIlluminate), GameStatePriority.Lowest);
    }

    public IEnumerator<bool> FindAndIlluminate(GameState gs) {
      //initializing stuff
      List<Unit> vehiclesInTeam;
      UsableOptic[] parentOptics;
      ReticleMesh[] childReticleMeshes;
      ReticleEditor.LightState reticleLight;
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
            //make sure the found optic is day sight
            if(optic.name == "FLIR" || optic.name == "NVS" || optic.name.Contains("night", StringComparison.CurrentCultureIgnoreCase))
              continue;
            childReticleMeshes = optic.gameObject.GetComponentsInChildren<ReticleMesh>(true);
            //going through reticle meshes in the optics
            foreach(ReticleMesh reticleMesh in childReticleMeshes) {
              reticleLight = reticleMesh.lights[0];
              //checks that the reticle can and hasn't been illuminated
              if(!reticleMesh.disableIllumination && reticleLight.light.type == ReticleTree.Light.Type.NightIllumination && reticleLight.value == 0) {
                LoggerInstance.Msg($"\nVehicle: {vic.name}\nOptics: {optic.name}\nReticle: {reticleMesh.name}");
                if(EnableIllumination(reticleMesh))
                  illumCount++;
              }
            }
          }
        }
      }
      LoggerInstance.Msg($"Illuminated {illumCount} reticles");
      yield return true;
    }

    public bool EnableIllumination(ReticleMesh rm) {
      if(rm != null && !rm.disableIllumination) {
        ReticleTree.Light.Type type = ReticleTree.Light.Type.NightIllumination;
        rm.SetLight(type, 1f);
        return true;
      }
      return false;
    }
  }
}