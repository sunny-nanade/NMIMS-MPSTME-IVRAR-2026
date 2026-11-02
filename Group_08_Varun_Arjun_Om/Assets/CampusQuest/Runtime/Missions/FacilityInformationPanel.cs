using UnityEngine;
namespace CampusQuest {
public class FacilityInformationPanel:MonoBehaviour {
 public FacilityData facility;
 public void Open(){
 if(facility==null)return;var m=MissionManager.Instance;
 if(m!=null)m.Visit(facility);
 if(DesktopHUD.Instance!=null)DesktopHUD.Instance.ShowInfo(facility.DisplayName,
 facility.learningText+"\n\nFloor: "+FloorConfig.Label(facility.Level)+(facility.Verified?"":"\nPRACTICE TARGET: room assignment or location is provisional.")+"\nGeometry and dimensions are approximate.");
 }
}
}
