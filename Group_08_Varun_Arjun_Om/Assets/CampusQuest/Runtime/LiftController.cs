using System.Collections;
using UnityEngine;
namespace CampusQuest {
public class LiftController : MonoBehaviour {
 public string cabinID;public int currentLevel;public LiftDoor door;public TextMesh indicator, cabinIndicator;
 public Vector3 cabinCentre;public Vector3 cabinSize=new Vector3(2.5f,3,2.4f);
 bool riding;
 bool Inside(){
  var manager=SceneTransitionManager.Instance;
  if(manager==null||manager.player==null)return false;
  var bounds=new Bounds(transform.TransformPoint(cabinCentre),Vector3.Scale(cabinSize,transform.lossyScale));
  // In XR the headset can move independently of the player root. Use the tracked
  // camera position so simulator/headset room movement is correctly recognized.
  var view=manager.player.view;
  var playerPoint=view!=null?view.transform.position:manager.player.transform.position;
  return bounds.Contains(playerPoint);
 }
 public void Call(){if(!riding)door.SetOpen(true);}
 public void RequestPanel(){if(riding)return;if(!Inside()){DesktopHUD.Instance.ShowInfo("Lift","Step inside the cabin to select a floor.");return;}DesktopHUD.Instance.ShowLift(this);}
 public void Select(int level){if(riding||level< -1||level>8)return;if(!Inside()){DesktopHUD.Instance.ShowInfo("Lift","Step inside first.");return;}DesktopHUD.Instance.CloseMenus();if(level==currentLevel){door.SetOpen(true);return;}StartCoroutine(Ride(level));}
 IEnumerator Ride(int destination){
 riding=true;door.SetOpen(false);
 // Keep the player in control: if they leave during closing, cancel the ride.
 yield return new WaitForSeconds(.65f);
 if(!Inside()){door.SetOpen(true);riding=false;yield break;}
 
 int step=destination>currentLevel?1:-1;
 for(int i=currentLevel;i!=destination;i+=step){if(!Inside()){door.SetOpen(true);riding=false;yield break;}SetDisplay(i);yield return new WaitForSeconds(.16f);}
 SetDisplay(destination);
 SceneTransitionManager.Instance.Travel(destination,"lift:"+cabinID);
 riding=false;
 }
 public void SetDisplay(int level){indicator.text=FloorConfig.Label(level);if(cabinIndicator!=null)cabinIndicator.text=FloorConfig.Label(level);}
 public void OpenOnArrival(){door.SetOpen(true);SetDisplay(currentLevel);}
}
}
