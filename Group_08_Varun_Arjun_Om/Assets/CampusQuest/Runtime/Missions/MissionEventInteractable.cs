using UnityEngine;
namespace CampusQuest {
public class MissionEventInteractable:MonoBehaviour {
 public string eventID,title;
 public FacilityData target;
 [TextArea]public string body;
 public void Open(){if(MissionManager.Instance!=null)MissionManager.Instance.Signal(eventID);if(DesktopHUD.Instance!=null)DesktopHUD.Instance.ShowInfo(title,body+(target==null?"":"\nDestination: "+target.DisplayName+" — Floor "+FloorConfig.Label(target.Level)));}
}
}
