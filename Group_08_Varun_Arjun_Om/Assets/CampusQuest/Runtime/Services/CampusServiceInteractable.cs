using UnityEngine;
namespace CampusQuest {
public enum CampusServiceAction { ChooseBook,IssueBook,CollectBook,ChooseComponent,IssueComponent,CollectComponent,Print,CollectPrint,Deliver }
public class CampusServiceInteractable:MonoBehaviour {
 public CampusServiceAction action;public int selection;public int level;public FacilityData facility;
 public void Use(){if(CampusServicesManager.Instance!=null)CampusServicesManager.Instance.Use(this);}
 }
}
