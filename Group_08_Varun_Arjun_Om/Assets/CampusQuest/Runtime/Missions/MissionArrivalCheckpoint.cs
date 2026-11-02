using UnityEngine;
namespace CampusQuest {
public class MissionArrivalCheckpoint:MonoBehaviour {
 public FacilityData facility;
 void OnTriggerEnter(Collider other){if(other.GetComponentInParent<DesktopPlayerController>()!=null&&MissionManager.Instance!=null)MissionManager.Instance.Visit(facility);}
 void OnTriggerStay(Collider other){if(other.GetComponentInParent<DesktopPlayerController>()!=null&&MissionManager.Instance!=null&&!MissionManager.Instance.Visited(facility))MissionManager.Instance.Visit(facility);}
}
}
