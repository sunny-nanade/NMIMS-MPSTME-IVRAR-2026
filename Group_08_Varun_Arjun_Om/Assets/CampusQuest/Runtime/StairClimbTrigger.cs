using UnityEngine;
namespace CampusQuest {
public class StairClimbTrigger : MonoBehaviour {
 public int destination;public string stairID;
 void OnTriggerEnter(Collider other){TryTransition(other);}
 void OnTriggerStay(Collider other){TryTransition(other);}
 void TryTransition(Collider other){
  var player=other.GetComponentInParent<DesktopPlayerController>();
  var transitions=SceneTransitionManager.Instance;
  var hud=DesktopHUD.Instance;
  // Stay is a recovery path if the player enters while a fade or another transition
  // is finishing; Travel itself ignores duplicate requests while Busy. Don't leave
  // a floor while the player is reading a menu or completing a mission screen.
  if(player!=null&&transitions!=null&&!transitions.Busy&&(hud==null||!hud.MenuOpen))
   transitions.Travel(destination,"stair:"+stairID);
 }
}
}
