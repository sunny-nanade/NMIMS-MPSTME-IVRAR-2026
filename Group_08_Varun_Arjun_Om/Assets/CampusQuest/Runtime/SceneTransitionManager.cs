using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace CampusQuest {
public class SceneTransitionManager : MonoBehaviour {
 public static SceneTransitionManager Instance {get;private set;}
 public DesktopPlayerController player;
 public int CurrentLevel {get;private set;}=0;
 public bool Busy {get;private set;}=true;
 public float Fade {get;private set;}=1;
 Scene environment;
 public string Status {get;private set;}="Loading campus...";
 void Awake(){Instance=this;}
 IEnumerator Start(){yield return Load(0,"start");}
 public void Travel(int level,string spawn){if(!Busy&&level>=-1&&level<=8)StartCoroutine(Load(level,spawn));}
 IEnumerator FadeTo(float target){float from=Fade;for(float t=0;t<.25f;t+=Time.unscaledDeltaTime){Fade=Mathf.Lerp(from,target,t/.25f);yield return null;}Fade=target;}
 IEnumerator Load(int level,string spawnID){
 Busy=true;if(DesktopHUD.Instance!=null)DesktopHUD.Instance.CloseMenus();Status="Loading level "+FloorConfig.Label(level);yield return FadeTo(1);
 string name=FloorConfig.SceneFor(level);
 if(!Application.CanStreamedLevelBeLoaded(name)){yield return Fail("Scene missing from Build Profiles: "+name);yield break;}
 Scene target=SceneManager.GetSceneByName(name); bool newlyLoaded=!target.isLoaded;
 if(newlyLoaded){
 AsyncOperation op=null;string error=null;
 try{op=SceneManager.LoadSceneAsync(name,LoadSceneMode.Additive);}catch(Exception ex){error=ex.Message;}
 if(op==null){yield return Fail(error??"Load failed");yield break;}
 yield return op;target=SceneManager.GetSceneByName(name);
 }
 SpawnPoint spawn=null;foreach(var root in target.GetRootGameObjects())foreach(var p in root.GetComponentsInChildren<SpawnPoint>(true))if(p.id==spawnID)spawn=p;
 if(spawn==null){if(newlyLoaded)yield return SceneManager.UnloadSceneAsync(target);yield return Fail("Missing spawn: "+spawnID+" in "+name);yield break;}
 int fromLevel=CurrentLevel;Scene old=environment;
 // Disable old colliders before placing the player; both scenes share coordinates.
 if(old.IsValid()&&old.isLoaded&&old!=target)foreach(var root in old.GetRootGameObjects())root.SetActive(false);
 SceneManager.SetActiveScene(target);player.Place(spawn.transform);environment=target;CurrentLevel=level;
 if(AtriumContextController.Instance!=null)AtriumContextController.Instance.SetLevel(level);
 if(old.IsValid()&&old.isLoaded&&old!=target)yield return SceneManager.UnloadSceneAsync(old);
 if(spawnID.StartsWith("lift:"))foreach(var root in target.GetRootGameObjects())foreach(var lift in root.GetComponentsInChildren<LiftController>())if("lift:"+lift.cabinID==spawnID)lift.OpenOnArrival();
 Status="Level "+FloorConfig.Label(level)+" | Approximate layout";
 yield return FadeTo(0);Busy=false;
 if(MissionManager.Instance!=null)MissionManager.Instance.Arrived(fromLevel,level,spawnID);
 }
 IEnumerator Fail(string message){Debug.LogError(message);Status=message;yield return FadeTo(0);Busy=false;if(DesktopHUD.Instance!=null)DesktopHUD.Instance.ShowInfo("Transition error",message);}
 void OnDestroy(){if(Instance==this)Instance=null;}
}
}
