using UnityEngine;
namespace CampusQuest {
public class HingedCampusDoor:MonoBehaviour {
 public Transform hinge;
 public BoxCollider leafCollider;
 public float openAngle=95,speed=100,width=1.1f;
 public bool openOnStart=true;
 bool open;float angle;
 void Awake(){open=openOnStart;angle=open?openAngle:0;Apply();}
 public void Toggle(){
 var player=SceneTransitionManager.Instance==null?null:SceneTransitionManager.Instance.player;
 if(open&&player!=null){var p=transform.InverseTransformPoint(player.transform.position);
 if(Mathf.Abs(p.x)<width+ .4f&&Mathf.Abs(p.z)<width+.4f){if(DesktopHUD.Instance!=null)DesktopHUD.Instance.ShowInfo("Door","Step clear of the doorway, then press E to close it.");return;}}
 open=!open;
 }
 void Update(){angle=Mathf.MoveTowards(angle,open?openAngle:0,speed*Time.deltaTime);Apply();}
 void Apply(){
 if(hinge==null)return;hinge.localRotation=Quaternion.Euler(0,angle,0);
 if(leafCollider==null)return;
 bool moving=Mathf.Abs(angle-(open?openAngle:0))>.05f;
 var p=SceneTransitionManager.Instance==null?null:SceneTransitionManager.Instance.player;
 // No moving collider pushes the camera. Wait until the player clears the leaf before enabling.
 var cc=p==null?null:p.GetComponent<CharacterController>();
 bool overlaps=false;
 if(cc!=null){var center=leafCollider.transform.TransformPoint(leafCollider.center);var half=Vector3.Scale(leafCollider.size*.5f,leafCollider.transform.lossyScale);var rot=leafCollider.transform.rotation;
 var delta=Quaternion.Inverse(rot)*(cc.bounds.center-center);var radius=cc.radius+.1f;
 overlaps=Mathf.Abs(delta.x)<half.x+radius&&Mathf.Abs(delta.z)<half.z+radius&&Mathf.Abs(delta.y)<half.y+cc.height*.5f;}
 leafCollider.enabled=!moving&&!overlaps;
 }
}
}
